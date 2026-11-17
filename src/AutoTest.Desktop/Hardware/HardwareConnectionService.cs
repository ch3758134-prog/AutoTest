using System.Collections.Immutable;
using System.IO;
using System.Text;
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
    /// <summary>Only source-reported v0.4-dev supports the independent clamp protocol.
    /// This string is not a physical firmware hash or wiring attestation.</summary>
    public bool HasV02GpioContract => IsConnected &&
        string.Equals(ConnectedFirmwareVersion, MainControllerCodec.ClampContractVersion,
            StringComparison.Ordinal);
    public ICanFrameHub? ConnectedCanHub => IsConnected ? _hub : null;
    public event Action<string>? EvidenceRecorded;
    public event Action<CanObservation>? FrameObserved;
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

    private void Record(string text) =>
        EvidenceRecorded?.Invoke(DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "  " + text);

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
                _link?.RevokePhysicalTestRun();
                _link?.RevokeManualCanBatch();
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
            Record("主控连接确认；CAN1=DUT，CAN0=按键板。CAN 接收监视已启动。");
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
                FrameObserved?.Invoke(frame);
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
            if (_testRunApproved || (_manualKeyActionApproved && frame.Role == BusRole.KeypadCan) ||
                (_manualCanBatchApproved && frame.Role != BusRole.KeypadCan))
                throw new InvalidOperationException("CAN1 周期任务运行中仅允许同一手动工位的 KEY_SIM CAN0 指令。");
            var map = new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
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
                throw new ArgumentException("KEY 动作只能向 CAN0 KEY_SIM 发送帧。");
            var map = new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
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
                new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
            await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            Record("KEY_SIM CAN0 主控 ACK：0x" + frame.Id.ToString("X") +
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

    /// <summary>Authorize a bounded snapshot of CAN1 frames, not an open-ended raw CAN route.</summary>
    public async Task AuthorizeManualCanBatchAsync(string fingerprint, string confirmation,
        IReadOnlyList<CanFrame> frames, int maxWrites = 3000, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null ||
                _testRunApproved || _manualCanBatchApproved)
                throw new InvalidOperationException("USB 未连接或工位正在运行其他 CAN/TEST 任务。");
            var map = new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
            var payloads = frames.Select(frame => CanFrameCodec.ToUsbPayload(frame, map)).ToArray();
            _link.AuthorizeManualCanBatch(fingerprint, confirmation, payloads, maxWrites);
            _manualBatchFingerprint = fingerprint;
            _manualCanBatchApproved = true;
            AnnounceManualBatch(true);
            Record("手动 CAN1 任务已授权：报文数=" + frames.Count + " SHA=" + fingerprint +
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
                throw new UnauthorizedAccessException("No active authorized manual CAN1 batch.");
            var payload = CanFrameCodec.ToUsbPayload(frame,
                new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
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
            _manualCanBatchApproved = false;
            _manualBatchFingerprint = null;
            AnnounceManualBatch(false);
            Record("手动 CAN1 任务已停止；不代表 ECU 电气状态发生改变。");
        }
        finally { _lifecycle.Release(); }
    }

    private void RequireV02GpioContract()
    {
        if (!HasV02GpioContract)
            throw new InvalidOperationException("GPIO 操作要求主控 GET_VERSION 为 " +
                MainControllerCodec.ClampContractVersion + "；当前为 " +
                (ConnectedFirmwareVersion ?? "未知") + "。拒绝旧版 GPIO 映射。");
    }

    /// <summary>Read MCU GPIO only; not J1 voltage or ECU ignition state.</summary>
    public async Task<bool> ReadIgnLevelAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            RequireV02GpioContract();
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
            RequireV02GpioContract();
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
        CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_testRunApproved || _manualCanBatchApproved || _manualKeyActionApproved ||
                _usb?.State != UsbSessionState.Connected || _link is null)
                throw new InvalidOperationException("Test run requires an idle connected session.");
            _link.AuthorizePhysicalTestRun(fingerprint, confirmation, allowedCan, allowIgn);
            _testRunApproved = true;
            _approvedPlan = fingerprint;
            PublishTestRunAuthorization(true);
            Record("已人工批准物理测试运行 SHA=" + fingerprint + "，设备会话内有效。");
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
            var payload = CanFrameCodec.ToUsbPayload(frame,
                new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
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
            RequireV02GpioContract();
            await _usb.RequestAsync(MainControllerCodec.SetIgn,
                new byte[] { on ? (byte)1 : (byte)0 },
                TimeSpan.FromSeconds(3), ct);
            Record("测试 IGN " + (on ? "ON" : "OFF") +
                " 设备 ACK；实际 ECU 电气状态须另行观测。");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Exchange(ref _currentEpoch, 0);
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
