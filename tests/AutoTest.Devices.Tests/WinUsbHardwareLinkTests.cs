using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class WinUsbHardwareLinkTests
{
    [Fact]
    public void HW_USB_01_MainControllerIdentityExcludesHidScanners()
    {
        Assert.Equal(0x1183, MainControllerUsbIdentity.VendorId);
        Assert.Equal(0x5850, MainControllerUsbIdentity.ProductId);
        Assert.Equal((byte)0x82, MainControllerUsbIdentity.BulkIn);
        Assert.Equal((byte)0x03, MainControllerUsbIdentity.BulkOut);
        Assert.Equal("4da6d065-1e3e-47e6-8cc3-c6977351defb", MainControllerUsbIdentity.InterfaceGuid.ToString());
        Assert.True(MainControllerUsbIdentity.IsControllerInstance("USB\\VID_1183&PID_5850\\9&1"));
        Assert.False(MainControllerUsbIdentity.IsControllerInstance("HID\\VID_1183&PID_5850\\A"));
        Assert.False(MainControllerUsbIdentity.IsControllerInstance("USB\\VID_1183&PID_5851\\A"));
        Assert.False(MainControllerUsbIdentity.IsControllerInstance("USB\\VID_1183&PID_58501\\A"));
    }
    [Fact]
    public void HW_USB_02_ExactInstanceSelectionFailsClosedOnMissingOrAmbiguous()
    {
        const string id = "USB\\VID_1183&PID_5850\\INSTANCE_001";
        var first = new WinUsbCandidate("path-a", id);
        Assert.Same(first, WinUsbSelection.Select(new[] { first }, id));
        Assert.Throws<IOException>(() => WinUsbSelection.Select(Array.Empty<WinUsbCandidate>(), id));
        Assert.Throws<IOException>(() => WinUsbSelection.Select(new[] { first, first }, id));
        Assert.Throws<UnauthorizedAccessException>(() => WinUsbSelection.Select(new[] { first }, "USB\\VID_045E&PID_0001\\1"));
    }
    [Fact]
    public void HW_USB_03_DefaultPhysicalFenceRejectsAllWritesExceptReadOnlyCommands()
    {
        var fence = new PhysicalCommandFence();
        foreach (byte cmd in new byte[] { 0xF0, 0xF1, 0x11, 0x13, 0x15, 0x17, 0x1F })
        {
            Assert.True(PhysicalCommandFence.IsReadOnly(cmd, ReadOnlySpan<byte>.Empty));
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(12, cmd, ReadOnlySpan<byte>.Empty), 1);
        }
        foreach (byte cmd in new byte[] { 0x10, 0x12, 0x14, 0x16, 0x40, 0x50, 0x51, 0x52, 0x53, 0xFF })
            Assert.Throws<UnauthorizedAccessException>(() =>
                fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(4, cmd, new byte[] { 1 }), 1));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(5, 0xF0, new byte[] { 0 }), 1));
        var corrupt = UsbWireCodec.EncodeRequest(6, 0xF0, Array.Empty<byte>());
        corrupt[6] ^= 0x80;
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(corrupt, 1));
    }
    [Fact]
    public void HW_USB_04_CanTxConsentExactOneFrameAndNeverSurvivesReconnect()
    {
        var fence = new PhysicalCommandFence();
        byte[] payload = { 1, 0, 0x23, 0x01, 0, 0, 1, 0xAB, 0, 0, 0, 0, 0, 0, 0 };
        byte[] request = UsbWireCodec.EncodeRequest(55, 0x40, payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 9));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleCanFrame(payload, "yes", 9));
        fence.AuthorizeSingleCanFrame(payload, "AUTHORIZE CAN TX " + Convert.ToHexString(payload), 9);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 10));
        fence.AssertAllowedAndConsume(request, 9);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 9));
        fence.AuthorizeSingleCanFrame(payload, "AUTHORIZE CAN TX " + Convert.ToHexString(payload), 9);
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 9));
    }
    [Fact]
    public async Task HW_USB_05_ExistingSessionHasSeqRawFramesTimeoutAndReadOnlyPing()
    {
        await using var link = new ScriptedByteLink();
        await using var session = new UsbSessionService(link);
        var trace = new List<UsbTraceRecord>();
        session.TraceRecorded += e => { lock (trace) trace.Add(e); };
        await session.ConnectAsync();
        Task<UsbResponse> requestTask = session.RequestAsync(0xF0, Array.Empty<byte>(), TimeSpan.FromSeconds(3));
        byte[] rawTx = await link.NextWriteAsync();
        Assert.True(UsbWireCodec.TryDecodeRequest(rawTx, out var request));
        Assert.Equal((byte)0xF0, request.Command);
        Assert.NotEqual((ushort)0, request.Sequence);
        byte[] rawRx = UsbWireCodec.EncodeResponse(request.Sequence, 0xF0, 0, "PONG"u8);
        link.Deliver(rawRx.AsSpan(0, 4).ToArray());
        link.Deliver(rawRx.AsSpan(4).ToArray());
        var response = await requestTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("PONG", Encoding.ASCII.GetString(response.Payload));
        lock (trace)
        {
            Assert.Contains(trace, t => t.Kind == "TX_REQUEST" && t.Sequence == request.Sequence && t.RawHex == Convert.ToHexString(rawTx));
            Assert.Contains(trace, t => t.Kind == "RX_FRAME" && t.Sequence == request.Sequence);
        }
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await session.RequestAsync(0xF1, Array.Empty<byte>(), TimeSpan.FromMilliseconds(100)));
        await session.DisconnectAsync();
    }
    [Fact]
    public void HW_CAN_01_RxAllowsUndefinedTrailingBytesBeyondDlcButKeepsRealPayload()
    {
        var map = new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
        byte[] input = { 0, 0, 0x30, 0x07, 0, 0, 1, 0x42, 0xFF, 1, 2, 3, 4, 5, 6 };
        Assert.True(CanFrameCodec.FromUsbPayload(input, map, out var decoded));
        Assert.Equal(BusRole.KeypadCan, decoded!.Role);
        Assert.Equal(new byte[] { 0x42 }, decoded.Data.ToArray());
        input[0] = 1;
        Assert.True(CanFrameCodec.FromUsbPayload(input, map, out var ecu));
        Assert.Equal(BusRole.DutCan, ecu!.Role);
    }
    [Fact]
    public async Task HW_CAN_02_RealTimeHostStampedCanIndicationFansIntoSharedHubWithoutAnyTx()
    {
        await using var link = new ScriptedByteLink();
        await using var session = new UsbSessionService(link);
        using var hub = new CanFrameHub();
        await using var bridge = new HardwareCanReceiveBridge(session, hub);
        await session.ConnectAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var spec = new CanSubscriptionSpec(BusRole.KeypadCan, ImmutableArray<uint>.Empty,
            CanBackpressurePolicy.DropWithLossFact, 8);
        await using var subscription = hub.SubscribeAsync(spec, stop.Token).GetAsyncEnumerator();
        var next = subscription.MoveNextAsync().AsTask();
        // CAN0=keypad; exact 15-byte firmware indication, no USB command writes.
        byte[] body = { 0, 0, 0x30, 0x07, 0, 0, 1, 0xAA, 0, 0, 0, 0, 0, 0, 0 };
        link.Deliver(UsbWireCodec.EncodeResponse(0, 0x41, 0, body));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(BusRole.KeypadCan, subscription.Current.Frame.Role);
        Assert.Equal((uint)0x730, subscription.Current.Frame.Id);
        Assert.Equal(TimeSource.HostArrival, subscription.Current.Time.Source);
        Assert.Equal(TimeQuality.HostArrivalOnly, subscription.Current.Time.Quality);
        Assert.Equal(EvidenceQuality.HostObserved, subscription.Current.EvidenceQuality);
        Assert.Equal(0, link.WriteCount);
        await session.DisconnectAsync();
    }
    [Fact]
    public async Task HW_CAN_03_SessionDisconnectClosesOldHubSubscriptions()
    {
        await using var link = new ScriptedByteLink();
        await using var session = new UsbSessionService(link);
        using var hub = new CanFrameHub();
        await using var bridge = new HardwareCanReceiveBridge(session, hub);
        await session.ConnectAsync();
        var spec = new CanSubscriptionSpec(BusRole.DutCan, ImmutableArray<uint>.Empty,
            CanBackpressurePolicy.DropWithLossFact, 2);
        await using var sub = hub.SubscribeAsync(spec).GetAsyncEnumerator();
        var next = sub.MoveNextAsync().AsTask();
        await session.DisconnectAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, hub.SubscriberCount);
    }

    private sealed class ScriptedByteLink : IUsbByteLink
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>();
        private bool _opened;
        private int _writeCount;
        public int WriteCount => Volatile.Read(ref _writeCount);
        public ValueTask OpenAsync(CancellationToken ct) { _opened = true; return ValueTask.CompletedTask; }
        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
        {
            byte[] next = await _incoming.Reader.ReadAsync(ct);
            if (next.Length > destination.Length) throw new IOException("Test input too long.");
            next.AsMemory().CopyTo(destination);
            return next.Length;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct)
        {
            if (!_opened) throw new IOException("Closed fake link.");
            Interlocked.Increment(ref _writeCount);
            _outgoing.Writer.TryWrite(source.ToArray());
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken ct) { _opened = false; return ValueTask.CompletedTask; }
        public async Task<byte[]> NextWriteAsync() => await _outgoing.Reader.ReadAsync();
        public void Deliver(byte[] payload) => _incoming.Writer.TryWrite(payload);
        public ValueTask DisposeAsync() { _incoming.Writer.TryComplete(); _outgoing.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
