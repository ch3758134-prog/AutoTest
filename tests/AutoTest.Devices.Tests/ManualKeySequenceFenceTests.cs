using AutoTest.Contracts;
using AutoTest.Devices.Can;
using AutoTest.Devices.Station;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>Tests ONLY the production authorization and codec, without a mock device.</summary>
public sealed class ManualKeySequenceFenceTests
{
    private static byte[] ToPayload(uint mask) => CanFrameCodec.ToUsbPayload(
        mask == 0 ? KeypadCommandCodec.EncodeReleaseAll() : KeypadCommandCodec.EncodeMask(mask),
        new BusRoleMap(default, DutPort: 1, KeypadPort: 0));

    [Fact]
    public void OnlyAuthorizedKeySequenceMayPassInOrderOnce()
    {
        var f = new PhysicalCommandFence();
        var frames = new[] { ToPayload(0xA), ToPayload(0xA), ToPayload(0x40), ToPayload(0) };
        var sha = new string('a', 64);
        Assert.Throws<UnauthorizedAccessException>(() =>
            f.AuthorizeManualKeySequence(sha, "bad", frames, 15));
        f.AuthorizeManualKeySequence(sha, "AUTHORIZE KEY ACTION " + sha, frames, 15);

        Assert.Throws<UnauthorizedAccessException>(() => f.AssertAllowedAndConsume(
            UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, frames[2]), 15));
        Assert.Throws<UnauthorizedAccessException>(() => f.AssertAllowedAndConsume(
            UsbWireCodec.EncodeRequest(2, MainControllerCodec.CanSend, frames[0]), 16));
        for (var i = 0; i < frames.Length; i++)
            f.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest((ushort)(i + 3),
                MainControllerCodec.CanSend, frames[i]), 15);
        Assert.Throws<UnauthorizedAccessException>(() => f.AssertAllowedAndConsume(
            UsbWireCodec.EncodeRequest(8, MainControllerCodec.CanSend, frames[3]), 15));
    }

    [Fact]
    public void OtherKeyCommandsAreBlockedButCan1PeriodicMayCoexist()
    {
        var f = new PhysicalCommandFence();
        var one = ToPayload(0x3);
        var sha = new string('b', 64);
        f.AuthorizeManualKeySequence(sha, "AUTHORIZE KEY ACTION " + sha, [one], 12);
        Assert.Throws<InvalidOperationException>(() => f.AuthorizeSingleCanFrame(
            one, "AUTHORIZE CAN TX " + Convert.ToHexString(one), 12));

        var txCan1 = new byte[15];
        txCan1[0] = 1; txCan1[2] = 0x01; txCan1[3] = 0x07; txCan1[6] = 1;
        f.AuthorizeManualCanBatch(sha, "AUTHORIZE BENCH CAN " + sha, [txCan1], 12);
        f.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(1,
            MainControllerCodec.CanSend, txCan1), 12);
        f.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(2,
            MainControllerCodec.CanSend, one), 12);
        f.RevokeManualKeySequence();
        Assert.Throws<UnauthorizedAccessException>(() => f.AssertAllowedAndConsume(
            UsbWireCodec.EncodeRequest(3, MainControllerCodec.CanSend, one), 12));
        f.RevokeManualCanBatch();
    }

    [Fact]
    public void TestRunCannotStartWhileKeySequenceIsAuthorized()
    {
        var f = new PhysicalCommandFence();
        var sha = new string('c', 64);
        f.AuthorizeManualKeySequence(sha, "AUTHORIZE KEY ACTION " + sha, [ToPayload(1)], 10);
        Assert.Throws<InvalidOperationException>(() => f.AuthorizePhysicalRun(
            sha, "AUTHORIZE TEST RUN " + sha, Array.Empty<(byte, bool, uint)>(), false, 10));
        f.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => f.AssertAllowedAndConsume(
            UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, ToPayload(1)), 10));
    }

    [Fact]
    public void WrongPortAndUnexpectedMaskAreRejectedAtAuthorization()
    {
        var f = new PhysicalCommandFence();
        var hash = new string('d', 64);
        var bad = ToPayload(1);
        bad[0] = 1;
        Assert.Throws<ArgumentException>(() => f.AuthorizeManualKeySequence(
            hash, "AUTHORIZE KEY ACTION " + hash, [bad], 7));
        var invalid = ToPayload(1);
        invalid[10] = 0x80;
        Assert.Throws<ArgumentException>(() => f.AuthorizeManualKeySequence(
            hash, "AUTHORIZE KEY ACTION " + hash, [invalid], 7));
    }
}
