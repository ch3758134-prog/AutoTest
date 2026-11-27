using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

public sealed partial class AutomaticTestHomeViewModel
{
    private readonly SemaphoreSlim _catalogScanGate = new(1, 1);

    /// <summary>
    /// Physical COM observation -> unique installed version -> production plan
    /// selection. No physical START/fixture/FLASH or inspection report is
    /// issued here. One station handles only one candidate at a time.
    /// </summary>
    private async Task InspectCatalogBarcodeAsync(string raw)
    {
        var revision = Interlocked.Increment(ref _scanRevision);
        var observedAt = DateTimeOffset.UtcNow;
        var port = _scanner.ConnectedPort;
        var stationEpoch = _hardware.CurrentStationEpoch;
        await _catalogScanGate.WaitAsync();
        try
        {
            bool CurrentScan() =>
                _isProductionWorkspace() &&
                revision == _scanRevision &&
                _scanner.IsConnected &&
                string.Equals(port, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(raw, _lastObservedBarcode, StringComparison.Ordinal);
            if (!CurrentScan() || IsOperatorRunning) return;
            _prepared = null;
            _acceptedStationEpoch = 0;
            _productionVerdict = "等待检测结果";
            var routed = await _productionCatalog.ResolveAsync(raw);
            if (!CurrentScan()) return;
            if (!routed.Accepted || routed.Package is null)
            {
                Reject(routed.Message);
                return;
            }
            // Every scan rechecks the exact contents, including immutable
            // firmware/security files and TEST/DBC/WAVE resource closure.
            await _productionCatalog.VerifySelectionAsync(routed.Package);
            if (!CurrentScan()) return;
            if (!await _activateCatalogPackage(routed.Package))
            {
                Reject("工位忙碌或工程未就绪，请完成当前操作后重新扫码。");
                return;
            }
            // Selecting a new plan invalidates prior scan state through the
            // ordinary plan event. The new physical observation still must
            // match, and no newer observation may have arrived.
            if (!_isProductionWorkspace() || !_scanner.IsConnected ||
                !string.Equals(port, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(raw, _lastObservedBarcode, StringComparison.Ordinal))
                return;
            var acceptedRevision = Interlocked.Increment(ref _scanRevision);
            var plan = _plans.Current;
            if (plan is null || !plan.ResourceReady ||
                plan.Project.Definition.Id.Value != routed.Package.ProjectId ||
                !string.Equals(plan.Project.ManifestPath, routed.Package.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Reject("检测工程未能完整加载，请检查导入的工程包。");
                return;
            }

            // Identity routing is useful even before the station has its
            // physical START and mechanical feedback connected. Present it,
            // but do not create a formal run or claim admission.
            _dutSerial = routed.FullBarcode;
            _scanStatus = "已识别 ECU " + routed.FullBarcode +
                " · 检测工程：" + routed.Package.ProjectName +
                "（修订 " + routed.Package.ProjectRevision + "）。";
            _scanAlertRequired = false;
            Notify();

            // Device configuration is independent of product mapping.
            // It is checked here but cannot itself authorize formal START.
            var station = await StationDeviceConfigurationStore.LoadAsync();
            station.Fixture.Validate();
            _ = StationFixtureExecutionPlan.From(station.Fixture);
            var device = await Task.Run(() =>
            {
                var saved = StationScannerDeviceBinding.Load();
                return saved is null ? null : StationScannerDeviceBinding.Match(saved);
            });
            // A real connected COM scanner is sufficient: a prior manually
            // saved PnP binding is optional and does not grant execution rights.
            if (device is not null && !string.Equals(device.PortName, port,
                    StringComparison.OrdinalIgnoreCase))
                device = null;
            var prepared = await ProductionPreparationService.PrepareAsync(
                plan.Project, routed.Marker, routed.FullBarcode,
                requireProductionDeployment: true);
            if (!prepared.ConfigurationReady || prepared.Preparation is null)
            {
                _scanStatus = "已选择检测工程，但资源检查未通过：" +
                    prepared.Explanation;
                _scanAlertRequired = true;
                Notify();
                return;
            }
            if (acceptedRevision != _scanRevision ||
                _hardware.CurrentStationEpoch != stationEpoch ||
                !string.Equals(raw, _lastObservedBarcode, StringComparison.Ordinal) ||
                !_scanner.IsConnected ||
                !string.Equals(port, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase))
            {
                Reject("扫码期间设备状态发生变化，请重新扫码。");
                return;
            }
            var latestRoute = await _productionCatalog.ResolveAsync(raw);
            if (!latestRoute.Accepted || latestRoute.Package is null ||
                latestRoute.Package.PackageSha256 != routed.Package.PackageSha256 ||
                latestRoute.RuleRevision != routed.RuleRevision)
            {
                Reject("扫码识别规则已变化，请重新扫码。");
                return;
            }
            var serialized = JsonSerializer.Serialize(new
            {
                Schema = "FIXED_MARKER_V1",
                Marker = routed.Marker,
                Separator = "-",
                PackageSha256 = routed.Package.PackageSha256,
                Revision = routed.RuleRevision
            });
            var ruleHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
            _acceptedStationEpoch = stationEpoch;
            _lastIdentity = routed.FullBarcode;
            _prepared = prepared.Preparation with
            {
                AcceptedScannerObservation = new FrozenScanIdentity(
                    Guid.NewGuid(), observedAt, port, raw,
                    FrozenScanSource.ProductionFixedMarker, serialized, ruleHash,
                    routed.Marker, routed.RuleRevision,
                    routed.Package.PackageSha256, station.Fixture.StationSlotId,
                    device?.PnpInstanceId ?? "", FingerprintEngineeringFixture(station.Fixture))
            };
            _scanStatus = "ECU 编号：" + routed.FullBarcode + " · 工程：" +
                routed.Package.ProjectName + "。可以开始检测。";
            _scanAlertRequired = false;
            Notify();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Never treat a failed install/routing operation as a test result.
            Reject("工程匹配或加载失败：" + error.Message);
        }
        finally { _catalogScanGate.Release(); }

        void Reject(string reason)
        {
            _prepared = null;
            _acceptedStationEpoch = 0;
                _dutSerial = "等待扫码";
            _scanStatus = reason;
            _scanAlertRequired = true;
            Notify();
        }
    }
}
