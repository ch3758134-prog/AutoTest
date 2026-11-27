using System.ComponentModel;
using System.IO;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Operator identity is projected from the real station scanner and its
/// explicitly deployed ECU project. Scanning is only observation and offline
/// configuration preflight: no new Inspection, START, flash or fixture action.
/// </summary>
public sealed partial class AutomaticTestHomeViewModel
{
    private long _scanRevision;
    private long _acceptedStationEpoch;
    private string _dutSerial = "等待扫码";
    private string _scanStatus = "请使用工位扫码枪扫描 ECU 编号";
    private bool _scanAlertRequired;
    private string _lastObservedBarcode = "";
    private string _lastObservedAt = "";

    // Raw observation is intentionally independent of identity matching,
    // published rules, an open ECU project, and permission to run hardware.
    public string LastObservedBarcode => string.IsNullOrEmpty(_lastObservedBarcode)
        ? "尚未收到扫码内容" : _lastObservedBarcode;
    public string LastObservedAt => _lastObservedAt;
    private string? _lastIdentity;
    private FrozenProductionPreparation? _prepared;
    private IReadOnlyDictionary<string, string> _scanMappedFields =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Operator-facing trace only; no formal RECORD is claimed
    /// until the production run and evidence writer are connected.</summary>
    public string ScanTraceSummary => _scanMappedFields.Count == 0 ? "" :
        "本次扫码字段：" + string.Join(" · ",
            _scanMappedFields.Select(x => x.Key + "=" + x.Value));

