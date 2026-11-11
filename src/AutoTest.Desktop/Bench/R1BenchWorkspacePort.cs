using System.IO;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Devices.Diagnostics;
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
    private readonly Func<bool,long,bool>? _approveIgn;
    private readonly Func<byte,long,bool>? _approveSolenoid;
    private readonly Func<bool>? _testRunning;
    private readonly SemaphoreSlim _saveGate = new(1,1);
    private readonly string _auditPath;
    private readonly ProjectBenchActionsStore? _actionStore;
    private readonly Func<BenchKeyAction, long, bool>? _approveKeyAction;
    private readonly Func<string?>? _projectPath;
    private readonly Func<string?>? _projectId;
    public R1BenchWorkspacePort(Func<ControllerConfigToolViewModel?> configuration,
        Func<HardwareBenchController?> hardware, string? auditPath = null,
        Func<uint,long,bool>? approveKeySim = null,
        Func<bool,long,bool>? approveIgn = null,
        Func<bool>? testRunning = null,
        Func<byte,long,bool>? approveSolenoid = null,
        Func<string?>? projectPath = null, Func<string?>? projectId = null,
        Func<BenchKeyAction, long, bool>? approveKeyAction = null)
    {
        _projectPath = projectPath;
        _projectId = projectId;
        _approveKeyAction = approveKeyAction;
        if (projectPath is not null && projectId is not null)
            _actionStore = new ProjectBenchActionsStore(projectPath, projectId);
        _approveIgn = approveIgn;
        _testRunning = testRunning;
        _approveSolenoid = approveSolenoid;
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
        if (_testRunning?.Invoke() == true)
            return Reject("TEST_EXECUTION_ACTIVE",
                "自动 TEST 正在执行；请先停止 TEST，再执行手动台架动作。", source);
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
        if (physical.PhysicalCanToolFlashActive)
            return Reject("FLASH_ACTIVE", "CAN1 烧录过程中禁止手动按键控制。", source);
        if (physical.PhysicalCanToolFlashActive)
            return Reject("FLASH_ACTIVE", "ECU 正在烧录，禁止手动 KEY_SIM 输出。", source);
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
    /// Direct manual-bench switch selection: uses exactly the same verified
    /// CAN0 KEY_SIM path as configured Controller States, without requiring a
    /// named ECU Profile to inspect the actual board's twenty switch bits.
    /// Only USB CAN_SEND ACK is observable; five analog outputs are not read.
    /// </summary>
    public async Task<BenchOperationResult> ApplyPhysicalKeyMaskAsync(
        uint mask, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = BenchEvidenceSource.Physical;
        if (_testRunning?.Invoke() == true)
            return Reject("TEST_EXECUTION_ACTIVE", "自动 TEST 正在运行，禁止手动 CAN0 输出。", source);
        if (mask == 0 || mask > KeySimCanCodec.MaxMask)
            return Reject("KEYSIM_DIRECT_MASK_INVALID",
                "直接选中的按键组合必须在 1～20 位范围且非空；全部释放使用独立的释放命令。",
                source);
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("KEYSIM_DEVICE_NOT_CONNECTED",
                "需在设备连接窗口建立真实主控并完成固件版本验证。", source);
        var generation = physical.Generation;
        if (_approveKeySim?.Invoke(mask, generation) != true)
            return Reject("KEYSIM_OPERATOR_REJECTED",
                "本次 CAN0 组合开关操作未经操作员确认，未发送。", source);
        if (!ReferenceEquals(_hardware(), physical) ||
            physical.State != UsbSessionState.Connected ||
            physical.Generation != generation)
            return Reject("KEYSIM_SESSION_CHANGED",
                "确认期间 WinUSB 会话变化；没有发送过期掩码。", source);
        var result = await physical.SetPhysicalKeySimMaskAsync(mask,
            HardwareBenchController.PhysicalKeySimConfirmation(mask, generation), ct);
        var accepted = result.Outcome == Outcome.Accepted &&
            result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
        return new BenchOperationResult(accepted,
            result.Value?.OperationId.ToString("D"),
            result.OperationId.ToString("D"),
            generation,
            $"CAN0 ID=0x730 DLC=8 / KEY20 MASK=0x{mask:X5}",
            accepted ? "MAIN_CONTROLLER_USB_CAN_SEND_ACK" :
                result.Outcome == Outcome.Uncertain ? "UNKNOWN" : "NOT_SENT",
            result.Value?.Evidence.ToString(),
            result.FailureCode,
            result.Failure?.Message,
            source,
            accepted
                ? "主控已 ACK；并未测量 KEY_SIM 五路输出电压或 ECU 识别状态"
                : result.Outcome == Outcome.Uncertain
                    ? "实际 CAN0 发送状态不确定；不得将选中位当成已确认状态"
                    : "未确认主控接受 CAN_SEND");
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
        if (_testRunning?.Invoke() == true)
            return Reject("TEST_EXECUTION_ACTIVE", "自动 TEST 正在运行，禁止手动释放按键。", source);
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("KEYSIM_RELEASE_DEVICE_NOT_CONNECTED",
                "当前没有经过版本验证的主控，无法确认释放帧是否发送。", source);
        if (physical.PhysicalCanToolFlashActive)
            return Reject("FLASH_ACTIVE", "ECU 烧录运行中，禁止手动 KEY_SIM 释放。", source);
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

    /// <summary>Real SET_IGN 0x12 physical output through the same verified USB session.</summary>
    public async Task<BenchOperationResult> SetIgnAsync(bool on, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = BenchEvidenceSource.Physical;
        if (_testRunning?.Invoke() == true)
            return Reject("TEST_EXECUTION_ACTIVE",
                "自动 TEST 正在运行，禁止独立切换 IGN。", source);
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("IGN_DEVICE_NOT_READY",
                "需要已连接、已核对版本的真实主控板。", source);
        if (physical.PhysicalCanToolFlashActive)
            return Reject("IGN_FLASH_ACTIVE",
                "CAN1 ECU 烧录时不可手动切换 IGN。", source);
        var generation = physical.Generation;
        if (_approveIgn?.Invoke(on, generation) != true)
            return Reject("IGN_OPERATOR_NOT_AUTHORIZED",
                "用户取消了本次真实 IGN 操作，未发送。", source);
        if (!ReferenceEquals(_hardware(), physical) ||
            physical.State != UsbSessionState.Connected ||
            physical.Generation != generation ||
            _testRunning?.Invoke() == true || physical.PhysicalCanToolFlashActive)
            return Reject("IGN_SESSION_CHANGED",
                "确认过程中执行状态或 USB Generation 已变化，拒绝发送。", source);
        var result = await physical.SetPhysicalIgnAsync(on,
            HardwareBenchController.PhysicalIgnConfirmation(on, generation), ct);
        var accepted = result.Outcome == Outcome.Accepted &&
            result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
        return new BenchOperationResult(accepted,
            result.Value?.OperationId.ToString("D"),
            result.OperationId.ToString("D"), generation,
            "USB CMD 0x12 / SET_IGN=" + (on ? "01 ON" : "00 OFF"),
            accepted ? "MAIN_CONTROLLER_SET_IGN_ACK" :
                result.Outcome == Outcome.Uncertain ? "UNKNOWN" : "NOT_SENT",
            result.Value?.Evidence.ToString(),
            result.FailureCode, result.Failure?.Message,
            source,
            accepted
                ? "主控确认已接受 IGN 命令；未实测 IGN 电压，实际输出状态不可由 ACK 证明"
                : result.Outcome == Outcome.Uncertain
                    ? "IGN 物理输出未知；按现场电压表确认"
                    : "SET_IGN 未获得有效 ACK");
    }

    public async Task<BenchOperationResult> SetSolenoidAsync(byte action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = BenchEvidenceSource.Physical;
        if (action > 1)
            return Reject("SOL_ACTION_INVALID", "电磁阀命令只允许 0=升起、1=下落。", source);
        if (_testRunning?.Invoke() == true)
            return Reject("TEST_EXECUTION_ACTIVE", "自动 TEST 正在执行，禁止手动电磁阀动作。", source);
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("SOL_DEVICE_NOT_READY", "需要已验证版本且已连接的真实主控板。", source);
        if (physical.PhysicalCanToolFlashActive)
            return Reject("SOL_FLASH_ACTIVE", "ECU 烧录进行中，禁止电磁阀动作。", source);
        var generation = physical.Generation;
        if (_approveSolenoid?.Invoke(action, generation) != true)
            return Reject("SOL_OPERATOR_REJECTED", "操作员未授权电磁阀动作，未发送。", source);
        if (!ReferenceEquals(_hardware(), physical) ||
            physical.Generation != generation ||
            physical.State != UsbSessionState.Connected ||
            physical.PhysicalCanToolFlashActive ||
            _testRunning?.Invoke() == true)
            return Reject("SOL_SESSION_CHANGED", "确认期间硬件/执行状态变化，未发送。", source);
        var result = await physical.SetPhysicalSolenoidAsync(action,
            HardwareBenchController.PhysicalSolenoidConfirmation(action, generation), ct);
        var accepted = result.Outcome == Outcome.Accepted &&
            result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
        return new BenchOperationResult(accepted,
            result.Value?.OperationId.ToString("D"),
            result.OperationId.ToString("D"), generation,
            $"USB SET_SOL 0x16 PAYLOAD {action:X2} / " + (action == 0 ? "升起" : "下落"),
            accepted ? "MAIN_CONTROLLER_SET_SOL_ACK" :
                result.Outcome == Outcome.Uncertain ? "UNKNOWN" : "NOT_SENT",
            result.Value?.Evidence.ToString(), result.FailureCode,
            result.Failure?.Message, source,
            accepted
                ? "主控 ACK 已接受 SET_SOL；并未测得气缸行程/夹紧到位"
                : result.Outcome == Outcome.Uncertain
                    ? "SET_SOL 结果不确定，实际夹具状态 UNKNOWN"
                    : "未收到可靠 SET_SOL ACK");
    }

    /// <summary>GET_SOL 0x17 reports firmware action/GPIO state, not mechanical contact.</summary>
    public async Task<BenchOperationResult> ReadSolenoidAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = BenchEvidenceSource.Physical;
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return Reject("SOL_READ_NOT_READY", "未连接已验证的主控板，无法读取固件 GPIO 状态。", source);
        var generation = physical.Generation;
        try
        {
            var response = await physical.ReadOnlyCommandAsync(
                FirmwareReadOnlyProtocol.GetSolenoid, TimeSpan.FromSeconds(2), ct);
            if (physical.State != UsbSessionState.Connected || physical.Generation != generation ||
                response.Command != FirmwareReadOnlyProtocol.GetSolenoid ||
                response.Payload.Length != 1 || response.Payload[0] > 1)
                return Reject("SOL_READ_INVALID",
                    "GET_SOL 返回与当前主控会话不一致，丢弃陈旧结果。", source);
            return new BenchOperationResult(true, null, null, generation,
                "USB GET_SOL 0x17", "GPIO_ACTION_" + response.Payload[0],
                physical.TraceFile, null, null, source,
                $"固件报告动作值 {response.Payload[0]}（0=升起/1=下落）；不是机械到位回读");
        }
        catch (Exception ex)
        {
            return Reject("SOL_READ_FAILED", ex.Message, source);
        }
    }

    private static BenchOperationResult Reject(string code,string detail,BenchEvidenceSource source)
        => new(false,null,null,null,null,null,null,code,detail,source,null);

    public Task<IReadOnlyList<BenchKeyAction>> LoadKeyActionsAsync(CancellationToken ct) =>
        _actionStore is null
            ? Task.FromException<IReadOnlyList<BenchKeyAction>>(
                new InvalidOperationException("手动台架未绑定当前工程。"))
            : _actionStore.LoadAsync(ct);

    public Task SaveKeyActionsAsync(IReadOnlyList<BenchKeyAction> actions, CancellationToken ct) =>
        _actionStore is null
            ? Task.FromException(new InvalidOperationException("手动台架未绑定当前工程。"))
            : _actionStore.SaveAsync(actions, ct);

    /// <summary>
    /// One operator authorization for a frozen action plan. Every SetKeys step
    /// is a real KEY_SIM CAN0 operation, not a fake voltages/simulated ECU.
    /// Cancellation does NOT send an invented "neutral" mask.
    /// </summary>
    public async Task<BenchActionExecution> RunKeyActionAsync(BenchKeyAction action,
        IProgress<BenchActionProgress>? progress, CancellationToken ct)
    {
        BenchActionValidation.Validate(action);
        if (_actionStore is null)
            return new(false, false, false, "未绑定工程动作配置，拒绝真实输出。");
        // Never execute an unsaved action or one from a different open project.
        // This also blocks stale action selections after the user switches projects.
        var currentActions = await _actionStore.LoadAsync(ct);
        var saved = currentActions.FirstOrDefault(a => a.Id == action.Id);
        if (saved is null || saved.Name != action.Name ||
            saved.Steps.Count != action.Steps.Count ||
            !saved.Steps.SequenceEqual(action.Steps))
            return new(false, false, false, "当前动作与工程中已保存的配置不一致；请重新加载工程动作。");
        if (_projectPath is null || _projectId is null ||
            string.IsNullOrWhiteSpace(_projectPath()) || string.IsNullOrWhiteSpace(_projectId()))
            return new(false, false, false, "尚未绑定已保存的工程，拒绝真实输出。");
        if (_testRunning?.Invoke() == true)
            return new(false, false, false, "自动 TEST 正在运行，禁止手动按键动作。");
        var physical = _hardware();
        if (physical is not { State: UsbSessionState.Connected,
            ProtocolVersionVerified: true, Generation: > 0 })
            return new(false, false, false, "真实主控尚未连接并完成协议版本验证。");
        if (physical.PhysicalCanToolFlashActive)
            return new(false, false, false, "真实 CAN1 烧录进行中，禁止 KEY_SIM 操作。");
        var generation = physical.Generation;
        var identity = _projectId();
        var projectPath = _projectPath();
        var frozen = new BenchKeyAction(action.Id, action.Name, action.Steps.ToArray());
        if (_approveKeyAction?.Invoke(frozen, generation) != true)
            return new(false, false, false, "用户取消真实按键连续动作；未发送。");

        string? lastMask = null;
        try
        {
            for (var i = 0; i < frozen.Steps.Count; ++i)
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_hardware(), physical) ||
                    physical.Generation != generation ||
                    physical.State != UsbSessionState.Connected ||
                    !physical.ProtocolVersionVerified ||
                    physical.PhysicalCanToolFlashActive ||
                    _testRunning?.Invoke() == true ||
                    _projectId() != identity || _projectPath() != projectPath)
                    return new(false, false, false,
                        "动作执行期间工程、主控会话或设备占用发生变化，停止后续步骤；最后开关状态 UNKNOWN。");
                var step = frozen.Steps[i];
                if (step.Kind == BenchActionStepKind.SetKeys)
                {
                    var result = await physical.SetPhysicalKeySimMaskAsync(step.Mask,
                        HardwareBenchController.PhysicalKeySimConfirmation(step.Mask, generation), ct);
                    var accepted = result.Outcome == Outcome.Accepted &&
                        result.Value is { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged };
                    if (!accepted)
                        return new(false, false, false,
                            $"步骤 {i + 1} 主控 ACK 未确认：{result.FailureCode ?? "UNKNOWN"}；" +
                            "不推断最终 KEY_SIM 开关或 ECU 识别状态。");
                    lastMask = $"0x{step.Mask:X5}";
                    progress?.Report(new BenchActionProgress(i + 1, frozen.Steps.Count,
                        $"已请求 Key 状态 {lastMask}，主控 CAN_SEND ACK；五路实际电压未测量"));
                }
                else
                {
                    progress?.Report(new BenchActionProgress(i + 1, frozen.Steps.Count,
                        $"保持当前 Key 状态 {step.DurationMs} ms"));
                    await Task.Delay(step.DurationMs, ct);
                }
            }
            return new(true, false, false,
                $"动作执行完成；最后已确认发送掩码 {lastMask ?? "UNKNOWN"}。" +
                "未自动恢复任何状态，五路电压及 ECU 识别均未测量。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(false, true, false,
                "用户停止动作；不会自动释放或恢复。最后已发送的开关状态可能保持，实际电压 UNKNOWN。");
        }
    }

    public async Task SaveManualMeasurementAsync(BenchManualMeasurement measurement,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ct.ThrowIfCancellationRequested();
        if (measurement.ProjectRevision == "RAW_KEYSIM")
        {
            // No Controller Profile is required for direct KEY_SIM operation.
            // Only an acknowledged physical command may be linked to manually
            // measured CH1..CH5. Never synthesize voltages from switch masks.
            if (measurement.Source != BenchEvidenceSource.Physical ||
                measurement.ControllerId != "KEYSIM_CAN0" ||
                !measurement.StateId.StartsWith("MASK_0x", StringComparison.Ordinal) ||
                measurement.StateId.Length != 12 ||
                !uint.TryParse(measurement.StateId.AsSpan(7),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var mask) ||
                mask == 0 || mask > KeySimCanCodec.MaxMask ||
                string.IsNullOrWhiteSpace(measurement.OperationId))
                throw new InvalidOperationException(
                    "直接掩码人工测量必须引用确实获得主控 ACK 的 KEY_SIM 操作。");
        }
        else if (_configuration()?.CurrentProfile?.Revision.ToString()!=measurement.ProjectRevision)
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
