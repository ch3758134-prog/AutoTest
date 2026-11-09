using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Can;

public sealed record HardwareCanTrace(DateTimeOffset HostUtc, long HostMonotonicTicks,
    long SessionGeneration, string RawUsbPayloadHex, int PhysicalPort, BusRole? Role,
    bool Valid, string Detail);

/// <summary>Physical CAN0=KeypadCan; CAN1=DutCan per frozen ASW_BusProxy.c/main.c.
/// HostArrivalOnly timestamp; the firmware sends NO controller timestamp in 0x41.
/// Does not transmit CAN or configure controller channels.</summary>
public sealed class HardwareCanReceiveBridge : IAsyncDisposable
{
    private sealed record Indication(UsbResponse Response, DateTimeOffset Utc, long Ticks, long Generation);
    private readonly UsbSessionService _usb;
    private readonly CanFrameHub _hub;
    private readonly BusRoleMap _map;
    private readonly Channel<Indication> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private long _enqueued, _queueDropped, _malformed, _published;
    private readonly object _sync = new();
    private bool _disposed;
    public event Action<HardwareCanTrace>? Trace;
    public long Queued => Interlocked.Read(ref _enqueued);
    public long DroppedBeforeDecode => Interlocked.Read(ref _queueDropped);
    public long InvalidFrames => Interlocked.Read(ref _malformed);
    public long Published => Interlocked.Read(ref _published);

    public HardwareCanReceiveBridge(UsbSessionService usb, CanFrameHub hub, BusRoleMap? map = null, int capacity = 4096)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _usb = usb;
        _hub = hub;
        // Source-proven mapping. Physical connector/harness still requires B0 check.
        _map = map ?? new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
        if (_map.DutPort != 1 || _map.KeypadPort != 0)
            throw new InvalidOperationException("Real firmware mapping is CAN0=keypad, CAN1=DUT; no auto guessing.");
        _queue = Channel.CreateBounded<Indication>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
        _usb.AsynchronousResponse += OnAsyncResponse;
        _usb.StateChanged += OnUsbState;
        _worker = Task.Run(PumpAsync);
    }
    private void OnUsbState(UsbSessionState state)
    {
        if (state is UsbSessionState.Faulted or UsbSessionState.Disconnected)
            _hub.Disconnect(); // old consumers must re-subscribe after reconnect
    }
    private void OnAsyncResponse(UsbResponse response)
    {
        if (response.Command != MainControllerCodec.CanRxIndication) return;
        long generation = _usb.Generation;
        var fact = new Indication(response, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), generation);
        if (_queue.Writer.TryWrite(fact)) Interlocked.Increment(ref _enqueued);
        else Interlocked.Increment(ref _queueDropped);
    }
    private async Task PumpAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                if (item.Generation != _usb.Generation || _usb.State != UsbSessionState.Connected)
                    continue; // never publish an indication from a previous connection
                var resp = item.Response;
                int port = resp.Payload.Length > 0 ? resp.Payload[0] : -1;
                CanFrame? frame = null;
                bool ok = resp.Sequence == 0 && resp.Status == 0 &&
                    CanFrameCodec.FromUsbPayload(resp.Payload, _map, out frame);
                if (!ok) Interlocked.Increment(ref _malformed);
                else
                {
                    var stamp = TimePoint.FromHostArrival(item.Utc, item.Ticks);
                    _hub.Publish(new CanObservation(frame!, stamp, EvidenceQuality.HostObserved,
                        item.Generation, DroppedBeforeDecode, EvidenceRef.New()));
                    Interlocked.Increment(ref _published);
                }
                var line = new HardwareCanTrace(item.Utc, item.Ticks, item.Generation,
                    Convert.ToHexString(resp.Payload), port,
                    ok ? frame!.Role : null, ok,
                    ok ? "USB 0x41: host arrival (not ECU sampling time)" : "Malformed/status-failed CAN indication");
                foreach (Action<HardwareCanTrace> handler in Trace?.GetInvocationList()
                             .Cast<Action<HardwareCanTrace>>() ?? Enumerable.Empty<Action<HardwareCanTrace>>())
                { try { handler(line); } catch { /* user log must not block CAN hub */ } }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        _usb.AsynchronousResponse -= OnAsyncResponse;
        _usb.StateChanged -= OnUsbState;
        _queue.Writer.TryComplete();
        _stop.Cancel();
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _hub.Disconnect();
        _stop.Dispose();
    }
}
