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
            _scanStatus = "已收到扫码原文；工位正在执行，不能替换本次 ECU 身份。";
            Notify();
            return;
        }
        if (!_isProductionWorkspace() && !_engineeringVerificationArmed)
        {
            _scanStatus = "已收到扫码原文；打开工程实测后可核验并运行。";
            Notify();
            return;
        }
        if (_plans.Current is null)
        {
            _scanStatus = "已收到扫码原文；当前没有可运行 ECU 工程，不能开始测试。";
            Notify();
            return;
        }
        _scanStatus = "已收到扫码原文，正在核对 ECU 编号与工程资源。";
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

    private void InvalidateScan(string reason)
    {
        Interlocked.Increment(ref _scanRevision);
        _prepared = null;
        _acceptedStationEpoch = 0;
        _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        _dutSerial = "等待扫码";
        _lastIdentity = null;
        _scanStatus = reason;
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
        if (plan is null || !_scanner.IsConnected || string.IsNullOrWhiteSpace(raw))
            return;
        _prepared = null;
        _acceptedStationEpoch = 0;
        _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        _dutSerial = "正在核对";
        _scanStatus = "正在使用已发布编码形制和唯一工程清单核对身份。";
        Notify();
        try
        {
            // A production scan requires a real, current USB station epoch.
            // An offline parser observation cannot authorize a later session.
            if (_isProductionWorkspace() &&
                (!_hardware.IsConnected || initialStationEpoch <= 0))
            {
                Reject("主控 USB 尚未连接；本次扫码不能绑定真实工位会话，请连接主控后重新扫描。");
                return;
            }
            // One published station identity model. Legacy scanner recipes and
            // project barcode expectations cannot select physical TEST resources.
            string scannerPnp = "";
            if (_isProductionWorkspace())
            {
                // Real production identity is accepted only from the station\'s
                // explicitly bound USB scanner, not any manually selected COM.
                // Registry enumeration stays off the WPF UI thread.
                var boundScanner = await Task.Run(() =>
                {
                    var saved = StationScannerDeviceBinding.Load();
                    return saved is null ? null :
                        StationScannerDeviceBinding.Match(saved);
                });
                if (boundScanner is null ||
                    !string.Equals(boundScanner.PortName, scannerPort,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Reject("当前扫码枪并非唯一绑定的工位 USB 设备；请先在生产准备中验证 COM 并绑定扫码枪。");
                    return;
                }
                scannerPnp = boundScanner.PnpInstanceId;
            }
            var resolved = await ProductionIdentityResolver.ResolvePublishedSnapshotAsync(raw);
            var release = resolved.Publication;
            var identity = resolved.Decision;
            if (!identity.Accepted || release is null)
            {
                Reject("本次扫码不符合发布的生产编码配置：" + identity.Detail);
                return;
            }
            if (identity.Project!.ProjectId != plan.Project.Definition.Id.Value ||
                !string.Equals(identity.Project.ManifestPath, plan.Project.ManifestPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(identity.Project.ManifestSha256, plan.Project.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                Reject("此编号匹配另一份已发布 ECU 工程；须在工位空闲时切换，不能使用当前 TEST/FLASH。");
                return;
            }
            var rule = release.Configuration.Rules.Single(x => x.RuleId == identity.RuleId);
            var ruleJson = System.Text.Json.JsonSerializer.Serialize(rule);
            var ruleSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(ruleJson)));
            var decision = await ProductionPreparationService.PrepareAsync(
                plan.Project, identity.Model, identity.Serial,
                requireProductionDeployment: _isProductionWorkspace());
            if (!decision.ConfigurationReady)
            {
                Reject("本次正式资源无法冻结：" + decision.Explanation);
                return;
            }
            if (revision != _scanRevision || !_scanner.IsConnected ||
                !string.Equals(scannerPort, _scanner.ConnectedPort,
                    StringComparison.OrdinalIgnoreCase) ||
                (!_isProductionWorkspace() && !_engineeringVerificationArmed) ||
                !ReferenceEquals(_plans.Current, plan)) return;
            if (_hardware.CurrentStationEpoch != initialStationEpoch ||
                _hardware.RequestedEcuCanBitrateKbps != initialCanTarget)
            {
                Reject("扫码预检期间主控会话或 ECU CAN 目标码率改变；请重新扫码。");
                return;
            }
            var repeated = string.Equals(_lastIdentity, identity.Serial,
                StringComparison.Ordinal);
            _lastIdentity = identity.Serial;
            _dutSerial = identity.Serial;
            _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DutSerial"] = identity.Serial,
                ["ModelCode"] = identity.Model,
                ["HardwareCode"] = identity.Hardware,
                ["BarcodeRule"] = identity.RuleId,
                ["ModelBasis"] = identity.ModelBasis
            };
            // Freeze the published station rule and its exact SHA.
            // This is not yet a formal RECORD fact; no RunId exists until
            // the physical START and production record adapter are ready.
            _acceptedStationEpoch = initialStationEpoch;
            _prepared = decision.Preparation! with
            {
                AcceptedScannerObservation = new FrozenScanIdentity(
                    Guid.NewGuid(), scannedAt, scannerPort, raw,
                    ruleJson, ruleSha,
                    identity.Model, identity.Serial, identity.Hardware,
                    _scanMappedFields.ToDictionary(x => x.Key, x => x.Value,
                        StringComparer.Ordinal))
                {
                    PublishedConfigurationSha256 = release.ConfigurationSha256,
                    PublicationId = release.PublicationId,
                    BarcodeRuleId = identity.RuleId,
                    BarcodeRuleRevision = identity.RuleRevision,
                    ModelDecisionBasis = identity.ModelBasis,
                    StationSlotId = release.Configuration.Fixture.StationSlotId,
                    ScannerPnpInstanceId = scannerPnp
                }
            };
            _scanStatus = repeated
                ? "同一 ECU 编号再次扫码：仅更新真实观察，不计新增 DUT，也不触发复检。"
                : "已接受真实扫码并冻结本次 TEST/固件/算法资源；" +
                  "尚未确认夹具、START 或生产执行授权。";
            _status = "身份与工程资源已核对 · 工位执行待验";
            Notify();

            // The operator explicitly chose the top-level engineering quick
            // run BEFORE scanning. A verified COM scan may now open the SAME
            // physical FLASH/fixture consent dialog that the Start button uses.
            // The scan itself is never permission to write to hardware.
            if (_quickEngineeringStartOnScan && _engineeringVerificationArmed &&
                !_isProductionWorkspace())
            {
                if (CanStartOperatorVerification)
                {
                    _quickEngineeringStartOnScan = false; // one-shot; no popup storm on repeat scans
                    StartRequestStatus = "已核对 ECU 编号与资源；等待工程师确认真实夹具与烧录操作。";
                    StartOperatorVerificationCommand.Execute();
                }
                else
                {
                    // Tell the engineer the actionable blocker at the point of
                    // scanning. Never pretend barcode identification means the
                    // physical ECU bus is ready for a destructive FLASH.
                    _scanStatus = !_hardware.IsConnected
                        ? "编号已识别；请先连接主控 USB，再重新扫码。"
                        : !_hardware.IsEcuCanBitrateApplied
                            ? "编号已识别；请先确认并回读 ECU CAN 波特率，再重新扫码。"
                            : !_plans.Current!.ResourceReady
                                ? "编号已识别，但 TEST/FLASH 工程资源未通过核验，请检查工程。"
                                : "编号已识别，但工位执行状态尚未就绪，请查看主控和工程状态。";
                    StartRequestStatus = _scanStatus;
                    Notify();
                }
            }
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
            Notify();
        }
    }
}
