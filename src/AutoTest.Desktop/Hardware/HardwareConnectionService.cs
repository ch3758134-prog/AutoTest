using System.Collections.Immutable;
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
    public ICanFrameHub? ConnectedCanHub => IsConnected ? _hub : null;
    public event Action<string>? EvidenceRecorded;
    public event Action<CanObservation>? FrameObserved;

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
            pending.TraceRecorded += trace =>
            {
                if (trace.Kind is "CONNECT_ERROR" or "READ_FAULT" or "REQUEST_TIMEOUT" or
                    "REQUEST_ERROR" or "CONNECTED" or "DISCONNECTED")
                    Record("SESSION " + trace.Kind + " G" + trace.Generation +
                        (trace.Detail is null ? "" : " " + trace.Detail));
            };
            await pending.ConnectAsync(cancellationToken);
            // An acknowledged F0 ping confirms the protocol path, not CAN bus/ECU power.
            await pending.RequestAsync(MainControllerCodec.Ping, ReadOnlyMemory<byte>.Empty,
                TimeSpan.FromSeconds(2), cancellationToken);
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

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
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
