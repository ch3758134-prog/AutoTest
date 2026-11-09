using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class ReadOnlyUdsPhysicalTxTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static ReadOnlyUdsPhysicalEndpoint Endpoint =>
        new("ECU_PROFILE_REV_7", 0x7E0, 0x7E8, false);
    private static LeaseToken Lease(long generation) =>
        new(Guid.NewGuid(), "UDS_READONLY_TEST", generation, Today.AddMinutes(3));
    private static CanFrame Tx(params byte[] data) =>
        CanFrame.Create(BusRole.DutCan, false, 0x7E0, data);
    private static CanObservation Rx(long generation, byte[] data, uint id = 0x7E8, long loss = 0) =>
        new(CanFrame.Create(BusRole.DutCan, false, id, data),
            TimePoint.FromHostArrival(Today, 1234), EvidenceQuality.HostObserved,
            generation, loss, EvidenceRef.New());

    [Fact]
    public void OnlyReadOnlyUdsSidsAndValidDutEndpointAreAuthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(new byte[] { 0x2E, 0xF1, 0x90, 0x00 }));
        foreach (byte sid in new byte[] { 0x10, 0x11, 0x27, 0x2E, 0x31, 0x34, 0x36, 0x37 })
            Assert.Throws<UnauthorizedAccessException>(() =>
                ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(new byte[] { sid, 0x01, 0x02 }));
        ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(new byte[] { 0x19, 0x02, 0xFF });
        ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(new byte[] { 0x22, 0xF1, 0x90 });
        Assert.Throws<ArgumentException>(() => new ReadOnlyUdsPhysicalEndpoint("rev", 0x7E0, 0x7E0, false).Validate());
        Assert.Throws<ArgumentException>(() => new ReadOnlyUdsPhysicalEndpoint("", 0x7E0, 0x7E8, false).Validate());
        Assert.Throws<ArgumentException>(() =>
            ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(new byte[] { 0x22, 0xF1 }));
    }

    [Fact]
    public void SingleFrameRequiresExactPduLeaseAndGenerationThenAllowsMatchedResponseFc()
    {
        var gate = new ReadOnlyUdsTransmitBoundary(() => Today);
        var lease = Lease(4);
        var pdu = new byte[] { 0x22, 0xF1, 0x90 };
        var requestId = Guid.NewGuid();
        using var permit = gate.Begin(Endpoint, pdu, lease, requestId, 4, TimeSpan.FromSeconds(30), 32);

        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x03, 0x2E, 0xF1, 0x90), lease, 4, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(
            CanFrame.Create(BusRole.KeypadCan, false, 0x7E0, new byte[] { 3, 0x22, 0xF1, 0x90 }),
            lease, 4, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(Tx(3, 0x22, 0xF1, 0x90), lease, 5, out _, out _));
        Assert.True(gate.TryConsumeOutgoing(Tx(3, 0x22, 0xF1, 0x90), lease, 4,
            out var boundRequestId, out _));
        Assert.Equal(requestId, boundRequestId);
        Assert.False(gate.TryConsumeOutgoing(Tx(3, 0x22, 0xF1, 0x90), lease, 4, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 4));

        gate.ObserveIncoming(Rx(4, new byte[] { 0x10, 0x11, 0x62, 0xF1, 0x90, 1, 2, 3 }, id: 0x7E9));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
        gate.ObserveIncoming(Rx(4, new byte[] { 0x10, 0x11, 0x62, 0xF1, 0x90, 1, 2, 3 }));
        Assert.True(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
        gate.ObserveIncoming(Rx(4, new byte[] { 0x21, 4, 5, 6, 7, 8, 9, 10 }));
        gate.ObserveIncoming(Rx(4, new byte[] { 0x22, 11, 12, 13, 14 }));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 4, out _, out _));
    }

    [Fact]
    public void MultiFrameRequestNeedsMatchingEcuFlowControlAndCorrectConsecutiveBytes()
    {
        var gate = new ReadOnlyUdsTransmitBoundary(() => Today);
        var lease = Lease(6);
        var pdu = new byte[] { 0x22, 0xF1, 0x90, 0xF1, 0x91, 0xF1, 0x92, 0xF1, 0x93 };
        var requestId = Guid.NewGuid();
        using var scope = gate.Begin(Endpoint, pdu, lease, requestId, 6, TimeSpan.FromSeconds(30), 20);
        Assert.True(gate.TryConsumeOutgoing(Tx(0x10, 9, 0x22, 0xF1, 0x90, 0xF1, 0x91, 0xF1),
            lease, 6, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x21, 0x92, 0xF1, 0x93), lease, 6, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 6));
        gate.ObserveIncoming(Rx(6, new byte[] { 0x31, 0, 0 })); // WAIT does not authorize CF
        Assert.False(gate.TryConsumeOutgoing(Tx(0x21, 0x92, 0xF1, 0x93), lease, 6, out _, out _));
        gate.ObserveIncoming(Rx(6, new byte[] { 0x30, 0, 0 }));
        Assert.True(gate.TryConsumeOutgoing(Tx(0x21, 0x92, 0xF1, 0x93), lease, 6, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 6));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x21, 0x92, 0xF1, 0x93), lease, 6, out _, out _));
    }

    [Fact]
    public void Extended29BitEndpointAndFiniteFrameBudgetAreBoundToSameTransaction()
    {
        var gate = new ReadOnlyUdsTransmitBoundary(() => Today);
        var endpoint = new ReadOnlyUdsPhysicalEndpoint("REV_29BIT", 0x18DA10F1u, 0x18DAF110u, true);
        var lease = Lease(15);
        var requestId = Guid.NewGuid();
        using var scope = gate.Begin(endpoint, new byte[] { 0x19, 0x02, 0xFF }, lease,
            requestId, 15, TimeSpan.FromSeconds(20), 2);
        var wrongRole = CanFrame.Create(BusRole.KeypadCan, true, 0x18DA10F1u,
            new byte[] { 3, 0x19, 0x02, 0xFF });
        var correct = CanFrame.Create(BusRole.DutCan, true, 0x18DA10F1u,
            new byte[] { 3, 0x19, 0x02, 0xFF });
        Assert.False(gate.TryConsumeOutgoing(wrongRole, lease, 15, out _, out _));
        Assert.True(gate.TryConsumeOutgoing(correct, lease, 15, out _, out _));
        Assert.False(gate.TryConsumeOutgoing(correct, lease, 15, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 15));
        Assert.False(gate.TryConsumeOutgoing(correct, lease, 15, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            new ReadOnlyUdsPhysicalEndpoint("r", 0x7E0, 0x7E8, true).Validate());
    }

    [Fact]
    public void MultiFrameResponseRequiresNewFlowControlOnlyAfterRequestedBlock()
    {
        var gate = new ReadOnlyUdsTransmitBoundary(() => Today);
        var lease = Lease(17);
        var requestId = Guid.NewGuid();
        using var scope = gate.Begin(Endpoint, new byte[] { 0x22, 0xF1, 0x90 },
            lease, requestId, 17, TimeSpan.FromSeconds(20), 16);
        Assert.True(gate.TryConsumeOutgoing(Tx(3, 0x22, 0xF1, 0x90), lease, 17, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 17));
        gate.ObserveIncoming(Rx(17, new byte[] { 0x10, 0x50, 0x62, 0xF1, 0x90, 1, 2, 3 }));
        Assert.True(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 17, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 17));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 17, out _, out _));
        for (byte seq = 1; seq <= 8; seq++)
        {
            var data = new byte[8];
            data[0] = (byte)(0x20 | seq);
            gate.ObserveIncoming(Rx(17, data));
        }
        Assert.True(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 17, out _, out _));
        Assert.True(gate.ConfirmControllerAck(requestId, 17));
        Assert.False(gate.TryConsumeOutgoing(Tx(0x30, 8, 0), lease, 17, out _, out _));
    }

    [Fact]
    public void PermitRevokesOnScopeEndTimeoutLossAndOldGeneration()
    {
        var now = Today;
        var gate = new ReadOnlyUdsTransmitBoundary(() => now);
        var lease = Lease(9);
        var single = Tx(3, 0x22, 0xF1, 0x90);
        using (gate.Begin(Endpoint, new byte[] { 0x22, 0xF1, 0x90 }, lease, Guid.NewGuid(),
            9, TimeSpan.FromSeconds(10), 3))
        {
            gate.ObserveIncoming(Rx(9, new byte[] { 0x10, 0x12, 0x62, 0xF1, 0x90, 1, 2, 3 }, loss: 1));
            Assert.False(gate.TryConsumeOutgoing(single, lease, 9, out _, out _));
        }

        using (gate.Begin(Endpoint, new byte[] { 0x22, 0xF1, 0x90 }, lease, Guid.NewGuid(),
            9, TimeSpan.FromSeconds(10), 3))
        {
            now = Today.AddSeconds(11);
            Assert.False(gate.TryConsumeOutgoing(single, lease, 9, out _, out _));
        }
        Assert.False(gate.TryConsumeOutgoing(single, lease, 9, out _, out _));
    }

    [Fact]
    public async Task FakeWinUsbReceivesOnlyExactlyAuthorizedCan1FrameAndRevokesAfterWrite()
    {
        var native = new FakeNative();
        await using var link = new WinUsbByteLink(
            "USB\\VID_1183&PID_5850\\MOCK", new Discovery(), native);
        await link.OpenAsync(default);
        var gate = new ReadOnlyUdsTransmitBoundary(() => Today);
        var lease = Lease(3);
        var requestId = Guid.NewGuid();
        using var permit = gate.Begin(Endpoint, new byte[] { 0x22, 0xF1, 0x90 },
            lease, requestId, 3, TimeSpan.FromSeconds(20), 10);
        byte[] usbBody = CanFrameCodec.ToUsbPayload(Tx(3, 0x22, 0xF1, 0x90),
            new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
        var raw = UsbWireCodec.EncodeRequest(7, 0x40, usbBody);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(raw, default));
        Assert.Equal(0, native.Writes);
        Assert.True(gate.TryConsumeOutgoing(Tx(3, 0x22, 0xF1, 0x90), lease, 3, out _, out _));
        link.AuthorizeOneCanTransmission(usbBody, "AUTHORIZE CAN TX " + Convert.ToHexString(usbBody));
        await link.WriteAsync(raw, default);
        Assert.True(gate.ConfirmControllerAck(requestId, 3));
        link.RevokeOneCanTransmission();
        Assert.Equal(1, native.Writes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(raw, default));
        await link.CloseAsync(default);
        await link.OpenAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(raw, default));
        Assert.Equal(1, native.Writes);
        await link.CloseAsync(default);
    }

    private sealed class Discovery : IWinUsbDiscovery
    {
        public IReadOnlyList<WinUsbCandidate> ListCandidates() =>
            new[] { new WinUsbCandidate("mock-path", "USB\\VID_1183&PID_5850\\MOCK", "WinUSB") };
    }
    private sealed class FakeNative : IWinUsbTransportNative
    {
        public int Writes;
        public bool IsSupported => true;
        public int LastError => 0;
        public SafeFileHandle Open(string path, uint flags) => new(new IntPtr(1234), false);
        public bool Initialize(SafeFileHandle file, out IntPtr handle)
        { handle = new IntPtr(4321); return true; }
        public bool QueryInterfaceSettings(IntPtr handle, out WinUsbNative.USB_INTERFACE_DESCRIPTOR d)
        {
            d = new WinUsbNative.USB_INTERFACE_DESCRIPTOR
            { bInterfaceNumber = 0, bAlternateSetting = 0, bInterfaceClass = 0xFF, bNumEndpoints = 2 };
            return true;
        }
        public bool QueryPipe(IntPtr handle, byte index, out WinUsbNative.WINUSB_PIPE_INFORMATION info)
        {
            info = new WinUsbNative.WINUSB_PIPE_INFORMATION
            { PipeType = WinUsbNative.PipeTypeBulk,
                PipeId = index == 0 ? (byte)0x82 : (byte)0x03, MaximumPacketSize = 64 };
            return true;
        }
        public bool SetPipeTimeout(IntPtr handle, byte endpoint, ref uint timeout) => true;
        public bool Read(IntPtr handle, byte[] buffer, out uint transferred)
        { transferred = 0; return false; }
        public bool Write(IntPtr handle, byte[] bytes, out uint transferred)
        { Interlocked.Increment(ref Writes); transferred = (uint)bytes.Length; return true; }
        public bool Cancel(SafeFileHandle handle) => true;
        public void Free(IntPtr handle) { }
    }
}
