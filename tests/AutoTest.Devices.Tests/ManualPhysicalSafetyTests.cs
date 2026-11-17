using AutoTest.Contracts;
using AutoTest.Devices.Station;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>
/// Pure protocol/fence verification; no mock device, no USB transport, no physical output.
/// Production physical writes must still pass the same fence and real WinUSB session.
/// </summary>
public sealed class ManualPhysicalSafetyTests
{
    [Fact]
    public void Ign_RequiresExactStateConsentAndConsumesOnce()
    {
        var fence = new PhysicalCommandFence();
        var on = UsbWireCodec.EncodeRequest(1, MainControllerCodec.SetIgn, new byte[] { 1 });
        var off = UsbWireCodec.EncodeRequest(2, MainControllerCodec.SetIgn, new byte[] { 0 });

        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 7));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN OFF", 7));

        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 7);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(off, 7));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 8));
        fence.AssertAllowedAndConsume(on, 7);

        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 7));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(off, 7));
    }

    [Fact]
    public void Ign_ConsentCannotSurviveConnectionResetOrWriteUnknownCommands()
    {
        var fence = new PhysicalCommandFence();
        var on = UsbWireCodec.EncodeRequest(1, MainControllerCodec.SetIgn, new byte[] { 1 });
        var fixtureUnknown = UsbWireCodec.EncodeRequest(2, 0x22, new byte[] { 1 });
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 3);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(fixtureUnknown, 3));
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 3));
    }

    [Fact]
    public void ClampV02_RequiresExactTwoByteConsentAndConsumesOneOperation()
    {
        var fence = new PhysicalCommandFence();
        var firstPress = MainControllerCodec.EncodeClamp(1, 1, true);
        var secondPress = MainControllerCodec.EncodeClamp(2, 2, true);
        var firstRelease = MainControllerCodec.EncodeClamp(3, 1, false);
        Assert.True(UsbWireCodec.TryDecodeRequest(firstPress, out var decoded));
        Assert.Equal(MainControllerCodec.SetClamp, decoded.Command);
        Assert.Equal(new byte[] { 1, 1 }, decoded.Payload);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(firstPress, 9));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleClamp(1, true, "AUTHORIZE CLAMP 1 RELEASE", 9));
        fence.AuthorizeSingleClamp(1, true, "AUTHORIZE CLAMP 1 PRESS", 9);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(secondPress, 9));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(firstPress, 10));
        fence.AssertAllowedAndConsume(firstPress, 9);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(firstPress, 9));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(firstRelease, 9));
        fence.AuthorizeSingleClamp(2, true, "AUTHORIZE CLAMP 2 PRESS", 9);
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(secondPress, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainControllerCodec.EncodeClamp(4, 3, true));
    }

    [Fact]
    public void ClampV02_OldReadCommandsForbiddenAndTestRunCannotWriteClamps()
    {
        var fence = new PhysicalCommandFence();
        var old = UsbWireCodec.EncodeRequest(1, 0x17, ReadOnlySpan<byte>.Empty);
        var read = UsbWireCodec.EncodeRequest(2, MainControllerCodec.GetClamps, ReadOnlySpan<byte>.Empty);
        var press = MainControllerCodec.EncodeClamp(3, 1, true);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(old, 5));
        fence.AssertAllowedAndConsume(read, 5);
        var sha = new string('b', 64);
        fence.AuthorizePhysicalRun(sha, "AUTHORIZE TEST RUN " + sha,
            Array.Empty<(byte Port, bool Extended, uint Id)>(), false, 5);
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleClamp(1, true, "AUTHORIZE CLAMP 1 PRESS", 5));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(press, 5));
    }

    [Theory]
    [InlineData(0x00001u, 0x01, 0x01, 0x00, 0x00)]
    [InlineData(0x12345u, 0x01, 0x45, 0x23, 0x01)]
    [InlineData(0xFFFFFu, 0x01, 0xFF, 0xFF, 0x0F)]
    public void KeySim20BitMask_UsesFirmwareDocumentedFrame(
        uint mask, byte opcode, byte b1, byte b2, byte b3)
    {
        var frame = KeypadCommandCodec.EncodeMask(mask);
        Assert.Equal(BusRole.KeypadCan, frame.Role);
        Assert.False(frame.ExtendedId);
        Assert.Equal(0x730u, frame.Id);
        Assert.Equal(8, frame.Dlc);
        Assert.Equal(new byte[] { opcode, b1, b2, b3, 0, 0, 0, 0 }, frame.Data.ToArray());
    }

    [Fact]
    public void KeySimReleaseAll_RequiresDistinctFirmwareReleaseOpcode()
    {
        var frame = KeypadCommandCodec.EncodeReleaseAll();
        Assert.Equal(0x730u, frame.Id);
        Assert.Equal(new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0 }, frame.Data.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => KeypadCommandCodec.EncodeMask(0x100000u));
    }
    [Fact]
    public void AuthorizedTestRun_DisablesManualCanAndIgnAndRevokesPendingManualTokens()
    {
        var fence = new PhysicalCommandFence();
        var payload = new byte[15];
        payload[0] = 1;
        payload[2] = 0x23;
        payload[3] = 0x01;
        var request = UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
        var on = UsbWireCodec.EncodeRequest(2, MainControllerCodec.SetIgn, new byte[] { 1 });
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 7);
        fence.AuthorizeSingleCanFrame(payload, "AUTHORIZE CAN TX " + Convert.ToHexString(payload), 7);
        var fingerprint = new string('a', 64);
        fence.AuthorizePhysicalRun(fingerprint, "AUTHORIZE TEST RUN " + fingerprint,
            Array.Empty<(byte Port, bool Extended, uint Id)>(), false, 7);
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 7));
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleCanFrame(payload,
                "AUTHORIZE CAN TX " + Convert.ToHexString(payload), 7));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 7));
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(on, 7));
        fence.RevokePhysicalRun();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 7));
    }

}
