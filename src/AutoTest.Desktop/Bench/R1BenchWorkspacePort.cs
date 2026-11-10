using System.IO;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
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
    private readonly Func<uint,long,bool>? _approveKeySim;
    private readonly SemaphoreSlim _saveGate = new(1,1);
    private readonly string _auditPath;
    public R1BenchWorkspacePort(Func<ControllerConfigToolViewModel?> configuration,
        Func<HardwareBenchController?> hardware, string? auditPath = null,
        Func<uint,long,bool>? approveKeySim = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _approveKeySim = approveKeySim;
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
    public async Task<BenchOperationResult> ApplyStateAsync(
        string revision, string controllerId, string stateId,
        BenchEvidenceSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_configuration()?.CurrentProfile is not { } profile ||
            profile.Revision.ToString() != revision ||
            !Guid.TryParse(controllerId, out var controller) ||
            !Guid.TryParse(stateId, out var selected))
            return Reject("REFERENCE_STALE",
                "控制器 Profile 或 Revision 已变化，重新加载后才能控制。", source);

        uint mask;
        try
        {
            var state = profile.ResolveState(controller, selected, profile.Revision);
            mask = state.KeyMask;
            if (mask > KeySimCanCodec.MaxMask)
                return Reject("KEYSIM_MASK_INVALID", "按键掩码超过 20 位。", source);
        }
        catch (Exception e)
        { return Reject("REFERENCE_STALE", e.Message, source); }

        if (source == BenchEvidenceSource.Replay)
            return Reject("REPLAY_READ_ONLY", "历史回放不发送实际按键动作。", source);
        if (source != BenchEvidenceSource.Physical)
            return Reject("SOURCE_UNSUPPORTED", "正式 R1 不提供模拟按键。", source);

        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("KEYSIM_DEVICE_NOT_CONNECTED",
                "需要 P02 当前已验证的真实主控 USB 会话。", source);
        var generation = physical.Generation;
        if (_approveKeySim?.Invoke(mask, generation) != true)
            return Reject("KEYSIM_OPERATOR_REJECTED",
                "操作者未授权本次实际 CAN0 按键掩码操作；没有向硬件发送。", source);
        if (_configuration()?.CurrentProfile is not { } latest ||
            latest.Revision != profile.Revision ||
            !ReferenceEquals(latest, profile) ||
            physical.Generation != generation)
            return Reject("KEYSIM_CONFIGURATION_CHANGED",
                "确认期间 Profile/设备会话已变化，拒绝发出旧指令。", source);

        var result = await physical.SetPhysicalKeySimMaskAsync(mask,
            HardwareBenchController.PhysicalKeySimConfirmation(mask, generation), ct);
        var accepted = result.Outcome == Outcome.Accepted &&
            result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
        return new BenchOperationResult(accepted,
            result.Value?.OperationId.ToString("D"),
            result.OperationId.ToString("D"),
            generation,
            $"CAN0 / ID 0x730 / DLC8 / KEY20 MASK 0x{mask:X5}" +
                (mask == 0 ? " / 全部释放" : " / 保持电阻网络闭合"),
            accepted ? "MAIN_CONTROLLER_USB_CAN_SEND_ACK" :
                result.Outcome == Outcome.Uncertain ? "UNKNOWN" : "NOT_SENT",
            result.Value?.Evidence.ToString(),
            result.FailureCode,
            result.Failure?.Message,
            BenchEvidenceSource.Physical,
            accepted
                ? "主控 CAN_SEND 已回 ACK；KEY_SIM 开关电压及 ECU 识别状态未测量"
                : result.Outcome == Outcome.Uncertain
                    ? "CAN0 发送结果不确定；不要推断开关状态"
                    : "未收到主控有效 ACK");
    }
    /// <summary>
    /// Physical recovery: explicit 0x80 frame clears all KEY_SIM switch bits.
    /// It remains available without a selected P05 controller state, because
    /// a stale/missing project must not prevent an operator-requested release.
    /// </summary>
    public async Task<BenchOperationResult> ReleaseAllPhysicalKeysAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = BenchEvidenceSource.Physical;
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("KEYSIM_RELEASE_DEVICE_NOT_CONNECTED",
                "当前没有经过版本验证的主控，无法确认释放帧是否发送。", source);
        var generation = physical.Generation;
        if (_approveKeySim?.Invoke(0, generation) != true)
            return Reject("KEYSIM_RELEASE_OPERATOR_REJECTED",
                "操作员取消真实 CAN0 全部释放；未发送。", source);
        var result = await physical.SetPhysicalKeySimMaskAsync(0,
            HardwareBenchController.PhysicalKeySimConfirmation(0, generation), ct);
        var accepted = result.Outcome == Outcome.Accepted &&
            result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
        return new BenchOperationResult(accepted,
            result.Value?.OperationId.ToString("D"),
            result.OperationId.ToString("D"),
            generation,
            "CAN0 ID=0x730 DLC8 DATA=8000000000000000 / 全部释放",
            accepted ? "MAIN_CONTROLLER_USB_CAN_SEND_ACK" : "UNKNOWN_OR_NOT_SENT",
            result.Value?.Evidence.ToString(), result.FailureCode, result.Failure?.Message,
            source,
            accepted ? "主控确认 CAN 发送；按键板开关状态仍需按需观测" :
                "释放结果未确认，不能宣称所有开关已断开");
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
            measurement.Comment,
            PhysicalCommandAckRecorded=measurement.Source == BenchEvidenceSource.Physical &&
                !string.IsNullOrWhiteSpace(measurement.OperationId),
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