    private void OnScannerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SerialScannerWorkbenchViewModel.IsConnected) &&
            !_scanner.IsConnected)
            OnUi(() => InvalidateScan("扫码枪断开，本次扫码身份失效；恢复后请重新扫码。"));
        else if (e.PropertyName == nameof(SerialScannerWorkbenchViewModel.ConnectedPort))
            OnUi(() =>
            {
                // Scanner COM may be re-bound without an observable period of
                // IsConnected=false. The accepted observation is device-scoped.
                if ((_prepared?.AcceptedScannerObservation is { } scan &&
                     !string.Equals(scan.ScannerPort, _scanner.ConnectedPort,
                         StringComparison.OrdinalIgnoreCase)) ||
                    _dutSerial == "正在核对")
                    InvalidateScan("扫码枪 COM 端口已切换或当前扫码预检被打断；请重新扫码。");
                else UpdateConnection();
            });
        else if (e.PropertyName == nameof(SerialScannerWorkbenchViewModel.IsConnected))
            OnUi(UpdateConnection);
        else if (e.PropertyName == nameof(SerialScannerWorkbenchViewModel.Status))
            OnUi(Notify);
    }

    private void OnIncompleteBarcodeObserved(string raw) => OnUi(() =>
    {
        // Never treat an unterminated serial packet as an accepted DUT.
        // Showing its bytes is diagnostics only, so invalidate any prior
        // accepted identity rather than leaving a stale Start enabled.
        InvalidateScan("串口已收到内容，但未检测到 CR/LF 结束符；" +
            "请检查扫码枪串口结束符设置。本次还不能开始测试。");
        _lastObservedBarcode = raw;
        _lastObservedAt = DateTimeOffset.Now.ToString("HH:mm:ss") + " · 未完整";
        Notify();
    });

    private void OnRawBarcodeObserved(string raw) => OnUi(() =>
    {
        // A different physical label was scanned. Even if the station is
        // currently unable to admit its identity, the previous DUT must not
        // remain startable after a possible ECU replacement.
        var previous = _prepared?.AcceptedScannerObservation?.RawBarcode;
        if (!IsOperatorRunning && previous is not null &&
            !string.Equals(previous, raw, StringComparison.Ordinal))
            InvalidateScan("收到新的扫码原文，旧 DUT 身份已失效；" +
                "新编号尚未通过工程和工位预检。");
        _lastObservedBarcode = raw;
        _lastObservedAt = DateTimeOffset.Now.ToString("HH:mm:ss");
        Notify();
    });

    private void OnCompleteBarcodeReceived(string raw) => OnUi(() =>
    {
        if (string.IsNullOrEmpty(raw)) return;
        // Always echo the physical COM observation before any business gate.
        // A rejected identity or missing publication must never look like
        // the scanner was ignored. A scan alone never starts physical work.
        _lastObservedBarcode = raw;
        _lastObservedAt = DateTimeOffset.Now.ToString("HH:mm:ss");
        if (IsOperatorRunning)
        {
            _scanAlertRequired = false;
            _scanStatus = "已收到扫码原文；工位正在执行，不能替换本次 ECU 身份。";
            Notify();
            return;
        }
        if (!_isProductionWorkspace() && !_engineeringVerificationArmed)
        {
            _scanAlertRequired = false;
            _scanStatus = "已收到扫码；请进入工程测试后开始运行。";
            Notify();
            return;
        }
        if (_isProductionWorkspace())
        {
            // Production resolves the ECU to a verified installed package.
            // It must not depend on an engineer having opened that project.
            _scanAlertRequired = false;
            _scanStatus = "正在根据扫码内容查找检测工程。";
            Notify();
            _ = InspectCatalogBarcodeAsync(raw);
            return;
        }
        if (_plans.Current is null)
        {
            _scanAlertRequired = true;
            _scanStatus = "已收到扫码，但没有可用的检测工程。请先打开工程。";
            Notify();
            return;
        }
        _scanAlertRequired = false;
        _scanStatus = "已收到扫码，正在核对 ECU 编号与检测工程。";
        Notify();
        _ = InspectRealBarcodeAsync(raw);
    });

    private static void OnUi(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    internal void OnPublishedConfigurationChanged() => OnUi(() =>
        InvalidateScan("生产编码与工位发布版本已改变；旧扫码冻结身份作废，请重新扫码。"));

    private void InvalidateScan(string reason, bool requiresAttention = true)
    {
        Interlocked.Increment(ref _scanRevision);
        _prepared = null;
        _acceptedStationEpoch = 0;
        _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        _dutSerial = "等待扫码";
        _lastIdentity = null;
        _scanStatus = reason;
        _scanAlertRequired = requiresAttention;
        Notify();
    }

    private async Task InspectRealBarcodeAsync(string raw)
    {
        var revision = Interlocked.Increment(ref _scanRevision);
        var scannedAt = DateTimeOffset.UtcNow;
        var scannerPort = _scanner.ConnectedPort;
        var initialCanTarget = _hardware.RequestedEcuCanBitrateKbps;
        var initialStationEpoch = _hardware.CurrentStationEpoch;
        var plan = _plans.Current;
        if (_isProductionWorkspace() || plan is null || !_scanner.IsConnected || string.IsNullOrWhiteSpace(raw))
            return;
        _prepared = null;
        _acceptedStationEpoch = 0;
        _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        _dutSerial = "正在核对";
        _scanAlertRequired = false;
        _scanStatus = "正在核对 ECU 编号与检测工程。";
        Notify();
        try
        {
            // Engineering verification uses the opened ECU project and
            // a REAL COM observation. Publishing barcode rules is not a
            // prerequisite. No physical action occurs without preflight
            // and the existing explicit hardware confirmation.
            if (!_engineeringVerificationArmed)
            {
                Reject("已收到扫码，请先打开检测工程。");
                return;
            }
            var stationDraft =
                await StationDeviceConfigurationStore.LoadAsync();
            stationDraft.Fixture.Validate();
            _ = StationFixtureExecutionPlan.From(stationDraft.Fixture);
            var fixtureSha = FingerprintEngineeringFixture(stationDraft.Fixture);
            var engineeringDecision = await ProductionPreparationService.PrepareAsync(
                plan.Project, "", raw, requireProductionDeployment: false);
            if (!engineeringDecision.ConfigurationReady)
            {
                Reject("本次工程实测配置未就绪：" + engineeringDecision.Explanation);
                return;
            }
            if (revision != _scanRevision || !_engineeringVerificationArmed ||
                _isProductionWorkspace() || !_scanner.IsConnected ||
                !string.Equals(scannerPort, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase) ||
                !ReferenceEquals(_plans.Current, plan))
                return;
            if (_hardware.CurrentStationEpoch != initialStationEpoch ||
                _hardware.RequestedEcuCanBitrateKbps != initialCanTarget)
            {
                Reject("扫码期间主控 USB/CAN 配置变化；请重新扫码。");
                return;
            }
            const string engineeringRuleJson = "ENGINEERING_RAW_SERIAL_V1";
            var engineeringRuleSha = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(engineeringRuleJson)));
            var engineeringRepeated = string.Equals(_lastIdentity, raw,
                StringComparison.Ordinal);
            _lastIdentity = raw;
            _dutSerial = raw;
            _scanMappedFields = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["DutSerial"] = raw,
                ["ProjectId"] = plan.Project.Definition.Id.Value.ToString("D"),
                ["ProjectBasis"] = "OPENED_ENGINEERING_PROJECT"
            };
            _acceptedStationEpoch = initialStationEpoch;
            _prepared = engineeringDecision.Preparation! with
            {
                AcceptedScannerObservation = new FrozenScanIdentity(
                    Guid.NewGuid(), scannedAt, scannerPort, raw,
                    engineeringRuleJson, engineeringRuleSha, "", raw, "",
                    _scanMappedFields.ToDictionary(x => x.Key, x => x.Value,
                        StringComparer.Ordinal))
                {
                    BarcodeRuleId = "ENGINEERING_RAW_SERIAL",
                    BarcodeRuleRevision = 1,
                    ProjectDecisionBasis = "OPENED_ENGINEERING_PROJECT",
                    StationSlotId = stationDraft.Fixture.StationSlotId,
                    EngineeringFixtureSha256 = fixtureSha
                }
            };
            _scanAlertRequired = false;
            _scanStatus = engineeringRepeated
                ? "再次收到相同编号；不会计为新增 ECU。"
                : "已收到 ECU 编号，可继续检查并开始工程测试。";
            _status = StationReadiness;
            Notify();
            if (_quickEngineeringStartOnScan && CanStartOperatorVerification)
            {
                _quickEngineeringStartOnScan = false;
                StartRequestStatus =
                    "扫码与工程资源通过；等待工程师确认夹具与烧录。";
                StartOperatorVerificationCommand.Execute();
            }
            else if (_quickEngineeringStartOnScan)
            {
                StartRequestStatus = _scanStatus = !_hardware.IsConnected
                    ? "编号已接收；请连接主控 USB 后重新扫码。"
                    : !_hardware.IsEcuCanBitrateApplied
                        ? "编号已接收；请确认 ECU CAN 码率后重新扫码。"
                        : "编号已接收，但工程或硬件未准备好，请检查当前状态。";
                Notify();
            }
            return;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException or
            FormatException or AutoTest.Projects.Project.ProjectDataException or
            System.Text.Json.JsonException)
        {
            Reject("无法完成本次扫码资源核对：" + error.Message);
        }

        void Reject(string reason)
        {
            if (revision != _scanRevision) return;
            _prepared = null;
            _acceptedStationEpoch = 0;
            _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
            _dutSerial = "等待扫码";
            _scanStatus = reason;
            _scanAlertRequired = true;
            Notify();
        }
    }
    private static string FingerprintEngineeringFixture(
        ProductionFixtureConfiguration fixture) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(fixture)));

}
