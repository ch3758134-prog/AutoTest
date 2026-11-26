using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using AutoTest.FlashLab.Hardware;
using AutoTest.FlashLab.Programming;
using AutoTest.Contracts;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using AutoTest.Devices.Station;

namespace AutoTest.Desktop.Hardware;

/// <summary>
/// One real WinUSB session and one CAN hub for the whole station.
/// Only read-only USB ping and CAN receiving are activated on connect.
/// No mock fallback, automatic CAN transmit, or physical actuation.
/// </summary>
public sealed partial class HardwareConnectionService : IAsyncDisposable
{

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _keyRequestGate = new();
    private uint? _acknowledgedKeyMask;
    private long _acknowledgedKeyEpoch;
    private bool _keyRequestUncertain = true;
    public event Action? KeyRequestStateChanged;

    /// <summary>
    /// Last KEY mask accepted by the USB controller in this connection epoch.
    /// This is NOT a measurement of the five analogue voltages.
    /// Null means no reliable request state is known.
    /// </summary>
    public uint? AcknowledgedKeyRequestMask
    {
        get
        {
            lock (_keyRequestGate)
                return !_keyRequestUncertain && IsConnected &&
                    _acknowledgedKeyEpoch == CurrentStationEpoch
                    ? _acknowledgedKeyMask : null;
        }
    }

    public string KeyRequestStatus
    {
        get
        {
            var mask = AcknowledgedKeyRequestMask;
            if (!mask.HasValue)
                return "KEY 状态未知 · 请重新设置组合";
            if (mask.Value == 0)
                return "KEY：全部断开（主控已确认）";
            var keys = string.Join(" + ", Enumerable.Range(1, 20)
                .Where(n => (mask.Value & (1u << (n - 1))) != 0)
                .Select(n => "K" + n.ToString("00")));
            return "KEY 保持：" + keys + "（主控已确认）";
        }
    }

    private static bool TryDecodeKeyRequest(CanFrame frame, out uint mask)
    {
        mask = 0;
        if (frame.Role != BusRole.KeypadCan ||
            frame.Id != KeypadCommandCodec.KeypadId || frame.Data.Length != 8)
            return false;
        if (frame.Data[0] == 0x80) return true;
        if (frame.Data[0] != 0x01) return false;
        mask = (uint)frame.Data[1] | ((uint)frame.Data[2] << 8) |
            ((uint)(frame.Data[3] & 0x0F) << 16);
        return true;
    }

    private void ReportKeyRequestState()
    {
        foreach (Action callback in KeyRequestStateChanged?.GetInvocationList()
                     .Cast<Action>() ?? Enumerable.Empty<Action>())
            try { callback(); } catch { /* UI must not affect WinUSB. */ }
    }

    private void MarkKeyRequestUnknown()
    {
        lock (_keyRequestGate)
        {
            _keyRequestUncertain = true;
            _acknowledgedKeyMask = null;
            _acknowledgedKeyEpoch = 0;
        }
        ReportKeyRequestState();
    }

    private void ConfirmKeyRequest(CanFrame frame)
    {
        if (!TryDecodeKeyRequest(frame, out var mask)) return;
        lock (_keyRequestGate)
        {
            _acknowledgedKeyMask = mask;
            _acknowledgedKeyEpoch = CurrentStationEpoch;
            _keyRequestUncertain = false;
        }
        ReportKeyRequestState();
    }
    private int _flashSecuritySensitive;
    public bool IsFlashSecuritySensitive => Volatile.Read(ref _flashSecuritySensitive) != 0;

    // The active firmware exposes ECU-only USB 0x42 SET / 0x43 GET.
    // Never infer the feature from unchanged GET_VERSION text: probe 0x43
    // in the actual USB session and bind every readback to station epoch.
    public sealed record EcuCanRateChoice(int Kbps, string Label);
    public static IReadOnlyList<EcuCanRateChoice> EcuCanRateChoices { get; } =
    [
        new(20, "20 kbit/s"), new(50, "50 kbit/s"),
        new(100, "100 kbit/s"), new(125, "125 kbit/s"),
        new(250, "250 kbit/s"), new(500, "500 kbit/s"),
        new(800, "800 kbit/s"), new(1000, "1 Mbit/s")
    ];
    private int _requestedEcuCanBitrateKbps = 500;
    private int _confirmedEcuCanBitrateKbps;
    private long _confirmedEcuCanBitrateEpoch;
    private int _legacyFixed500;
    public event Action<int>? EcuCanBitrateSelectionChanged;
    public int RequestedEcuCanBitrateKbps => Volatile.Read(ref _requestedEcuCanBitrateKbps);
    public int ConfirmedEcuCanBitrateKbps =>
        IsConnected && Interlocked.Read(ref _confirmedEcuCanBitrateEpoch) == CurrentStationEpoch
            ? Volatile.Read(ref _confirmedEcuCanBitrateKbps) : 0;
    public bool IsEcuCanBitrateApplied =>
        ConfirmedEcuCanBitrateKbps == RequestedEcuCanBitrateKbps;
    public string EcuCanBitrateStatus
    {
        get
        {
            var target = RequestedEcuCanBitrateKbps;
            if (!IsConnected) return "未连接 · 目标 " + target + "k";
            int actual = ConfirmedEcuCanBitrateKbps;
            if (actual == 0) return "状态未知 · 请点击应用/读回";
            if (actual != target)
                return "未生效 · 当前 " + actual + "k / 目标 " + target + "k";
            return Volatile.Read(ref _legacyFixed500) != 0
                ? "旧固件固定 500k · 无 0x43 读回"
                : "已确认 " + actual + "k · USB 0x43 读回";
        }
    }

    private void PublishEcuCanBaudState()
    {
        foreach (Action<int> subscriber in EcuCanBitrateSelectionChanged?.GetInvocationList()
                     .Cast<Action<int>>() ?? Enumerable.Empty<Action<int>>())
            try { subscriber(RequestedEcuCanBitrateKbps); }
            catch { /* UI notifications must not affect hardware authorization. */ }
    }

    private void SetObservedEcuCanBaud(int kbps, long epoch, bool legacy)
    {
        Volatile.Write(ref _confirmedEcuCanBitrateKbps, kbps);
        Volatile.Write(ref _legacyFixed500, legacy ? 1 : 0);
        Interlocked.Exchange(ref _confirmedEcuCanBitrateEpoch, epoch);
        PublishEcuCanBaudState();
    }

