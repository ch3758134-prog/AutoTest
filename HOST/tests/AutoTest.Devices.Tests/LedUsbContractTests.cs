using AutoTest.Contracts;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

// Protocol/authorization assertions only. They do NOT simulate hardware ACK
// or claim the six actual high-side outputs were measured.
public sealed class LedUsbContractTests
{
    [Fact]
    public void SixLedCommandsMatchFirmwareAndRejectOtherOutputs()
    {
        Assert.Equal((byte)0x1A, MainControllerCodec.SetLed);
        Assert.Equal((byte)0x1B, MainControllerCodec.GetLeds);
        Assert.Equal((byte)0x1C, MainControllerCodec.SetLeds);
        var request = MainControllerCodec.EncodeLed(123, 4, true);
        Assert.True(UsbWireCodec.TryDecodeRequest(request, out var decoded));
        Assert.Equal(new byte[] { 4, 1 }, decoded.Payload);
        Assert.Throws<ArgumentOutOfRangeException>(() => MainControllerCodec.EncodeLed(1, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainControllerCodec.EncodeLed(1, 7, false));
    }

    [Fact]
    public void ReadbackIsReadOnlyButExactLedWriteRequiresOneShotConsent()
    {
        var fence = new PhysicalCommandFence();
        var read = UsbWireCodec.EncodeRequest(1, MainControllerCodec.GetLeds, []);
        fence.AssertAllowedAndConsume(read, 3);
        var led4 = MainControllerCodec.EncodeLed(2, 4, true);
        var led5 = MainControllerCodec.EncodeLed(3, 5, true);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(led4, 3));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleLed(4, true, "AUTHORIZE LED 5 ON", 3));
        fence.AuthorizeSingleLed(4, true, "AUTHORIZE LED 4 ON", 3);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(led5, 3));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(led4, 4));
        fence.AssertAllowedAndConsume(led4, 3);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(led4, 3));
    }

    [Fact]
    public void LedIsBlockedDuringTestAndByReconnectAndWholeMaskHasNoImplicitGrant()
    {
        var fence = new PhysicalCommandFence();
        var led = MainControllerCodec.EncodeLed(1, 1, true);
        var mask = UsbWireCodec.EncodeRequest(2, MainControllerCodec.SetLeds, [(byte)0x3F]);
        var sha = new string('b', 64);
        fence.AuthorizePhysicalRun(sha, "AUTHORIZE TEST RUN " + sha,
            Array.Empty<(byte Port, bool Extended, uint Id)>(), false, 5);
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleLed(1, true, "AUTHORIZE LED 1 ON", 5));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(led, 5));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(mask, 5));
        fence.RevokePhysicalRun();
        fence.AuthorizeSingleLed(1, true, "AUTHORIZE LED 1 ON", 5);
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(led, 5));
    }
}
