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
    }

    private void OnCompleteBarcodeReceived(string raw) =>
        OnUi(() => { if (!IsOperatorRunning && (_isProductionWorkspace() || _engineeringVerificationArmed))
            _ = InspectRealBarcodeAsync(raw); });

    private static void OnUi(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

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
        _scanStatus = "正在匹配当前工程的扫码期望与资源。";
        Notify();
        try
        {
            var expectation = plan.Project.Definition.BarcodeExpectation;
            if (expectation is null)
            {
                Reject("当前工程缺少扫码期望；请先在检测准备编辑并保存。");
                return;
            }
            var ruleJson = System.Text.Json.JsonSerializer.Serialize(expectation);
            var ruleSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(ruleJson)));
            var parsed = ProductionBarcodePreflight.InspectExpected(
                raw, plan.Project.Definition);
            if (!parsed.IdentityMatched ||
                string.IsNullOrWhiteSpace(parsed.ModelCode) ||
                string.IsNullOrWhiteSpace(parsed.DutSerial))
            {
                Reject("本次扫码身份未被接受：" + parsed.Explanation);
                return;
            }
            var decision = await ProductionPreparationService.PrepareAsync(
                plan.Project, parsed.ModelCode, parsed.DutSerial,
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
            var identity = parsed.DutSerial;
            var repeated = string.Equals(_lastIdentity, identity,
                StringComparison.Ordinal);
            _lastIdentity = identity;
            _dutSerial = parsed.DutSerial;
            _scanMappedFields = parsed.MappedFields is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : parsed.MappedFields.ToDictionary(x => x.Key, x => x.Value,
                    StringComparer.Ordinal);
            // Freeze the project-owned expectation's serialized content and its SHA.
            // This is not yet a formal RECORD fact; no RunId exists until
            // the physical START and production record adapter are ready.
            _acceptedStationEpoch = initialStationEpoch;
            _prepared = decision.Preparation! with
            {
                AcceptedScannerObservation = new FrozenScanIdentity(
                    Guid.NewGuid(), scannedAt, scannerPort, raw,
                    ruleJson, ruleSha,
                    parsed.ModelCode, parsed.DutSerial, parsed.HardwareCode,
                    _scanMappedFields.ToDictionary(x => x.Key, x => x.Value,
                        StringComparer.Ordinal))
            };
            _scanStatus = repeated
                ? "同一 ECU 编号再次扫码：仅更新真实观察，不计新增 DUT，也不触发复检。"
                : "已接受真实扫码并冻结本次 TEST/固件/算法资源；" +
                  "尚未确认夹具、START 或生产执行授权。";
            _status = "身份与工程资源已核对 · 工位执行待验";
            Notify();
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
