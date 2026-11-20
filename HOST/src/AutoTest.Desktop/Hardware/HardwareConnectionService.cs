using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using AutoTest.FlashLab.Hardware;
using AutoTest.FlashLab.Programming;
using AutoTest.Contracts;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Desktop.Hardware;

/// <summary>
/// One real WinUSB session and one CAN hub for the whole station.
/// Only read-only USB ping and CAN receiving are activated on connect.
/// No mock fallback, automatic CAN transmit, or physical actuation.
/// </summary>
public sealed class HardwareConnectionService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private int _flashSecuritySensitive;
    public bool IsFlashSecuritySensitive => Volatile.Read(ref _flashSecuritySensitive) != 0;

    // Current firmware always initializes ECU_CAN_PORT to 500 kbit/s and
    // exposes NO USB set/get bitrate command. This is a requested configuration
    // only: unsupported choices must NEVER be treated as applied to the bus.
    public sealed record EcuCanRateChoice(int Kbps, string Label);
    public static IReadOnlyList<EcuCanRateChoice> EcuCanRateChoices { get; } =
    [
        new(20, "20 kbit/s"), new(50, "50 kbit/s"),
        new(100, "100 kbit/s"), new(125, "125 kbit/s"),
        new(250, "250 kbit/s"), new(500, "500 kbit/s · 当前固件"),
        new(800, "800 kbit/s"), new(1000, "1 Mbit/s")
    ];
    private int _requestedEcuCanBitrateKbps = 500;
    public event Action<int>? EcuCanBitrateSelectionChanged;
    public int RequestedEcuCanBitrateKbps => Volatile.Read(ref _requestedEcuCanBitrateKbps);
    public const int FirmwareFixedEcuCanBitrateKbps = 500;

    public void SelectRequestedEcuCanBitrate(int kbps)
    {
        if (kbps is not (20 or 50 or 100 or 125 or 250 or 500 or 800 or 1000))
            throw new ArgumentOutOfRangeException(nameof(kbps), "不是主控 CAN 驱动已定义的码率。");
        if (_testRunApproved || _manualCanBatchApproved ||
            _manualKeyActionApproved || IsFlashSecuritySensitive ||
            _lifecycle.CurrentCount == 0)
            throw new InvalidOperationException("工位正在执行硬件操作，禁止改变 ECU CAN 波特率设置。");
        Volatile.Write(ref _requestedEcuCanBitrateKbps, kbps);
        Record("ECU CAN 期望码率选择 " + kbps +
            " kbit/s；当前固件 USB 不支持设置/读取，未执行硬件修改。");
        try { EcuCanBitrateSelectionChanged?.Invoke(kbps); }
        catch { /* UI observation cannot change the physical rate contract. */ }
    }

    public void RequireConfiguredEcuCanBitrate()
    {
        if (RequestedEcuCanBitrateKbps != FirmwareFixedEcuCanBitrateKbps)
            throw new NotSupportedException("已选择 ECU CAN " +
                RequestedEcuCanBitrateKbps +
                " kbit/s，但主控固件当前仅固定 500 kbit/s，且没有 USB 改速指令。" +
                "目标波特率未生效，已禁止 ECU CAN 实际发送/测试/烧录。");
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
    private volatile bool _manualCanBatchApproved;
    private string? _manualBatchFingerprint;
    private volatile bool _manualKeyActionApproved;
    private string? _manualKeyActionFingerprint;
    public bool IsManualKeyActionApproved => _manualKeyActionApproved;
    public bool IsManualCanBatchApproved => _manualCanBatchApproved;
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
                Interlocked.Exchange(ref _ledCapabilityEpoch, 0);
                _link?.RevokePhysicalTestRun();
                _link?.RevokeManualCanBatch();
                _link?.RevokeManualKeySequence();
                _manualKeyActionApproved = false;
                _manualKeyActionFingerprint = null;
                _manualCanBatchApproved = false;
                _manualBatchFingerprint = null;
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
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), cancellationToken);
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
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
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
            Record("KEY 序列授权撤销；没有自动释放 KEY，实体状态 UNKNOWN。");
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Authorize a bounded snapshot of ECU CAN frames, not an open-ended raw CAN route.</summary>
    public async Task AuthorizeManualCanBatchAsync(string fingerprint, string confirmation,
        IReadOnlyList<CanFrame> frames, int maxWrites = 3000, CancellationToken ct = default)
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
                "，最多 " + maxWrites + " 次写入 / 5 分钟 / 当前 USB 会话。");
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
        }
        finally { _lifecycle.Release(); }
    }

    public async Task RevokeManualCanBatchAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _link?.RevokeManualCanBatch();
            _link?.RevokeManualKeySequence();
            _manualKeyActionApproved = false;
            _manualKeyActionFingerprint = null;
            _manualCanBatchApproved = false;
            _manualBatchFingerprint = null;
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
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
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
            await _usb.RequestAsync(MainControllerCodec.SetIgn,
                new byte[] { on ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), ct);
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
            if (job.ConfiguredBitrate is not ("500K" or "500000"))
                throw new NotSupportedException("当前主控只确认 500K，拒绝自动切换波特率。");
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
            Interlocked.Exchange(ref _ledCapabilityEpoch, 0);
            try { ConnectionChanged?.Invoke(0); } catch { }
            _link?.RevokePhysicalTestRun();
            _link?.RevokeManualCanBatch();
            _manualCanBatchApproved = false;
            _manualBatchFingerprint = null;
            AnnounceManualBatch(false);
            _testRunApproved = false;
            _approvedPlan = null;
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
