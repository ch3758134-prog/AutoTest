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
    private string _dutSerial = "等待扫码";
    private string _scanStatus = "等待已绑定工位扫码枪输入唯一编号";
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
        if (e.PropertyName != nameof(SerialScannerWorkbenchViewModel.IsConnected) ||
            _scanner.IsConnected) return;
        OnUi(() => InvalidateScan("扫码枪断开，本次扫码身份失效；恢复后请重新扫码。"));
    }

    private void OnCompleteBarcodeReceived(string raw) =>
        OnUi(() => { _ = InspectRealBarcodeAsync(raw); });

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
        _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        _dutSerial = "等待扫码";
        _lastIdentity = null;
        _scanStatus = reason;
        Notify();
    }

    private async Task InspectRealBarcodeAsync(string raw)
    {
        var revision = Interlocked.Increment(ref _scanRevision);
        var plan = _plans.Current;
        if (plan is null || !_scanner.IsConnected || string.IsNullOrWhiteSpace(raw))
            return;
        try
        {
            var policy = await StationScanIdentityPolicyStore.LoadAsync();
            if (policy is null)
            {
                Reject("未保存工位条码识别规则，不能从字符串猜 ECU 型号。");
                return;
            }
            var parsed = ProductionBarcodePreflight.Inspect(
                raw, plan.Project.Definition, policy);
            if (!parsed.IdentityMatched ||
                string.IsNullOrWhiteSpace(parsed.ModelCode) ||
                string.IsNullOrWhiteSpace(parsed.DutSerial))
            {
                Reject("本次扫码身份未被接受：" + parsed.Explanation);
                return;
            }
            // The deployed ECU project is the only model-to-test mapping.
            // A second station catalogue cannot override its TEST/FLASH scope.
            var deployment = await StationDeploymentStore.RestoreAsync();
            if (deployment is null ||
                !string.Equals(deployment.Pin.ManifestSha256,
                    plan.DeploymentFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Reject("工位部署已变化，不能复用旧工程或旧扫码；需核对部署。");
                return;
            }
            var decision = await ProductionPreparationService.PrepareAsync(
                plan.Project, parsed.ModelCode, parsed.DutSerial);
            if (!decision.ConfigurationReady)
            {
                Reject("本次正式资源无法冻结：" + decision.Explanation);
                return;
            }
            if (revision != _scanRevision || !_scanner.IsConnected ||
                !ReferenceEquals(_plans.Current, plan)) return;
            var identity = parsed.ModelCode + "\u001f" + parsed.DutSerial;
            var repeated = string.Equals(_lastIdentity, identity,
                StringComparison.Ordinal);
            _lastIdentity = identity;
            _dutSerial = parsed.DutSerial;
            _scanMappedFields = parsed.MappedFields is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed.MappedFields,
                    StringComparer.Ordinal);
            _prepared = decision.Preparation;
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
            _scanMappedFields = new Dictionary<string, string>(StringComparer.Ordinal);
            _dutSerial = "等待扫码";
            _scanStatus = reason;
            Notify();
        }
    }
}
