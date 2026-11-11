using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class PhysicalIgnFenceTests
{
    [Fact]
    public void IgntionIsDeniedByDefaultAndGrantIsOneShot()
    {
        var fence = new PhysicalCommandFence();
        var on = UsbWireCodec.EncodeRequest(1, MainControllerCodec.SetIgn, new byte[] { 1 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 7));
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON GEN 7", 7);
        fence.AssertAllowedAndConsume(on, 7);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 7));
    }

    [Fact]
    public void IgnitionOnAuthorizationCannotSwitchIgnitionOff()
    {
        var fence = new PhysicalCommandFence();
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON GEN 8", 8);
        var off = UsbWireCodec.EncodeRequest(2, MainControllerCodec.SetIgn, new byte[] { 0 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(off, 8));
        var on = UsbWireCodec.EncodeRequest(3, MainControllerCodec.SetIgn, new byte[] { 1 });
        fence.AssertAllowedAndConsume(on, 8);
    }

    [Fact]
    public void OldConnectionAndResetCannotReuseAnIgnitionGrant()
    {
        var fence = new PhysicalCommandFence();
        fence.AuthorizeSingleIgn(false, "AUTHORIZE IGN OFF GEN 9", 9);
        var off = UsbWireCodec.EncodeRequest(2, MainControllerCodec.SetIgn, new byte[] { 0 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(off, 10));
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(off, 9));
    }

    [Fact]
    public void InvalidIgnPayloadAndWrongConsentCannotReachUsb()
    {
        var fence = new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN OFF GEN 2", 2));
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON GEN 2", 2);
        var invalid = UsbWireCodec.EncodeRequest(4, MainControllerCodec.SetIgn, new byte[] { 2 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(invalid, 2));
        var twoBytes = UsbWireCodec.EncodeRequest(5, MainControllerCodec.SetIgn, new byte[] { 1, 0 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(twoBytes, 2));
    }

    [Fact]
    public void UsbEncodedCommandStillMatchesConfirmedFirmwareSemantics()
    {
        var on = MainControllerCodec.EncodeIgn(3, true);
        var off = MainControllerCodec.EncodeIgn(4, false);
        Assert.True(UsbWireCodec.TryDecodeRequest(on, out var reqOn));
        Assert.True(UsbWireCodec.TryDecodeRequest(off, out var reqOff));
        Assert.Equal(MainControllerCodec.SetIgn, reqOn.Command);
        Assert.Equal(new byte[]{1}, reqOn.Payload);
        Assert.Equal(new byte[]{0}, reqOff.Payload);
    }
}