    public void SelectRequestedEcuCanBitrate(int kbps)
    {
        _ = MainControllerCodec.EcuCanBaudCode(kbps);
        if (_testRunApproved || _manualCanBatchApproved ||
            _manualKeyActionApproved || IsFlashSecuritySensitive ||
            _lifecycle.CurrentCount == 0)
            throw new InvalidOperationException("工位正在执行硬件操作，禁止改变 ECU CAN 波特率设置。");
        Volatile.Write(ref _requestedEcuCanBitrateKbps, kbps);
        Record("ECU CAN 目标码率 " + kbps +
            " kbit/s；选择仅修改目标值，必须核对 USB 0x43，必要时点击应用。");
        PublishEcuCanBaudState();
    }

    /// <summary>Explicitly set ECU CAN on an idle, connected real controller.
    /// Successful 0x42 ACK is not sufficient; 0x43 must confirm the target in
    /// the same USB session. Firmware 0x05 busy propagates without retrying.</summary>
    public async Task<int> ApplyRequestedEcuCanBitrateAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected ||
                CurrentStationEpoch <= 0)
                throw new IOException("先连接真实 USB 主控再应用 ECU CAN 波特率。");
            if (_testRunApproved || _manualCanBatchApproved ||
                _manualKeyActionApproved || IsFlashSecuritySensitive)
                throw new InvalidOperationException(
                    "TEST、CAN、KEY、FLASH 占用工位期间禁止切换 ECU CAN 波特率。");
            var epoch = CurrentStationEpoch;
            var target = RequestedEcuCanBitrateKbps;
            try
            {
                // Always re-read first, even if the UI previously confirmed a
                // value, to avoid configuring a different live controller.
                int before;
                try
                {
                    before = await MainControllerCodec.ReadEcuCanBaudAsync(_usb, ct);
                }
                catch (UsbDeviceStatusException error)
                    when (error.Command == MainControllerCodec.GetEcuCanBaud &&
                          error.Status == 0x03)
                {
                    if (target != 500)
                        throw new NotSupportedException(
                            "连接的主控固件不支持 ECU CAN 0x42/0x43；只能使用旧固件固定 500k。",
                            error);
                    SetObservedEcuCanBaud(500, epoch, legacy: true);
                    Record("主控不支持 USB 0x43；按旧固件启动固定 500k 使用，未作实际总线测速。");
                    return 500;
                }
                if (before != target)
                {
                    // Any unknown 0x42 outcome must revoke previous confidence.
                    SetObservedEcuCanBaud(0, 0, legacy: false);
                    Record("ECU CAN USB 0x42 申请 " + target +
                        "k；修改前主控 0x43=" + before + "k。");
                    _link!.AuthorizeOneEcuCanBaudChange(MainControllerCodec.EcuCanBaudCode(target));
                    await MainControllerCodec.SetAndVerifyEcuCanBaudAsync(_usb,
                        target, ct);
                }
                if (!_usb.State.Equals(UsbSessionState.Connected) ||
                    CurrentStationEpoch != epoch)
                    throw new IOException("设置 ECU CAN 速率时 USB 会话已变化，状态未知。");
                SetObservedEcuCanBaud(target, epoch, legacy: false);
                Record("ECU CAN 波特率已由主控 USB 0x43 确认 " + target +
                    "k；这不是总线分析仪测得的物理速率。KEY CAN 未修改。");
                return target;
            }
            catch (Exception error)
            {
                SetObservedEcuCanBaud(0, 0, legacy: false);
                Record("ECU CAN 设置/读回失败，已撤销确认，禁止 DUT CAN 发送：" +
                    error.Message);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public void RequireConfiguredEcuCanBitrate()
    {
        if (!IsConnected || CurrentStationEpoch <= 0 ||
            !IsEcuCanBitrateApplied)
            throw new InvalidOperationException(
                "ECU CAN 目标码率尚未确认应用：" + EcuCanBitrateStatus +
                "；请停止工位任务，点击“应用/读回”并核实状态后再进行真实 DUT CAN/TEST/FLASH。");
    }
    private WinUsbByteLink? _link;
    private UsbSessionService? _usb;
    private CanFrameHub? _hub;
    private HardwareCanReceiveBridge? _bridge;
    private CancellationTokenSource? _monitorStop;
    private Task[] _monitorTasks = Array.Empty<Task>();
    public bool IsConnected => _usb?.State == UsbSessionState.Connected;
    public bool HasSession => _usb is not null;
    /// <summary>Discovery identity for the currently ping-confirmed USB session, not an ECU identity.</summary>
    public string? ConnectedDeviceIdentity { get; private set; }
    public string? ConnectedFirmwareVersion { get; private set; }
    private long _ledCapabilityEpoch;
    // Capability is confirmed by a successful GET_LEDS response, not by
    // the unchanged v0.4-dev firmware version text.
    public bool HasLedGpioContract => IsConnected && CurrentStationEpoch > 0 &&
        Interlocked.Read(ref _ledCapabilityEpoch) == CurrentStationEpoch;
    /// <summary>Only source-reported v0.4-dev supports the independent clamp protocol.
    /// This string is not a physical firmware hash or wiring attestation.</summary>
    public bool HasV02GpioContract => IsConnected &&
        string.Equals(ConnectedFirmwareVersion, MainControllerCodec.ClampContractVersion,
            StringComparison.Ordinal);
    public ICanFrameHub? ConnectedCanHub => IsConnected ? _hub : null;
    public event Action<string>? EvidenceRecorded;
    public event Action<CanObservation>? FrameObserved;
    /// <summary>
    /// Host-observed successful USB 0x40 ACK, NOT a physical CAN-bus transmit
    /// confirmation or ECU reception. Never expose sensitive FLASH traffic.
    /// </summary>
    public event Action<CanFrame>? FrameTransmitAcknowledged;
    private void PublishTransmitAck(CanFrame frame)
    {
        if (IsFlashSecuritySensitive) return;
        foreach (Action<CanFrame> handler in FrameTransmitAcknowledged?.GetInvocationList()
                     .Cast<Action<CanFrame>>() ?? Enumerable.Empty<Action<CanFrame>>())
        {
            try { handler(frame); }
            catch (Exception error) { Record("CAN TX 监视者异常：" + error.Message); }
        }
    }
    private long _canMonitorDelivered;
    private long _canMonitorObserverErrors;
    /// <summary>Stage-by-stage live RX diagnostics, including before any DBC parsing.
    /// USB 0x41 count is hardware notifications, not proof the DUT transmitted.</summary>
    public string CanReceiveStatus => !IsConnected ? "USB 未连接；无实时接收" :
        "USB 0x41=" + (_usb?.ReceivedCanIndications ?? 0) +
        " / CAN有效=" + (_bridge?.Published ?? 0) +
        " / 帧格式异常=" + (_bridge?.InvalidFrames ?? 0) +
        " / 桥接队列溢出=" + (_bridge?.DroppedBeforeDecode ?? 0) +
        " / USB解帧异常=" + (_usb?.BadUsbFrameCandidates ?? 0) +
        " / 接收订阅=" + (_hub?.SubscriberCount ?? 0) +
        " / 监视交付=" + Interlocked.Read(ref _canMonitorDelivered) +
        " / 视图处理异常=" + Interlocked.Read(ref _canMonitorObserverErrors);
    public long RawCanIndicationCount => _usb?.ReceivedCanIndications ?? 0;
    private long _epochCounter;
    private long _currentEpoch;
    public long CurrentStationEpoch => Interlocked.Read(ref _currentEpoch);
    public event Action<long>? ConnectionChanged;
    private volatile bool _testRunApproved;
    private string? _approvedPlan;
    private int _testRunGrantBudget;
    private int _testRunGrantUsed;
    private DateTimeOffset _testRunRefreshAt;
    private volatile bool _manualCanBatchApproved;
    private string? _manualBatchFingerprint;
    private readonly ConcurrentDictionary<Guid, string> _manualCanRows = new();
    private volatile bool _manualKeyActionApproved;
    private string? _manualKeyActionFingerprint;
    public bool IsManualKeyActionApproved => _manualKeyActionApproved;
    public bool IsManualCanBatchApproved => _manualCanBatchApproved;
    /// <summary>Outstanding manual CAN row leases belong to the actual bench;
    /// model/bitrate changes must wait until each lease is released.</summary>
    public bool HasManualCanRowLeases => !_manualCanRows.IsEmpty;
    public event Action<bool>? ManualCanBatchChanged;
    private void AnnounceManualBatch(bool active)
    {
        try { ManualCanBatchChanged?.Invoke(active); }
        catch (Exception ex) { Record("手动 CAN 任务状态通知失败：" + ex.Message); }
    }
    public bool IsTestRunApproved => _testRunApproved;
    public event Action<bool>? TestRunAuthorizationChanged;
    private void PublishTestRunAuthorization(bool isApproved)
    {
        try { TestRunAuthorizationChanged?.Invoke(isApproved); }
        catch (Exception ex) { Record("Test Run 授权状态通知失败：" + ex.Message); }
    }

    private void Record(string text)
    {
        var entry = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "  " + text;
        // An already-closed window must never bring down the USB or CAN receiver.
        foreach (Action<string> sink in EvidenceRecorded?.GetInvocationList()
                     .Cast<Action<string>>() ?? Enumerable.Empty<Action<string>>())
        {
            try { sink(entry); } catch { /* Diagnostic observers cannot stop the station. */ }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        UsbSessionService? pending = null;
        try
        {
            if (_usb is not null) throw new InvalidOperationException("请先断开现有连接。");
            var candidates = new WinUsbDiscovery().ListCandidates();
            if (candidates.Count != 1)
                throw new IOException("需要恰好一台已识别的主控板，实际发现 " + candidates.Count +
                    " 台；不会自动选择不明 USB 设备。");
            var candidate = candidates[0];
            Record("USB 发现：" + candidate.DisplayIdentity + "，驱动：" + (candidate.DriverService ?? "未知"));
            var link = new WinUsbByteLink(candidate.DeviceInstanceId);
            link.OperationRecorded += ev =>
                Record("USB " + ev.Operation + "/" + ev.Stage + " " + ev.Outcome +
                    (ev.NativeErrorHex is null ? "" : " " + ev.NativeErrorHex));
            pending = new UsbSessionService(link);
            pending.StateChanged += state =>
            {
                if (state != UsbSessionState.Faulted) return;
                Interlocked.Exchange(ref _currentEpoch, 0);
                MarkKeyRequestUnknown();
                Interlocked.Exchange(ref _ledCapabilityEpoch, 0);
                SetObservedEcuCanBaud(0, 0, legacy: false);
                _link?.RevokePhysicalTestRun();
                _link?.RevokeManualCanBatch();
                _link?.RevokeManualKeySequence();
                _manualKeyActionApproved = false;
                _manualKeyActionFingerprint = null;
                _manualCanBatchApproved = false;
                _manualBatchFingerprint = null;
                _manualCanRows.Clear();
                _link?.RevokeAllManualCanRows();
                AnnounceManualBatch(false);
                _testRunApproved = false;
                _approvedPlan = null;
                PublishTestRunAuthorization(false);
                Record("USB RX 已故障；实体状态 UNKNOWN，需断开旧会话后重新连接。");
                try { ConnectionChanged?.Invoke(0); } catch { }
            };
            pending.TraceRecorded += trace =>
            {
                if (trace.Kind is "CONNECT_ERROR" or "READ_FAULT" or "REQUEST_TIMEOUT" or
                    "REQUEST_ERROR" or "CONNECTED" or "DISCONNECTED")
                    Record("SESSION " + trace.Kind + " G" + trace.Generation +
                        (trace.Detail is null ? "" : " " + trace.Detail));
            };
            await pending.ConnectAsync(cancellationToken);
            // An acknowledged F0 ping confirms the protocol path, not CAN bus/ECU power.
            var ping = await pending.RequestAsync(MainControllerCodec.Ping, ReadOnlyMemory<byte>.Empty,
                TimeSpan.FromSeconds(2), cancellationToken);
            if (!ping.Payload.AsSpan().SequenceEqual("PONG"u8))
                throw new InvalidDataException("USB PING returned an unexpected payload.");
            var version = await pending.RequestAsync(MainControllerCodec.GetVersion,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(2), cancellationToken);
            if (version.Payload.Length is < 1 or > 80 ||
                version.Payload.Any(value => value < 0x20 || value > 0x7E))
                throw new InvalidDataException("GET_VERSION did not return printable ASCII.");
            ConnectedFirmwareVersion = Encoding.ASCII.GetString(version.Payload);
            Record("GET_VERSION：" + ConnectedFirmwareVersion +
                "（设备自报版本，不是固件哈希或烧录内容证据）");
            // Probe the actual firmware. GET_VERSION is NOT a capability
            // discriminator; the old and new firmware use the same label.
            int observedRate = 0;
            bool legacy500 = false;
            try
            {
                observedRate = await MainControllerCodec.ReadEcuCanBaudAsync(
                    pending, cancellationToken);
                Record("ECU CAN 0x43 实际配置读回：" + observedRate + "k。");
            }
            catch (UsbDeviceStatusException error)
                when (error.Command == MainControllerCodec.GetEcuCanBaud &&
                      error.Status == 0x03)
            {
                // Only a proven command-not-supported response permits
                // compatibility with the pre-0x42 firmware's fixed 500k.
                observedRate = 500;
                legacy500 = true;
                Record("主控返回 ECU CAN 0x43 ERR_CMD：按旧版固定 500k 兼容。");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Record("ECU CAN 0x43 查询失败：" + error.Message +
                    "；状态未知，需重试应用/读回才能发送 DUT CAN。");
            }
            ConnectedDeviceIdentity = candidate.DisplayIdentity;
            _link = link;
            _usb = pending;
            _hub = new CanFrameHub();
            _bridge = new HardwareCanReceiveBridge(pending, _hub);
            _bridge.Trace += trace =>
            {
                if (!trace.Valid) Record("CAN RX 异常：" + trace.Detail);
            };
            _monitorStop = new CancellationTokenSource();
            _monitorTasks = new[]
            {
                ObserveBusAsync(_hub, BusRole.DutCan, _monitorStop.Token),
                ObserveBusAsync(_hub, BusRole.KeypadCan, _monitorStop.Token)
            };
            Interlocked.Exchange(ref _currentEpoch, Interlocked.Increment(ref _epochCounter));
            MarkKeyRequestUnknown();
            SetObservedEcuCanBaud(observedRate,
                observedRate == 0 ? 0 : CurrentStationEpoch, legacy500);
            try { ConnectionChanged?.Invoke(CurrentStationEpoch); } catch { }
            Record("主控连接确认；ECU CAN（A1/A12）、KEY CAN（B1/B11）；接收监视已启动。");
        }
        catch
        {
            if (_usb is null && pending is not null)
            {
                try { await pending.DisposeAsync(); }
                catch (Exception e) { Record("连接清理失败：" + e.Message); }
            }
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    private async Task ObserveBusAsync(CanFrameHub hub, BusRole role, CancellationToken ct)
    {
        try
        {
            var spec = new CanSubscriptionSpec(role, ImmutableArray<uint>.Empty,
                CanBackpressurePolicy.DropWithLossFact, 1024);
            await foreach (var frame in hub.SubscribeAsync(spec, ct))
            {
                if (IsFlashSecuritySensitive) continue; // Do not publish raw Seed/Key frames.
                // A closed window or bad UI observer must never terminate the
                // long-lived CAN receiver for all tools and tests.
                foreach (Action<CanObservation> observer in FrameObserved?.GetInvocationList()
                             .Cast<Action<CanObservation>>() ?? Enumerable.Empty<Action<CanObservation>>())
                {
                    try { observer(frame); Interlocked.Increment(ref _canMonitorDelivered); }
                    catch (Exception e)
                    {
                        var failures = Interlocked.Increment(ref _canMonitorObserverErrors);
                        if (failures == 1 || (failures & (failures - 1)) == 0)
                            Record("CAN 监视视图处理失败 #" + failures + "：" + e.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { Record("CAN 监视异常：" + e.Message); }
    }

    /// <summary>One physically authorized classic CAN frame. The transport fence consumes
    /// the consent before native USB write; firmware ACK is not proof of DUT receipt.</summary>
    public async Task SendCanOnceAsync(CanFrame frame, string exactConfirmation,
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null)
                throw new IOException("USB 主控未连接，禁止 CAN 发送。");
            if (frame.Role == BusRole.DutCan) RequireConfiguredEcuCanBitrate();
            if (_testRunApproved || (_manualKeyActionApproved && frame.Role == BusRole.KeypadCan) ||
                (_manualCanBatchApproved && frame.Role != BusRole.KeypadCan))
                throw new InvalidOperationException("ECU CAN 周期任务运行期间，仅允许同一工位已授权的 KEY CAN 按键命令。");
            var map = StationCanWiring.Current;
            var payload = CanFrameCodec.ToUsbPayload(frame, map);
            _link.AuthorizeOneCanTransmission(payload, exactConfirmation);
            Record("CAN TX 授权：" + frame.Role + " ID=0x" + frame.Id.ToString("X") +
                " data=" + Convert.ToHexString(frame.Data.ToArray()));
            if (TryDecodeKeyRequest(frame, out _)) MarkKeyRequestUnknown();
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), cancellationToken);
            ConfirmKeyRequest(frame);
            PublishTransmitAck(frame);
            Record("CAN TX 设备 ACK；不能据此认定 DUT 已收到该帧。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>A complete KEY action is authorized once; writes are ordered at USB fence.</summary>
    public async Task AuthorizeManualKeyActionAsync(string fingerprint, string confirmation,
        IReadOnlyList<CanFrame> frames, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null ||
                _testRunApproved || _manualKeyActionApproved)
                throw new InvalidOperationException("USB 未连接或 KEY/TEST 任务正在运行。");
            if (frames.Count is < 1 or > 512 || frames.Any(frame => frame.Role != BusRole.KeypadCan))
                throw new ArgumentException("KEY 动作只能使用 KEY CAN 发送帧。");
            var map = StationCanWiring.Current;
            var data = frames.Select(frame => CanFrameCodec.ToUsbPayload(frame, map)).ToArray();
            _link.AuthorizeManualKeySequence(fingerprint, confirmation, data);
            _manualKeyActionFingerprint = fingerprint;
            _manualKeyActionApproved = true;
            Record("KEY_SIM 序列授权，段数=" + frames.Count + " SHA=" + fingerprint);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SendManualKeyStepAsync(CanFrame frame, string fingerprint, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!_manualKeyActionApproved || _manualKeyActionFingerprint != fingerprint ||
                _usb?.State != UsbSessionState.Connected || _testRunApproved ||
                frame.Role != BusRole.KeypadCan)
                throw new UnauthorizedAccessException("当前 USB 会话没有有效 KEY 序列授权。");
            var payload = CanFrameCodec.ToUsbPayload(frame,
                StationCanWiring.Current);
            if (TryDecodeKeyRequest(frame, out _)) MarkKeyRequestUnknown();
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            ConfirmKeyRequest(frame);
            Record("KEY CAN 主控 ACK：0x" + frame.Id.ToString("X") +
                " DATA=" + Convert.ToHexString(frame.Data.AsSpan()) +
                "（非五路电压或 ECU 识别确认）");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeManualKeyActionAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokeManualKeySequence();
            _manualKeyActionApproved = false;
            _manualKeyActionFingerprint = null;
            Record("KEY 序列授权撤销；未发送任何 KEY 释放帧。" + KeyRequestStatus);
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Permit one exact ECU CAN frame for this connection. Each row
    /// can stop without revoking any other row's authorization.</summary>
    public async Task<Guid> AuthorizeManualCanRowAsync(CanFrame frame, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null ||
                _testRunApproved || _manualBatchFingerprint is not null ||
                IsFlashSecuritySensitive)
                throw new InvalidOperationException("USB 未连接或 TEST/FLASH 占用 ECU CAN。");
            if (frame.Role != BusRole.DutCan)
                throw new ArgumentException("只能在 ECU CAN 上发送手动报文。");
            RequireConfiguredEcuCanBitrate();
            var payload = CanFrameCodec.ToUsbPayload(frame, StationCanWiring.Current);
            var token = Guid.NewGuid();
            _link.AuthorizeManualCanRow(token, payload);
            if (!_manualCanRows.TryAdd(token, Convert.ToHexString(payload)))
            {
                _link.RevokeManualCanRow(token);
                throw new InvalidOperationException("CAN 发送授权标识冲突。");
            }
            if (!_manualCanBatchApproved)
            {
                _manualCanBatchApproved = true;
                AnnounceManualBatch(true);
            }
            return token;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SendManualCanRowAsync(CanFrame frame, Guid token, CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _testRunApproved ||
                frame.Role != BusRole.DutCan)
                throw new IOException("当前主控连接不可发送 ECU CAN。");
            RequireConfiguredEcuCanBitrate();
            var payload = CanFrameCodec.ToUsbPayload(frame, StationCanWiring.Current);
            if (!_manualCanRows.TryGetValue(token, out var granted) ||
                !string.Equals(granted, Convert.ToHexString(payload), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("报文授权已经失效或发送内容发生改变。");
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            PublishTransmitAck(frame);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeManualCanRowAsync(Guid token)
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokeManualCanRow(token);
            _manualCanRows.TryRemove(token, out _);
            if (_manualCanRows.Count == 0 && _manualBatchFingerprint is null)
            {
                _manualCanBatchApproved = false;
                AnnounceManualBatch(false);
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SendManualBenchCanOnceAsync(CanFrame frame,
        CancellationToken ct = default)
    {
        var token = await AuthorizeManualCanRowAsync(frame, ct);
        try { await SendManualCanRowAsync(frame, token, ct); }
        finally { await RevokeManualCanRowAsync(token); }
    }

    /// <summary>Authorize a bounded snapshot of ECU CAN frames, not an open-ended raw CAN route.</summary>
    public async Task AuthorizeManualCanBatchAsync(string fingerprint, string confirmation,
        IReadOnlyList<CanFrame> frames, int? maxWrites = 3000, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null ||
                _testRunApproved || _manualCanBatchApproved)
                throw new InvalidOperationException("USB 未连接或工位正在运行其他 CAN/TEST 任务。");
            RequireConfiguredEcuCanBitrate();
            var map = StationCanWiring.Current;
            var payloads = frames.Select(frame => CanFrameCodec.ToUsbPayload(frame, map)).ToArray();
            _link.AuthorizeManualCanBatch(fingerprint, confirmation, payloads, maxWrites);
            _manualBatchFingerprint = fingerprint;
            _manualCanBatchApproved = true;
            AnnounceManualBatch(true);
            Record("手动 ECU CAN 任务已授权：报文数=" + frames.Count + " SHA=" + fingerprint +
                (maxWrites.HasValue ? "，最多 " + maxWrites + " 次写入 / 5 分钟。" : "，持续发送至停止或当前 USB 会话结束。"));
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SendManualBatchCanAsync(CanFrame frame, string fingerprint, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!_manualCanBatchApproved || _manualBatchFingerprint != fingerprint ||
                _usb?.State != UsbSessionState.Connected || _testRunApproved ||
                frame.Role != BusRole.DutCan)
                throw new UnauthorizedAccessException("没有当前有效的 ECU CAN 手动发送授权。");
            RequireConfiguredEcuCanBitrate();
            var payload = CanFrameCodec.ToUsbPayload(frame,
                StationCanWiring.Current);
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            PublishTransmitAck(frame);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeManualCanBatchAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokeManualCanBatch();
            // ECU CAN and KEY CAN are separate manual tasks.
            _manualCanBatchApproved = false;
            _manualBatchFingerprint = null;
            _manualCanRows.Clear();
            _link?.RevokeAllManualCanRows();
            AnnounceManualBatch(false);
            Record("ECU CAN 任务已停止；不代表 ECU 电气状态发生改变。");
        }
        finally { _lifecycle.Release(); }
    }

    private void RequireV02GpioContract()
    {
        if (!HasV02GpioContract)
            throw new InvalidOperationException(
                "夹具命令协议尚未确认。此上位机按独立夹具两字节格式 0x18 [夹具号, 压紧/释放] 发令，" +
                "Main_Control 源码候选自报 " + MainControllerCodec.ClampContractVersion +
                "，而当前设备自报 " + (ConnectedFirmwareVersion ?? "未知") +
                "。不能仅凭版本字符串判断是否兼容；为避免错误驱动夹具，本次未发送控制指令。");
    }

    /// <summary>Read MCU GPIO only; not J1 voltage or ECU ignition state.</summary>
    public async Task<bool> ReadIgnLevelAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!IsConnected)
                throw new IOException("USB 主控未连接，无法读取 IGN GPIO。");
            var response = await _usb!.RequestAsync(MainControllerCodec.GetIgn,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct);
            if (response.Payload.Length != 1 || response.Payload[0] > 1)
                throw new InvalidDataException("GET_IGN must return a single GPIO bit.");
            Record("GET_IGN MCU GPIO=" + response.Payload[0] + "（非 J1 实测电压）");
            return response.Payload[0] == 1;
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Two independent clamp GPIO bits; never a mechanical sensor measurement.</summary>
    public async Task<byte> ReadClampLevelsAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            RequireV02GpioContract();
            var response = await _usb!.RequestAsync(MainControllerCodec.GetClamps,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct);
            if (response.Payload.Length != 1 || (response.Payload[0] & 0xFC) != 0)
                throw new InvalidDataException("GET_CLAMPS may contain only bit0/bit1.");
            Record("GET_CLAMPS GPIO 0x" + response.Payload[0].ToString("X2") +
                "（非气阀/机械位置测量）");
            return response.Payload[0];
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Read six MCU LED GPIO levels through Main_Control 0x1B.
    /// A valid response proves command compatibility, not external LED voltage.</summary>
    public async Task<byte> ReadLedLevelsAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!IsConnected)
                throw new IOException("USB 主控未连接，无法读取 LED GPIO。");
            try
            {
                var response = await _usb!.RequestAsync(MainControllerCodec.GetLeds,
                    ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct);
                if (response.Payload.Length != 1 || (response.Payload[0] & 0xC0) != 0)
                    throw new InvalidDataException("GET_LEDS must return 1 byte, bits 0..5 only.");
                Interlocked.Exchange(ref _ledCapabilityEpoch, CurrentStationEpoch);
                Record("GET_LEDS MCU GPIO 0x" + response.Payload[0].ToString("X2") +
                    "（非高边输出电压或 LED 实际点亮证明）");
                return response.Payload[0];
            }
            catch
            {
                Interlocked.Exchange(ref _ledCapabilityEpoch, 0);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Individual LED GPIO request: 0x1A [index=1..6, level=0/1].
    /// Never writes the 0x1C group mask automatically or controls RL_DRV.</summary>
    public async Task SendManualLedAsync(byte index, bool on, string exactConfirmation,
        CancellationToken ct = default)
    {
        if (index is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(index));
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!HasLedGpioContract || _link is null || _usb is null)
                throw new InvalidOperationException(
                    "LED 控制尚未确认：请先在本次 USB 连接中读取六路 LED GPIO (0x1B)。");
            if (_testRunApproved)
                throw new InvalidOperationException("自动测试正在占用工位，禁止手动 LED 动作。");
            _link.AuthorizeOneLedChange(index, on, exactConfirmation);
            var payload = new byte[] { index, on ? (byte)1 : (byte)0 };
            Record("LED" + index + " 请求" + (on ? "开启" : "关闭") +
                " USB 0x1A " + Convert.ToHexString(payload));
            await _usb.RequestAsync(MainControllerCodec.SetLed, payload,
                TimeSpan.FromSeconds(3), ct);
            Record("LED" + index + " 主控 USB ACK；实际端子电压、负载状态尚未验证。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>0x18 [clamp=1/2, state=0/1]. Other clamp is unchanged.
    /// Transport authorization consumes one exact request before USB writing.</summary>
    public async Task SendManualClampAsync(byte clamp, bool press, string exactConfirmation,
        CancellationToken ct = default)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        await _lifecycle.WaitAsync(ct);
        try
        {
            RequireV02GpioContract();
            if (_testRunApproved || _link is null)
                throw new InvalidOperationException("自动测试占用工位或无实体连接；禁止手动夹具动作。");
            _link.AuthorizeOneClampChange(clamp, press, exactConfirmation);
            var data = new byte[] { clamp, press ? (byte)1 : (byte)0 };
            Record("夹具" + clamp + " 请求" + (press ? "压紧" : "释放") +
                "，原始请求=" + Convert.ToHexString(data));
            await _usb!.RequestAsync(MainControllerCodec.SetClamp, data,
                TimeSpan.FromSeconds(3), ct);
            Record("夹具" + clamp + " 主控 USB ACK；实际夹具到位未测量。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// Arm the exact fixture channels for ONE explicitly approved engineering
    /// station run. This is not manual-bench consent, and is not a TEST/FLASH
    /// grant. It may only be used between the dedicated physical stages.
    /// </summary>
    public async Task AuthorizeStationFixtureRunAsync(string runFingerprint,
        byte allowedOutputMask, bool allowIgn, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            // The 0x18/0x19 clamp protocol is version-gated. An IGN-only
            // engineering station uses the older independent 0x12/0x13 path.
            if (allowedOutputMask != 0) RequireV02GpioContract();
            if (_testRunApproved || _manualCanBatchApproved || _manualKeyActionApproved ||
                _usb?.State != UsbSessionState.Connected || _link is null)
                throw new InvalidOperationException(
                    "夹具运行作用域需要空闲、已验证固件协议的真实主控会话。");
            _link.AuthorizeStationFixtureScope(runFingerprint,
                "AUTHORIZE STATION FIXTURE " + runFingerprint, allowedOutputMask, allowIgn);
            Record("已授权本次工位装夹：" + runFingerprint +
                " · 输出掩码 0x" + allowedOutputMask.ToString("X2") +
                " · 有效期受限；正式机械到位未获确认。");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeStationFixtureRunAsync(string runFingerprint)
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokeStationFixtureScope(runFingerprint);
            Record("本次工位夹具输出授权已撤销；" +
                "不代表气缸已释放或机械位置安全。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// Send one station-owned 0x18 command and require fresh 0x19 GPIO
    /// readback. A GPIO echo is never a pneumatic/mechanical position sensor.
    /// A failed ACK/readback is UNKNOWN; do NOT retry actuation automatically.
    /// </summary>
    public async Task<byte> SendStationFixtureAsync(
        byte clamp, bool press, string runFingerprint,
        CancellationToken ct = default)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        await _lifecycle.WaitAsync(ct);
        try
        {
            RequireV02GpioContract();
            if (_testRunApproved || _usb?.State != UsbSessionState.Connected ||
                _link is null || IsFlashSecuritySensitive)
                throw new InvalidOperationException(
                    "当前 FLASH/TEST 或主控连接状态不允许发送夹具动作。");
            _link.AuthorizeStationFixtureChange(runFingerprint, clamp, press);
            Record("工位夹具" + clamp + " 请求" + (press ? "压紧" : "释放") +
                " · 来源：本次冻结工程工位执行 · 非手动授权");
            await _usb.RequestAsync(MainControllerCodec.SetClamp,
                new byte[] { clamp, press ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), ct);
            var answer = await _usb.RequestAsync(MainControllerCodec.GetClamps,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct);
            if (answer.Payload.Length != 1 || (answer.Payload[0] & 0xFC) != 0)
                throw new InvalidDataException("夹具 GPIO 回读帧不合法，实体状态 UNKNOWN。");
            var levels = answer.Payload[0];
            if (((levels >> (clamp - 1)) & 1) != (press ? 1 : 0))
                throw new IOException("夹具" + clamp + " MCU GPIO 回读与请求不一致，" +
                    "停止后续 FLASH/TEST，不能判定实际机械到位。");
            Record("工位夹具" + clamp + " USB ACK / MCU GPIO 回读一致 0x" +
                levels.ToString("X2") + "；实际气缸/阀位仍未测量。");
            return levels;
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Station-owned IGN write, allowed only between FLASH/TEST
    /// scopes with a one-shot permission derived from the same frozen run.
    /// GPIO readback is never proof of ECU IGN rail voltage.</summary>
    public async Task<bool> SendStationIgnAsync(bool on, string runFingerprint,
        CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_testRunApproved || _usb?.State != UsbSessionState.Connected ||
                _link is null || IsFlashSecuritySensitive)
                throw new InvalidOperationException("当前 FLASH/TEST 或主控会话不允许工位 IGN 动作。");
            _link.AuthorizeStationIgnChange(runFingerprint, on);
            await _usb.RequestAsync(MainControllerCodec.SetIgn,
                new byte[] { on ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), ct);
            var reply = await _usb.RequestAsync(MainControllerCodec.GetIgn,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct);
            if (reply.Payload.Length != 1 || reply.Payload[0] > 1 ||
                (reply.Payload[0] == 1) != on)
                throw new IOException("工位 IGN USB ACK / GPIO 回读不一致；实际电气状态 UNKNOWN。");
            Record("工位 IGN " + (on ? "ON" : "OFF") +
                " USB ACK / MCU GPIO 一致；ECU IGN 端子电压未测量。");
            return reply.Payload[0] == 1;
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>One explicit manual IGN request. Transport consumes the exact
    /// confirmation before the USB write; ACK is not proof of ECU electrical state.</summary>
    public async Task SendManualIgnAsync(bool on, string exactConfirmation,
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null)
                throw new IOException("USB 主控未连接，禁止 IGN 操作。");
            if (_testRunApproved)
                throw new InvalidOperationException("自动 TEST 占用工位，手动 IGN 被锁定。");
            // IGN 0x12 predates the experimental independent-clamp version string.
            // Do not couple established IGN control to the newer clamp payload contract.
            _link.AuthorizeOneIgnChange(on, exactConfirmation);
            Record("IGN 单次授权：" + (on ? "ON" : "OFF"));
            await _usb.RequestAsync(MainControllerCodec.SetIgn,
                new byte[] { on ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), cancellationToken);
            Record("IGN 设备 ACK：" + (on ? "ON" : "OFF") +
                "；实际 ECU 引脚电压及点火状态未测量。");
        }
        finally { _lifecycle.Release(); }
    }

    // Refresh the same operator-approved TEST scope under the device lifecycle
    // lock. Never add CAN IDs or any new output privileges.
    private void RefreshTestRunGrantIfNeeded(string fingerprint)
    {
        if (_link is null || _testRunGrantBudget <= 0) return;
        if (_testRunGrantUsed < _testRunGrantBudget * 3 / 4 &&
            DateTimeOffset.UtcNow < _testRunRefreshAt) return;
        _link.RefreshPhysicalTestRun(fingerprint, _testRunGrantBudget);
        _testRunGrantUsed = 0;
        _testRunRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
        Record("TEST 运行许可已续期；授权 CAN ID 与运行范围未改变。");
    }

    /// <summary>Explicit operator approval for a pinned run; never issued on USB connect.</summary>
    public async Task AuthorizeTestRunAsync(string fingerprint, string confirmation,
        IReadOnlyCollection<(byte Port, bool Extended, uint Id)> allowedCan, bool allowIgn,
        CancellationToken ct = default, int maxWrites = 1000)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_testRunApproved || _manualCanBatchApproved || _manualKeyActionApproved ||
                _usb?.State != UsbSessionState.Connected || _link is null)
                throw new InvalidOperationException("Test run requires an idle connected session.");
            _link.AuthorizePhysicalTestRun(fingerprint, confirmation, allowedCan, allowIgn,
                maxWrites);
            _testRunApproved = true;
            _approvedPlan = fingerprint;
            _testRunGrantBudget = maxWrites;
            _testRunGrantUsed = 0;
            _testRunRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
            PublishTestRunAuthorization(true);
            Record("已人工批准物理测试运行 SHA=" + fingerprint +
                "；最多 " + maxWrites + " 次授权写入 / 10 分钟 / 当前 USB 会话。");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeTestRunAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokePhysicalTestRun();
            _testRunApproved = false;
            _approvedPlan = null;
            _testRunGrantBudget = 0;
            _testRunGrantUsed = 0;
            PublishTestRunAuthorization(false);
            Record("物理测试运行授权已撤销。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Only a run with a previous explicit typed operator authorization can write.</summary>
    public async Task SendTestCanAsync(CanFrame frame, string fingerprint,
        CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!_testRunApproved || _approvedPlan != fingerprint ||
                _usb?.State != UsbSessionState.Connected)
                throw new UnauthorizedAccessException("No live approved test plan.");
            if (frame.Role == BusRole.DutCan) RequireConfiguredEcuCanBitrate();
            var payload = CanFrameCodec.ToUsbPayload(frame,
                StationCanWiring.Current);
            RefreshTestRunGrantIfNeeded(fingerprint);
            if (TryDecodeKeyRequest(frame, out _)) MarkKeyRequestUnknown();
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            ConfirmKeyRequest(frame);
            _testRunGrantUsed++;
            PublishTransmitAck(frame);
            Record("测试 CAN 写入设备 ACK：CAN" + payload[0] + " 0x" +
                frame.Id.ToString("X") + "；尚不能证明 DUT 已响应。");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SendTestIgnAsync(bool on, string fingerprint,
        CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (!_testRunApproved || _approvedPlan != fingerprint ||
                _usb?.State != UsbSessionState.Connected)
                throw new UnauthorizedAccessException("No live approved test plan.");
            // Real TEST IGN uses the established 0x12 path; the clamp-only
            // firmware identity check must not reject it.
            RefreshTestRunGrantIfNeeded(fingerprint);
            await _usb.RequestAsync(MainControllerCodec.SetIgn,
                new byte[] { on ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), ct);
            _testRunGrantUsed++;
            Record("测试 IGN " + (on ? "ON" : "OFF") +
                " 设备 ACK；实际 ECU 电气状态须另行观测。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>R1 Flash uses this exact connected WinUSB session and the existing
    /// CanTool/ISO-TP backend; it never creates a second USB connection.</summary>
    public async Task<FlashExecutionResult> ProgramTestFlashAsync(
        CanToolFlashJob job, string typedConsent, CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        Guid permit = Guid.Empty;
        CanFrameHub? flashHub = null;
        HardwareCanReceiveBridge? flashBridge = null;
        try
        {
            var usb = _usb;
            var link = _link;
            if (usb?.State != UsbSessionState.Connected || link is null ||
                _hub is null || _testRunApproved || _manualCanBatchApproved ||
                _manualKeyActionApproved || CurrentStationEpoch <= 0)
                throw new InvalidOperationException(
                    "FLASH 需要空闲的已连接主控；先撤销 TEST/CAN/KEY 实体授权。");
            RequireConfiguredEcuCanBitrate();
            var jobKbps = MainControllerCodec.ParseEcuCanBaud(job.ConfiguredBitrate);
            if (jobKbps != RequestedEcuCanBitrateKbps)
                throw new InvalidOperationException(
                    "TEST 烧录 XML 要求 ECU CAN " + jobKbps +
                    "k，但本次工位确认速率为 " + ConfirmedEcuCanBitrateKbps +
                    "k。请先结束 TEST，在主界面应用/读回匹配的波特率。");
            if (!string.Equals(typedConsent, job.Consent, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("必须输入准确 ECU FLASH SHA256 授权。");
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(job.FirmwarePath))),
                    job.FirmwareSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("固件内容变化，禁止烧录。");
            var security = CanToolSecurityProvider.Inspect(job.SecurityLibraryPath);
            if (!string.Equals(security.Sha256, job.SecuritySha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(security.Kind.ToString(), job.SecurityProviderKind,
                    StringComparison.Ordinal))
                throw new IOException("安全算法与冻结计划不一致。");
            long frames = (job.PlannedBytes / 6L) * 3L + 5000L + job.Blocks.Length * 50L;
            if (frames is < 1 or > 500000L)
                throw new FormatException("FLASH 帧预算超出安全上限。");
            var gate = new CanTransmitGate(usb.Generation);
            var transmitter = new CanToolCan1Transport(usb, link, gate,
                () => permit, (direction, frame, outcome) =>
                {
                    Record("FLASH CAN " + direction + " 0x" +
                        frame.Id.ToString("X") + " DLC=" + frame.Dlc + " " +
                        (IsFlashSecuritySensitive ? "[SECURITY_REDACTED]" :
                            Convert.ToHexString(frame.Data.AsSpan())) + " " + outcome);
                });
            flashHub = new CanFrameHub(transmitter.TransmitAsync);
            flashBridge = new HardwareCanReceiveBridge(usb, flashHub);
            flashBridge.Trace += trace =>
            {
                if (IsFlashSecuritySensitive) return;
                if (!trace.Valid) Record("FLASH RX 异常：" + trace.Detail);
                else if (trace.Role == BusRole.DutCan)
                    Record("FLASH CAN RX USB0x41 " + trace.RawUsbPayloadHex);
            };
            permit = link.AuthorizeFlashJob(job.Fingerprint, typedConsent,
                job.App.RequestCanId, job.Boot.RequestCanId, checked((int)frames));
            Record("FLASH_PLAN_APPROVED SHA=" + job.Fingerprint +
                " ECU=" + job.TargetLabel + " IMAGE=" + job.FirmwareSha256);
            var workflow = new CanToolFlashWorkflow(flashHub, gate, job,
                line => Record("FLASH_STAGE " + line),
                (ack, verified) => Record("FLASH_PROGRESS ACK=" + ack +
                    " CHECKSUM_OK=" + verified + " TOTAL=" + job.PlannedBytes),
                sensitive =>
                {
                    Interlocked.Exchange(ref _flashSecuritySensitive, sensitive ? 1 : 0);
                    Record(sensitive ? "FLASH_SECURITY_REDACTED_BEGIN" :
                        "FLASH_SECURITY_REDACTED_END");
                });
            var result = await workflow.ExecuteAsync(
                verifyApplicationSession: true, ct: ct);
            Record("FLASH_TERMINAL " + result.Outcome +
                (result.AppSessionObserved
                    ? "；APP 诊断会话已观察到，软件版本仍须通过 TEST 判据核对。"
                    : "；APP 会话未确认，不允许后续 TEST 写入。"));
            return result;
        }
        catch (Exception ex)
        {
            Record("FLASH_UNCERTAIN " + ex.GetType().Name + " " + ex.Message +
                "；ECU 擦写可能已发生，不会自动重试。");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _flashSecuritySensitive, 0);
            if (permit != Guid.Empty) _link?.RevokeFlashJob(permit);
            if (flashBridge is not null) await flashBridge.DisposeAsync();
            flashHub?.Dispose();
            _lifecycle.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Exchange(ref _currentEpoch, 0);
            MarkKeyRequestUnknown();
            Interlocked.Exchange(ref _ledCapabilityEpoch, 0);
            SetObservedEcuCanBaud(0, 0, legacy: false);
            try { ConnectionChanged?.Invoke(0); } catch { }
            _link?.RevokePhysicalTestRun();
            _link?.RevokeManualCanBatch();
            _manualCanBatchApproved = false;
            _manualBatchFingerprint = null;
            _manualCanRows.Clear();
            _link?.RevokeAllManualCanRows();
            AnnounceManualBatch(false);
            _testRunApproved = false;
            _approvedPlan = null;
            _testRunGrantBudget = 0;
            _testRunGrantUsed = 0;
            PublishTestRunAuthorization(false);
            _monitorStop?.Cancel();
            if (_bridge is not null) await _bridge.DisposeAsync();
            _bridge = null;
            foreach (var task in _monitorTasks)
                try { await task; } catch (OperationCanceledException) { }
            _monitorTasks = Array.Empty<Task>();
            _monitorStop?.Dispose();
            _monitorStop = null;
            if (_usb is not null) await _usb.DisposeAsync();
            _usb = null;
            _link = null;
            ConnectedDeviceIdentity = null;
            ConnectedFirmwareVersion = null;
            _hub?.Dispose();
            _hub = null;
            Record("主控已断开；旧连接 CAN 订阅已失效。");
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _lifecycle.Dispose();
    }
}
