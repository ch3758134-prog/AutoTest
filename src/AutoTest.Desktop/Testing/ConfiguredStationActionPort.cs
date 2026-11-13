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

    public IReadOnlyCollection<(byte Port, bool Extended, uint Id)> AuthorizedCanFrames(
        IReadOnlyList<TestTargetMapping> mappings)
    {
        var frames = new HashSet<(byte Port, bool Extended, uint Id)>();
        if (_buttons.Count > 0 || _hasRawKey)
            frames.Add((0, false, KeypadCommandCodec.KeypadId));
        foreach (var raw in mappings.Where(x => x.Kind == TestTargetKind.RawCan))
            frames.Add((1, raw.Extended, raw.CanId));
        foreach (var item in _can.Values)
        {
            var map = item.Target;
            frames.Add((map.Role == BusRole.DutCan ? (byte)1 : (byte)0,
                map.Extended, map.CanId));
        }
        return frames.ToArray();
    }

    public async Task AuthorizeAsync(string fingerprint, string typedConfirmation,
        IReadOnlyList<TestTargetMapping> mappings, CancellationToken ct)
    {
        if (_fingerprint is not null)
            throw new InvalidOperationException("A test run is already active.");
        await _hardware.AuthorizeTestRunAsync(fingerprint, typedConfirmation,
            AuthorizedCanFrames(mappings),
            mappings.Any(x => x.Kind == TestTargetKind.Ignition), ct);
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
                bool attempted = false;
                try
                {
                    attempted = true;
                    if (request.Action == RawKeyOperation.Release)
                    {
                        await _hardware.SendTestCanAsync(
                            KeypadCommandCodec.EncodeReleaseAll(), _fingerprint, ct);
                        _rawMask = 0;
                    }
                    else
                    {
                        await _hardware.SendTestCanAsync(
                            KeypadCommandCodec.EncodeMask(request.Mask), _fingerprint, ct);
                        _rawMask = request.Mask;
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
                    if (attempted)
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

    private static TestOperationResult Ack(string code) =>
        new(true, false, code, "Device acknowledged; ECU physical response remains unverified.",
            DateTimeOffset.UtcNow);

    public async ValueTask CleanupAsync(CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            if (_fingerprint is null) return;
            try
            {
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
        }
        finally { _serial.Release(); }
    }
}
