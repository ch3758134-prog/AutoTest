using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class PhysicalTestRunFenceTests
{
    [Fact]
    public void RunAuthorization_IsExplicitBoundedAndScopedToCanIds()
    {
        var fence = new PhysicalCommandFence();
        var payload = new byte[15];
        payload[0] = 0; payload[6] = 8;
        payload[2] = 0x30; payload[3] = 0x07;
        var request = UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
        var fingerprint = new string('f', 64);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizePhysicalRun(fingerprint, "not approved", [(0, false, 0x730)], false, 4));
        fence.AuthorizePhysicalRun(fingerprint, "AUTHORIZE TEST RUN " + fingerprint,
            [(0, false, 0x730)], false, 4, 1);
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
            [(0, false, 0x730)], false, 4);
        var payload = new byte[15]; payload[0] = 1; payload[6] = 8;
        var request = UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 4));
    }
}
