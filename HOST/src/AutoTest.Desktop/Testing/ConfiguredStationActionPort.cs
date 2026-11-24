using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Projects;
using AutoTest.Devices.Station;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Real station operations on configured TEST targets. It is not a mock adapter:
/// all CAN/IGN writes route through the bounded, typed-consent WinUSB physical fence.
/// Device ACK is reported as an operation fact, never as a DUT test verdict.
/// </summary>
public sealed class ConfiguredStationActionPort : ITestActuationPort
{
    private readonly HardwareConnectionService _hardware;
    private readonly ProjectWorkspaceService _projects;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Dictionary<string, (EcuControlProfile Profile, StateReference State)>
        _buttons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TestTargetMapping Target, DbcMessage Message)> _can =
        new(StringComparer.OrdinalIgnoreCase);
    private ImmutableArray<StateReference> _active = ImmutableArray<StateReference>.Empty;
    private EcuControlProfile? _activeProfile;
    private HardwareKeyPolicy? _policy;
    private string? _fingerprint;
    private bool _unknown;
    private bool _hasRawKey;
    private uint _rawMask;
    private readonly Dictionary<(BusRole Role, bool Extended, uint Id),
        (CancellationTokenSource Stop, Task Worker)> _periodic = new();
    private readonly ConcurrentQueue<Exception> _periodicErrors = new();

    public ConfiguredStationActionPort(HardwareConnectionService hardware,
        ProjectWorkspaceService projects)
    {
        _hardware = hardware;
        _projects = projects;
    }

    /// <summary>Full asset hash and mapping checks before any authorization.</summary>
    public async Task PrepareAsync(IEnumerable<TestTargetMapping> mappings, CancellationToken ct)
    {
        var project = _projects.Current ?? throw new InvalidOperationException("工程未打开。");
        _buttons.Clear(); _can.Clear(); _active = []; _activeProfile = null;
        _unknown = false; _hasRawKey = false; _rawMask = 0;
        foreach (var target in mappings)
        {
            ct.ThrowIfCancellationRequested();
            target.Validate();
            if (target.Kind == TestTargetKind.Ignition) continue;
            if (target.Kind == TestTargetKind.RawCan) continue;
            if (target.Kind == TestTargetKind.RawKey)
            {
                _hasRawKey = true;
                continue;
            }
            var asset = project.Definition.Assets.SingleOrDefault(a => a.Id == target.Asset)
                ?? throw new ProjectDataException("MAPPING_ASSET_MISSING", target.Name);
            if (asset.Revision != target.AssetRevision ||
                !string.Equals(asset.Sha256, target.AssetSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("MAPPING_STALE", target.Name);
            var path = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!asset.VerifyHash(bytes))
                throw new ProjectDataException("MAPPING_CONTENT_STALE", target.Name);
            if (target.Kind == TestTargetKind.Button)
            {
                if (asset.Kind != ProjectWorkspaceService.ControllerKind)
                    throw new ProjectDataException("BUTTON_PROFILE_KIND", target.Name);
                var profile = ControllerRepository.Decode(bytes);
                if (_activeProfile is not null && _activeProfile.ProfileId != profile.ProfileId)
                    throw new ProjectDataException("MULTI_BOARD_UNSUPPORTED",
                        "One hardware KEY_SIM station cannot combine two different controller profiles.");
                var reference = new StateReference(profile.ProfileId, target.ControllerId,
                    target.StateId, profile.Revision);
                _ = reference.Resolve(profile);
                _activeProfile = profile;
                _policy = profile.KeyHardwarePolicy?.Validate() ??
                    throw new ProjectDataException("CTRL_HARDWARE_NOT_CONFIGURED",
                        "真实按键执行要求工程师先配置可用 Key 和禁止组合规则。");
                _buttons.Add(target.Name, (profile, reference));
            }
            else if (target.Kind == TestTargetKind.CanMessage)
            {
                if (target.Role != BusRole.DutCan)
                    throw new ProjectDataException("CAN_TX_ROLE",
                        "自动测试 DBC 报文必须绑定 ECU CAN，不能向 KEY CAN 发送：" + target.Name);
                if (asset.Kind != ProjectWorkspaceService.DbcKind)
                    throw new ProjectDataException("CAN_MAPPING_KIND", target.Name);
                var message = DbcSource.Load(bytes).Parse(asset.Name).FindMessage(target.CanId);
                if (message.IsExtended != target.Extended ||
                    message.ArbitrationId != target.CanId)
                    throw new ProjectDataException("CAN_MAPPING_FORMAT", target.Name);
                _can.Add(target.Name, (target, message));
            }
        }
        if (_hasRawKey && _buttons.Count > 0)
            throw new ProjectDataException("KEY_CONTROL_MODE_CONFLICT",
                "一个测试程序不能同时使用原始 20 Key 操作和已配置功能按键，以免覆盖保持状态。");
    }

    /// <summary>Full literal TEST operation preflight, before physical authorization.
    /// Dynamic $variables are verified at their execution boundary, never
    /// substituted by empty strings or silently encoded as zeros.</summary>
    public void ValidateRunSteps(TestDocument document)
    {
        var mappings = TestRunStepScope.RequiredMappings(document)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var node in TestRunStepScope.EnabledSteps(document))
        {
            if (node.Type != "Write") continue;
            try
            {
                var name = node.Properties.GetValueOrDefault("target", "");
                if (!mappings.TryGetValue(name, out var target))
                    throw new InvalidOperationException("步骤没有可用的真实操作目标。");
                string action = node.Properties.GetValueOrDefault("value", "");
                if (string.IsNullOrWhiteSpace(action))
                    throw new FormatException("硬件操作值不能为空。");

                var periodText = node.Properties.GetValueOrDefault("periodMs", "0");
                var durationText = node.Properties.GetValueOrDefault("durationMs", "0");
                if (!int.TryParse(periodText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var period) || period != 0 && period is < 20 or > 600000)
                    throw new FormatException("CAN 周期必须为 0 或 20～600000ms。");
                if (!int.TryParse(durationText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var duration) || duration is < 0 or > 1800000)
                    throw new FormatException("周期发送持续时间必须为 0～1800000ms。");
                if (period > 0 && target.Kind is not (TestTargetKind.RawCan or TestTargetKind.CanMessage))
                    throw new FormatException("只有 ECU CAN 报文可以进行周期发送。");
                if (target.Kind == TestTargetKind.CanMessage && target.Role != BusRole.DutCan)
                    throw new InvalidOperationException("DBC 发送目标必须在 ECU CAN。");
                if (period == 0 && duration > 0 &&
                    target.Kind is TestTargetKind.RawCan or TestTargetKind.CanMessage)
                    throw new FormatException("需要先设置 CAN 发送周期才能设置持续时间。");
                if (action.StartsWith("$", StringComparison.Ordinal))
                    continue; // Runtime rejects an unavailable or invalid variable.

                if (target.Kind == TestTargetKind.RawKey)
                    _ = RawKeyAction.Parse(action);
                else if (target.Kind == TestTargetKind.RawCan)
                {
                    if (!action.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("原始 CAN 数据必须以 HEX: 开头。");
                    var raw = Convert.FromHexString(string.Concat(action[4..]
                        .Where(c => !char.IsWhiteSpace(c))));
                    if (raw.Length > 8)
                        throw new FormatException("经典 CAN 最多只能发送 8 字节。");
                }
                else if (target.Kind == TestTargetKind.CanMessage)
                {
                    if (!_can.TryGetValue(target.Name, out var mapping) || mapping.Target != target)
                        throw new InvalidOperationException("DBC 报文绑定无效。");
                    _ = DbcCanMessageEncoder.Encode(mapping.Message, action);
                }
                else if (target.Kind == TestTargetKind.Ignition)
                {
                    if (action is not ("ON" or "OFF" or "on" or "off" or "true" or "false" or "0" or "1"))
                        throw new FormatException("IGN 仅支持 ON/OFF/true/false/0/1。");
                }
                else if (target.Kind == TestTargetKind.Button)
                {
                    var parts = action.Split(':', 2);
                    if (!Enum.TryParse<ButtonActuation>(parts[0], true, out _))
                        throw new FormatException("功能按键只能选择 Pulse、Hold、Release。");
                    if (parts.Length == 2 && (!int.TryParse(parts[1],
                            NumberStyles.None, CultureInfo.InvariantCulture, out var hold) ||
                        hold is < 1 or > 60000))
                        throw new FormatException("按键时间必须在 1～60000ms 之间。");
                }
                else
                    throw new InvalidOperationException("目标类型不允许执行硬件 Write：" + target.Kind);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or
                InvalidOperationException or ProjectDataException)
            {
                throw new InvalidOperationException("运行前步骤检查失败“" + node.Name +
                    "”（" + node.Id + "）：" + error.Message, error);
            }
        }
    }

    public ValueTask CheckHealthAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_periodicErrors.TryPeek(out var fault))
            throw new IOException("ECU CAN 周期发送发生通信故障，已停止后续步骤。", fault);
        if (_unknown)
            throw new IOException("KEY_SIM 输出状态 UNKNOWN，停止其他真实硬件操作。");
        return ValueTask.CompletedTask;
    }

    public IReadOnlyCollection<(byte Port, bool Extended, uint Id)> AuthorizedCanFrames(
        IReadOnlyList<TestTargetMapping> mappings)
    {
        var frames = new HashSet<(byte Port, bool Extended, uint Id)>();
        if (_buttons.Count > 0 || _hasRawKey)
            frames.Add((StationCanWiring.KeypadPort, false, KeypadCommandCodec.KeypadId));
        foreach (var raw in mappings.Where(x => x.Kind == TestTargetKind.RawCan))
            frames.Add((StationCanWiring.DutPort, raw.Extended, raw.CanId));
        foreach (var item in _can.Values)
        {
            var map = item.Target;
            frames.Add((StationCanWiring.PortFor(map.Role), map.Extended, map.CanId));
        }
        return frames.ToArray();
    }

    public async Task AuthorizeAsync(string fingerprint, string typedConfirmation,
        IReadOnlyList<TestTargetMapping> mappings, TestDocument document, CancellationToken ct)
    {
        if (_fingerprint is not null)
            throw new InvalidOperationException("A test run is already active.");
        // The USB fence is intentionally finite. Periodic or looped steps need
        // a larger, still hardware-bounded quota; otherwise two 65ms VCU frames
        // exhaust the default 1000 writes during a normal test.
        var highRate = TestRunStepScope.EnabledSteps(document).Any(node =>
            node.Type == "Loop" || node.Type == "Write" &&
                int.TryParse(node.Properties.GetValueOrDefault("periodMs", "0"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var period) &&
                period > 0);
        var maxWrites = highRate ? 10000 : 1000;
        await _hardware.AuthorizeTestRunAsync(fingerprint, typedConfirmation,
            AuthorizedCanFrames(mappings),
            mappings.Any(x => x.Kind == TestTargetKind.Ignition), ct,
            maxWrites: maxWrites);
        _fingerprint = fingerprint;
    }

    public async ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target, string action,
        CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            if (_fingerprint is null || _unknown)
                throw new InvalidOperationException(_unknown
                    ? "Previous physical output is UNKNOWN; operator intervention required."
                    : "No approved test run.");
            if (action.StartsWith("PERIODIC:", StringComparison.OrdinalIgnoreCase))
            {
                if (target.Kind is not (TestTargetKind.CanMessage or TestTargetKind.RawCan))
                    throw new InvalidOperationException("周期发送只能使用 ECU CAN 原始帧或 DBC 报文。");
                var fields = action.Split(':', 4);
                if (fields.Length != 4 ||
                    !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture,
                        out var period) || period is < 20 or > 600000 ||
                    !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture,
                        out var duration) || duration is < 0 or > 1_800_000)
                    throw new FormatException("周期发送须设置 20～600000 毫秒周期，持续时间为 0～1800000 毫秒。");
                byte[] payload;
                if (target.Kind == TestTargetKind.CanMessage)
                {
                    if (!_can.TryGetValue(target.Name, out var bound) || bound.Target != target)
                        throw new InvalidOperationException("周期 CAN 报文的 DBC 绑定已失效。");
                    payload = DbcCanMessageEncoder.Encode(bound.Message, fields[3]);
                }
                else
                {
                    if (!fields[3].StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("原始周期 CAN 报文必须填写 HEX 数据。");
                    payload = Convert.FromHexString(string.Concat(fields[3][4..]
                        .Where(c => !char.IsWhiteSpace(c))));
                    if (payload.Length > 8)
                        throw new FormatException("当前仅支持经典 CAN 的 8 字节数据。");
                }
                var frame = CanFrame.Create(BusRole.DutCan, target.Extended,
                    target.CanId, payload);
                // Replace the previous same-ID sender BEFORE writing the new
                // data. Otherwise the old periodic worker could retransmit
                // stale bytes after the new first frame was acknowledged.
                var key = (target.Role, target.Extended, target.CanId);
                if (_periodic.Remove(key, out var previous))
                {
                    previous.Stop.Cancel();
                    try { await previous.Worker; }
                    finally { previous.Stop.Dispose(); }
                }
                // The replacement is committed only after a real device ACK.
                await _hardware.SendTestCanAsync(frame, _fingerprint, ct);
                var stop = new CancellationTokenSource();
                var worker = SendPeriodicAsync(frame, period, duration, _fingerprint, stop.Token);
                _periodic[key] = (stop, worker);
                return Ack("CAN_PERIODIC_STARTED");
            }
            if (target.Kind == TestTargetKind.Ignition)
            {
                var on = action.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                         action.Equals("true", StringComparison.OrdinalIgnoreCase) || action == "1";
                if (!on && !action.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
                    !action.Equals("false", StringComparison.OrdinalIgnoreCase) && action != "0")
                    throw new FormatException("IGN value must be ON/OFF.");
                await _hardware.SendTestIgnAsync(on, _fingerprint, ct);
                return Ack("IGN_" + (on ? "ON" : "OFF"));
            }
            if (target.Kind == TestTargetKind.RawCan)
            {
                target.Validate();
                if (!action.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("原始 CAN 报文必须以 HEX: 指定完整数据。");
                byte[] payload;
                try { payload = Convert.FromHexString(
                    string.Concat(action[4..].Where(c => !char.IsWhiteSpace(c)))); }
                catch (FormatException ex)
                { throw new FormatException("原始 CAN 数据不是合法的十六进制字节。", ex); }
                if (payload.Length > 8)
                    throw new FormatException("当前固定台架仅支持经典 CAN，最多 8 字节。");
                await _hardware.SendTestCanAsync(
                    CanFrame.Create(BusRole.DutCan, target.Extended, target.CanId, payload),
                    _fingerprint, ct);
                return Ack("RAW_CAN_TX_ACK");
            }
            if (target.Kind == TestTargetKind.RawKey)
            {
                if (!_hasRawKey || _buttons.Count > 0)
                    throw new InvalidOperationException("原始 20 Key 模式未授权或与功能按键冲突。");
                var request = RawKeyAction.Parse(action);
                if (request.Action == RawKeyOperation.Pulse && _rawMask != 0)
                    throw new InvalidOperationException("原始 Key 正在保持，先释放后再短按。");
                bool rawKeyAttempted = false;
                try
                {
                    rawKeyAttempted = true;
                    if (request.Action == RawKeyOperation.Release ||
                        request.Action == RawKeyOperation.Hold && request.Mask == 0)
                    {
                        // A zero-mask HOLD means all switches open for the
                        // requested time. Unlike an immediate Release, the
                        // duration is part of the executing TEST step.
                        await _hardware.SendTestCanAsync(
                            KeypadCommandCodec.EncodeReleaseAll(), _fingerprint, ct);
                        _rawMask = 0;
                        if (request.Action == RawKeyOperation.Hold && request.DurationMs > 0)
                            await Task.Delay(request.DurationMs, ct);
                    }
                    else
                    {
                        await _hardware.SendTestCanAsync(
                            KeypadCommandCodec.EncodeMask(request.Mask), _fingerprint, ct);
                        _rawMask = request.Mask;
                        if (request.Action == RawKeyOperation.Hold && request.DurationMs > 0)
                            await Task.Delay(request.DurationMs, ct);
                        if (request.Action == RawKeyOperation.Pulse)
                        {
                            await Task.Delay(request.DurationMs, ct);
                            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            await _hardware.SendTestCanAsync(
                                KeypadCommandCodec.EncodeReleaseAll(), _fingerprint, release.Token);
                            _rawMask = 0;
                        }
                    }
                    return Ack("RAW_KEY_" + request.Action.ToString().ToUpperInvariant());
                }
                catch
                {
                    if (rawKeyAttempted)
                    {
                        _unknown = true;
                        try
                        {
                            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            await _hardware.SendTestCanAsync(
                                KeypadCommandCodec.EncodeReleaseAll(), _fingerprint, release.Token);
                            _rawMask = 0; _unknown = false;
                        }
                        catch { /* Physical state remains UNKNOWN. */ }
                    }
                    throw;
                }
            }
            if (target.Kind == TestTargetKind.CanMessage)
            {
                if (!_can.TryGetValue(target.Name, out var bound) || bound.Target != target)
                    throw new InvalidOperationException("CAN mapping was changed during run.");
                // The full mapped DBC message is the source of truth for physical-value
                // writes. HEX remains an explicitly recognizable engineering option.
                byte[] data = DbcCanMessageEncoder.Encode(bound.Message, action);
                var frame = CanFrame.Create(target.Role, target.Extended, target.CanId, data);
                await _hardware.SendTestCanAsync(frame, _fingerprint, ct);
                return Ack("CAN_TX_ACK");
            }
            if (target.Kind != TestTargetKind.Button ||
                !_buttons.TryGetValue(target.Name, out var button) ||
                button.State.ControllerId != target.ControllerId ||
                button.State.StateId != target.StateId || _policy is null)
                throw new InvalidOperationException("Configured button mapping is unavailable.");

            var words = action.Split(':', 2);
            if (!Enum.TryParse<ButtonActuation>(words[0], true, out var actuation))
                throw new FormatException("Use Pulse, Hold or Release for a configured button.");
            var timing = button.State.Resolve(button.Profile).ActionTiming;
            if (words.Length == 2 && actuation == ButtonActuation.Pulse)
                timing = timing with
                {
                    DefaultPressDurationMs = int.Parse(words[1], CultureInfo.InvariantCulture)
                };
            var plan = ButtonActionPlanner.Resolve(button.Profile, _policy, _active,
                new ButtonActionRequest(button.State, actuation, timing));
            if (plan.Timing.DelayBeforePressMs > 0)
                await Task.Delay(plan.Timing.DelayBeforePressMs, ct);
            bool changed = false;
            bool attempted = false;
            try
            {
                var maskFrame = KeypadCommandCodec.EncodeMask(plan.MaskDuringOperation);
                attempted = true; // A failed USB write can still have modified hardware.
                await _hardware.SendTestCanAsync(maskFrame, _fingerprint, ct);
                changed = true;
                if (actuation == ButtonActuation.Pulse)
                {
                    await Task.Delay(plan.Timing.DefaultPressDurationMs, ct);
                    using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _hardware.SendTestCanAsync(
                        KeypadCommandCodec.EncodeMask(plan.MaskAfterOperation), _fingerprint,
                        release.Token);
                }
                _active = plan.ActiveAfterOperation;
                if (plan.Timing.DelayAfterReleaseMs > 0)
                    await Task.Delay(plan.Timing.DelayAfterReleaseMs, ct);
                return Ack("KEY_" + actuation.ToString().ToUpperInvariant());
            }
            catch
            {
                if (changed)
                {
                    try
                    {
                        using var emergencyRelease = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await _hardware.SendTestCanAsync(
                            KeypadCommandCodec.EncodeMask(plan.MaskAfterOperation),
                            _fingerprint, emergencyRelease.Token);
                        _active = plan.ActiveAfterOperation;
                    }
                    catch { _unknown = true; }
                }
                else if (attempted) _unknown = true;
                // A failure before a physical write was attempted does not establish
                // an unknown output state. A failed attempted write always does.
                throw;
            }
        }
        finally { _serial.Release(); }
    }

    // Separate scheduler for each active ECU CAN frame. This uses host timing rather
    // than pretending that the current host interface configures firmware 0x50.
    // All writes still traverse the same hardware authorization and ACK path.
    private async Task SendPeriodicAsync(CanFrame frame, int periodMs, int durationMs,
        string fingerprint, CancellationToken stop)
    {
        using var limited = new CancellationTokenSource();
        if (durationMs > 0) limited.CancelAfter(durationMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, limited.Token);
        try
        {
            var scheduled = System.Diagnostics.Stopwatch.GetTimestamp();
            var periodTicks = Math.Max(1L, (long)Math.Round(periodMs *
                (double)System.Diagnostics.Stopwatch.Frequency / 1000d));
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var deadline = MonotonicDeadline.Advance(scheduled, now, periodTicks);
                scheduled = deadline.NextTicks;
                // A missed deadline is skipped, not compensated with a CAN burst.
                var untilDue = System.Diagnostics.Stopwatch.GetElapsedTime(now, scheduled);
                if (untilDue > TimeSpan.Zero)
                    await Task.Delay(untilDue, linked.Token);
                await _hardware.SendTestCanAsync(frame, fingerprint, linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { _periodicErrors.Enqueue(error); }
    }

    private static TestOperationResult Ack(string code) =>
        new(true, false, code, "Device acknowledged; ECU physical response remains unverified.",
            DateTimeOffset.UtcNow);

    public async ValueTask StopPeriodicAsync(CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            if (_fingerprint is null)
                throw new InvalidOperationException("真实自动测试尚未授权。");
            await StopPeriodicWorkersAsync();
            if (_periodicErrors.TryDequeue(out var failure))
                throw new IOException("ECU CAN 周期发送发生设备通信错误。", failure);
        }
        finally { _serial.Release(); }
    }

    private async Task StopPeriodicWorkersAsync()
    {
        foreach (var item in _periodic.Values) item.Stop.Cancel();
        try { await Task.WhenAll(_periodic.Values.Select(x => x.Worker)); }
        finally
        {
            foreach (var item in _periodic.Values) item.Stop.Dispose();
            _periodic.Clear();
        }
    }

    public async ValueTask CleanupAsync(CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            if (_fingerprint is null) return;
            try
            {
                await StopPeriodicWorkersAsync();
                // Release regardless of our last ACK: a timed-out physical command can
                // have affected the board even if the host did not receive confirmation.
                if (_buttons.Count > 0 || _hasRawKey)
                {
                    await _hardware.SendTestCanAsync(KeypadCommandCodec.EncodeReleaseAll(),
                        _fingerprint, ct);
                    _active = []; _rawMask = 0;
                }
            }
            finally
            {
                _fingerprint = null;
                await _hardware.RevokeTestRunAsync();
            }
            if (_periodicErrors.TryDequeue(out var periodicFailure))
                throw new IOException("ECU CAN 周期发送期间出现设备错误，测试不能判为通过。",
                    periodicFailure);
        }
        finally { _serial.Release(); }
    }
}
