using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>Real transport-level gate validation: no mock USB, CAN device or ECU is used.</summary>
public sealed class ManualCanBatchFenceTests
{
    private static byte[] RawCan(byte channel, uint id, byte first)
    {
        var data = new byte[15];
        data[0] = channel;
        data[2] = (byte)id;
        data[3] = (byte)(id >> 8);
        data[6] = 1;
        data[7] = first;
        return data;
    }

    [Fact]
    public void AllowsOnlyExactCan1FramesInTheApprovedBatchAndConsumesBudget()
    {
        var fence = new PhysicalCommandFence();
        var a = RawCan(1, 0x701, 0x10);
        var b = RawCan(1, 0x702, 0x20);
        var modified = RawCan(1, 0x701, 0x11);
        var wrongPort = RawCan(0, 0x701, 0x10);
        var fingerprint = new string('a', 64);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeManualCanBatch(fingerprint, "wrong", [a, b], 7));
        fence.AuthorizeManualCanBatch(fingerprint,
            "AUTHORIZE BENCH CAN " + fingerprint, [a, b], 7, maxWrites: 3);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, modified), 7));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(2, MainControllerCodec.CanSend, wrongPort), 7));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(3, MainControllerCodec.CanSend, a), 8));

        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(4, MainControllerCodec.CanSend, a), 7);
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(5, MainControllerCodec.CanSend, b), 7);
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(6, MainControllerCodec.CanSend, a), 7);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(7, MainControllerCodec.CanSend, b), 7));
        fence.RevokeManualCanBatch();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(8, MainControllerCodec.CanSend, a), 7));
    }

    [Fact]
    public void ManualBatchAndPhysicalTestAreMutuallyExclusive()
    {
        var fence = new PhysicalCommandFence();
        var payload = RawCan(1, 0x701, 0x01);
        var manualHash = new string('b', 64);
        var testHash = new string('c', 64);
        fence.AuthorizeManualCanBatch(manualHash, "AUTHORIZE BENCH CAN " + manualHash,
            [payload], 3);
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizePhysicalRun(testHash, "AUTHORIZE TEST RUN " + testHash,
                [(1, false, 0x701)], false, 3));
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleCanFrame(payload, "AUTHORIZE CAN TX " +
                Convert.ToHexString(payload), 3));
        // The same manual station may change IGN while CAN1 periodic traffic runs.
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 3);
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(2,
            MainControllerCodec.SetIgn, new byte[] { 1 }), 3);
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(1,
                MainControllerCodec.CanSend, payload), 3));
        fence.AuthorizePhysicalRun(testHash, "AUTHORIZE TEST RUN " + testHash,
            [(1, false, 0x701)], false, 3);
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeManualCanBatch(manualHash, "AUTHORIZE BENCH CAN " + manualHash,
                [payload], 3));
    }

    [Fact]
    public void Can1PeriodicBatchAllowsDocumentedKeySimAndIndependentIoButNotOtherCan()
    {
        var fence = new PhysicalCommandFence();
        var hash = new string('e', 64);
        fence.AuthorizeManualCanBatch(hash, "AUTHORIZE BENCH CAN " + hash,
            [RawCan(1, 0x701, 0x11)], 9);
        var keyFrame = RawCan(0, 0x730, 0x01);
        keyFrame[6] = 8;
        var keyConsent = "AUTHORIZE CAN TX " + Convert.ToHexString(keyFrame);
        fence.AuthorizeSingleCanFrame(keyFrame, keyConsent, 9);
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(1,
            MainControllerCodec.CanSend, keyFrame), 9);
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 9);
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(2,
            MainControllerCodec.SetIgn, new byte[] { 1 }), 9);
        fence.AuthorizeSingleClamp(1, false, "AUTHORIZE CLAMP 1 RELEASE", 9);
        fence.AssertAllowedAndConsume(MainControllerCodec.EncodeClamp(3, 1, false), 9);
        var other = RawCan(0, 0x730, 0x02);
        other[6] = 8;
        Assert.Throws<InvalidOperationException>(() =>
            fence.AuthorizeSingleCanFrame(other,
                "AUTHORIZE CAN TX " + Convert.ToHexString(other), 9));
        fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(4,
            MainControllerCodec.CanSend, RawCan(1, 0x701, 0x11)), 9);
    }

    [Fact]
    public void Can0AndInvalidFramesCannotBeIncludedInManualDutBatch()
    {
        var fence = new PhysicalCommandFence();
        var hash = new string('d', 64);
        Assert.Throws<ArgumentException>(() =>
            fence.AuthorizeManualCanBatch(hash, "AUTHORIZE BENCH CAN " + hash,
                [RawCan(0, 0x730, 0x80)], 1));
        Assert.Throws<ArgumentException>(() =>
            fence.AuthorizeManualCanBatch(hash, "AUTHORIZE BENCH CAN " + hash,
                Array.Empty<byte[]>(), 1));
    }
}
