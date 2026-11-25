using AutoTest.Devices.Usb;
using AutoTest.Contracts;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class PhysicalTestRunFenceTests
{
    [Fact]
    public void ProjectScopeRenewalCannotWidenCanIdentifiersOrSurviveNewUsbEpoch()
    {
        var fence = new PhysicalCommandFence();
        var fingerprint = new string('a', 64);
        const long epoch = 15;
        static byte[] Request(uint id)
        {
            var payload = new byte[15];
            payload[0] = StationCanWiring.DutPort;
            payload[1] = 0; // Classic standard CAN
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(2, 4), id);
            payload[6] = 8;
            return UsbWireCodec.EncodeRequest(3, MainControllerCodec.CanSend, payload);
        }
        fence.AuthorizePhysicalRun(fingerprint,
            "AUTHORIZE TEST RUN " + fingerprint,
            [(StationCanWiring.DutPort, false, 0x160),
             (StationCanWiring.DutPort, false, 0x161)],
            false, epoch, maxWrites: 1);
        fence.AssertAllowedAndConsume(Request(0x160), epoch);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Request(0x161), epoch));
        fence.RefreshPhysicalRun(fingerprint, epoch, maxWrites: 3);
        fence.AssertAllowedAndConsume(Request(0x161), epoch);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Request(0x162), epoch));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Request(0x160), epoch + 1));
        fence.RevokePhysicalRun();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Request(0x161), epoch));
    }

    [Fact]
    public void RunAuthorization_IsExplicitBoundedAndScopedToCanIds()
    {
        var fence = new PhysicalCommandFence();
        var payload = new byte[15];
        payload[0] = StationCanWiring.KeypadPort; payload[6] = 8;
        payload[2] = 0x30; payload[3] = 0x07;
        var request = UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
        var fingerprint = new string('f', 64);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizePhysicalRun(fingerprint, "not approved", [(StationCanWiring.KeypadPort, false, 0x730)], false, 4));
        fence.AuthorizePhysicalRun(fingerprint, "AUTHORIZE TEST RUN " + fingerprint,
            [(StationCanWiring.KeypadPort, false, 0x730)], false, 4, 1);
        fence.AssertAllowedAndConsume(request, 4);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
        fence.RevokePhysicalRun();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
    }

    [Fact]
    public void RunAuthorization_DoesNotSurviveConnectionResetOrAllowOtherCan()
    {
        var fence = new PhysicalCommandFence();
        var fingerprint = new string('e', 64);
        fence.AuthorizePhysicalRun(fingerprint, "AUTHORIZE TEST RUN " + fingerprint,
            [(StationCanWiring.KeypadPort, false, 0x730)], false, 4);
        var payload = new byte[15]; payload[0] = StationCanWiring.DutPort; payload[6] = 8;
        var request = UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
    }
}
