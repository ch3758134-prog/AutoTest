using System.IO;
using System.Text.Json;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Projects.ControllerConfig;

namespace AutoTest.Desktop.Bench;

/// <summary>
/// R1 manual bench adapter to the same Controller Profile and Station hardware
/// session used by P05 / P02. The R1 product never opens a second USB link
/// or silently enables CAN0 keypad outputs from this UI.
/// </summary>
public sealed class R1BenchWorkspacePort : IBenchWorkbenchPort
{
    private readonly Func<ControllerConfigToolViewModel?> _configuration;
    private readonly Func<HardwareBenchController?> _hardware;
    private readonly SemaphoreSlim _saveGate = new(1,1);
    private readonly string _auditPath;
    public R1BenchWorkspacePort(Func<ControllerConfigToolViewModel?> configuration,
        Func<HardwareBenchController?> hardware, string? auditPath = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _auditPath = auditPath ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "AutoTest",
            "BenchMeasurements", "manual-measurements.jsonl");
    }
    public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EcuControlProfile? config = _configuration()?.CurrentProfile;
        if (config is null) return Task.FromResult<BenchConfiguration?>(null);
        config.Validate();
        var controllers = config.Controllers.OrderBy(x=>x.Order)
            .Select(c => new BenchControllerDefinition(c.Id.ToString("D"),c.Name,
                c.States.OrderBy(s=>s.Order).Select(s=>new BenchStateDefinition(
                    s.Id.ToString("D"),s.Name,s.KeyMask,
                    s.Interaction switch
                    {
                        ControlInteraction.Latched => BenchInteraction.Latching,
                        ControlInteraction.SpringReturn => BenchInteraction.ReturnToDefault,
                        _ => BenchInteraction.Momentary
                    },
                    s.Windows.Length>0
                        ? string.Join("; ",s.Windows.Select(w=>$"CH{w.Channel} target {w.TargetVolts}V"))
                        : "未配置目标模拟电压")).ToArray()))
            .ToArray();
        return Task.FromResult<BenchConfiguration?>(new BenchConfiguration(
            config.EcuName,config.EcuName,config.Revision.ToString(),controllers));
    }
    public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var controller = _hardware();
        bool connected=controller?.State==UsbSessionState.Connected;
        return Task.FromResult(new BenchConnectionSnapshot(connected
            ? BenchConnection.Connected : BenchConnection.Disconnected,
            controller?.SelectedInstanceId,
            connected ? controller!.Generation : null,
            null,null,connected
                ? "共享 P02 WinUSB 会话；硬件状态只由 DEVICE 更新。"
                : "请在 P02 主控设备页完成隔离确认并连接真实主控板；台架不单独打开 USB。"));
    }
    public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var controller=_hardware();
        IReadOnlyList<BenchDevice> result=controller is null
            ? Array.Empty<BenchDevice>()
            : controller.Enumerate().Select(c => new BenchDevice(
                c.DeviceInstanceId,c.DeviceInstanceId)).ToArray();
        return Task.FromResult(result);
    }
    public async Task<BenchConnectionSnapshot> ConnectAsync(string identity,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Connecting is only allowed through P02 DEVICE's verified isolation
        // and version handshake. Never duplicate a physical main-controller session.
        var snapshot=await GetConnectionAsync(ct);
        if (snapshot.State==BenchConnection.Connected &&
            string.Equals(snapshot.DeviceIdentity,identity,StringComparison.OrdinalIgnoreCase))
            return snapshot;
        return new BenchConnectionSnapshot(BenchConnection.Faulted,identity,null,
            "P02_CONNECTION_REQUIRED",null,
            "P06 不单独建立 WinUSB 会话；请在 P02 主控设备工作区连接后刷新本页。");
    }
    public async Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot=await GetConnectionAsync(ct);
        return snapshot with
        {
            Message="请在 P02 主控设备页断开共享 USB 会话；P06 不得强行切断正在使用中的 CAN/UDS 通道。"
        };
    }
    public Task<BenchOperationResult> ApplyStateAsync(string revision,string controllerId,
        string stateId,BenchEvidenceSource source,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_configuration()?.CurrentProfile is not { } profile ||
            profile.Revision.ToString()!=revision ||
            !Guid.TryParse(controllerId,out var c) ||
            !Guid.TryParse(stateId,out var s))
            return Task.FromResult(Reject("REFERENCE_STALE",
                "工程配置或状态已改变，必须重新选择并读取。",source));
        try { _=profile.ResolveState(c,s,profile.Revision); }
        catch(Exception ex) { return Task.FromResult(Reject("REFERENCE_STALE",ex.Message,source)); }
        if (source==BenchEvidenceSource.Physical)
            return Task.FromResult(Reject("BENCH_NOT_AUTHORIZED",
                "真实 KEY_SIM 20 Key 电阻组合网络 / 5 路模拟电压尚未接入可授权 CAN0 控制服务。"+
                "没有发出物理指令；不得当作已按键。",source));
        if (source==BenchEvidenceSource.Replay)
            return Task.FromResult(Reject("REPLAY_READ_ONLY",
                "回放不能执行状态修改；请选择实际录制文件读取。",source));
        if (source!=BenchEvidenceSource.Mock)
            return Task.FromResult(Reject("SOURCE_UNKNOWN","没有有效执行源。",source));
        var op=Guid.NewGuid().ToString("N");
        return Task.FromResult(new BenchOperationResult(true,op,op,null,
            $"MODELED_KEYMASK:{profile.ResolveState(c,s,profile.Revision).KeyMask:X5}",
            "MOCK_ONLY",null,null,null,BenchEvidenceSource.Mock,
            "Mock 层确认状态映射；未向硬件发送 CAN，也未测得任何模拟电压。"));
    }
    private static BenchOperationResult Reject(string code,string detail,BenchEvidenceSource source)
        => new(false,null,null,null,null,null,null,code,detail,source,null);

    public async Task SaveManualMeasurementAsync(BenchManualMeasurement measurement,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ct.ThrowIfCancellationRequested();
        if (_configuration()?.CurrentProfile?.Revision.ToString()!=measurement.ProjectRevision)
            throw new InvalidOperationException("当前 Controller Revision 已变更，拒绝保存过期测量。");
        if (measurement.Voltages.Count==0 || !measurement.Voltages.Any(v=>v.Volts.HasValue))
            throw new ArgumentException("必须填写实际万用表读数，空白不是零伏。");
        var record=new
        {
            Schema=1,Kind="R1_MANUAL_BENCH_MEASUREMENT",Utc=DateTimeOffset.UtcNow,
            measurement.ProjectRevision,measurement.ControllerId,measurement.StateId,
            measurement.OperationId,
            Source=measurement.Source.ToString(),measurement.Voltages,
            measurement.Comment,PhysicalCommandExecuted=false,
            IsMeasuredByHardware=false
        };
        var line=JsonSerializer.Serialize(record);
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_auditPath)!);
            await File.AppendAllTextAsync(_auditPath,line+Environment.NewLine,ct)
                .ConfigureAwait(false);
        }
        finally { _saveGate.Release(); }
    }
}
