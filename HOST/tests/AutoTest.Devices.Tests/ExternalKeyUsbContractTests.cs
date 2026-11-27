using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>Wire-format contract checks only; no simulated button presses or hardware acceptance.</summary>
public sealed class ExternalKeyUsbContractTests
{
    [Fact]
    public void ExternalKeyHasIndependentUsbOpCodes()
    {
        Assert.Equal((byte)0x44, MainControllerCodec.ExternalKeyEvent);
        Assert.Equal((byte)0x45, MainControllerCodec.GetExternalKeyInputs);
        Assert.NotEqual(MainControllerCodec.CanRxIndication, MainControllerCodec.ExternalKeyEvent);
    }

    [Fact]
    public void ExternalKeySnapshotIsReadOnlyButEventCannotBeSentByHost()
    {
        var fence = new PhysicalCommandFence();
        var read = UsbWireCodec.EncodeRequest(1, MainControllerCodec.GetExternalKeyInputs, []);
        fence.AssertAllowedAndConsume(read, 7);
        var malformedRead = UsbWireCodec.EncodeRequest(2, MainControllerCodec.GetExternalKeyInputs, [1]);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(malformedRead, 7));
        var forgedEvent = UsbWireCodec.EncodeRequest(3, MainControllerCodec.ExternalKeyEvent, [1, 1, 1, 1, 0]);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(forgedEvent, 7));
    }

    [Fact]
    public void ValidDecodedExternalKeyEventKeepsHardwareIndexLevelAndSequence()
    {
        var bytes = UsbWireCodec.EncodeResponse(0, MainControllerCodec.ExternalKeyEvent, 0,
            [2, 1, 0b0000_0010, 0x34, 0x12]);
        Assert.True(UsbWireCodec.TryDecodeResponse(bytes, out var response));
        var evt = MainControllerCodec.ParseExternalKeyEvent(response);
        Assert.Equal((byte)2, evt.Key);
        Assert.Equal((byte)1, evt.Level);
        Assert.Equal((byte)2, evt.Levels);
        Assert.Equal((ushort)0x1234, evt.Sequence);
        Assert.Throws<InvalidDataException>(() => MainControllerCodec.ParseExternalKeyEvent(
            new UsbResponse(0, MainControllerCodec.ExternalKeyEvent, 0, [2, 1, 0, 0, 0])));
        Assert.Throws<InvalidDataException>(() => MainControllerCodec.ParseExternalKeyEvent(
            new UsbResponse(0, MainControllerCodec.ExternalKeyEvent, 0, [4, 1, 8, 0, 0])));
        Assert.Throws<InvalidDataException>(() => MainControllerCodec.ParseExternalKeyEvent(
            new UsbResponse(10, MainControllerCodec.ExternalKeyEvent, 0, [2, 1, 2, 0, 0])));
    }

    [Fact]
    public void KeyEventAndCanEventShareFramingButAreSeparateEvents()
    {
        var key = UsbWireCodec.EncodeResponse(0, MainControllerCodec.ExternalKeyEvent, 0, [1, 0, 0, 2, 0]);
        var can = UsbWireCodec.EncodeResponse(0, MainControllerCodec.CanRxIndication, 0, new byte[15]);
        var decoder = new UsbFrameDecoder();
        var decoded = decoder.Feed(key.Concat(can).ToArray());
        Assert.Equal(2, decoded.Count);
        Assert.Equal(MainControllerCodec.ExternalKeyEvent, decoded[0].Command);
        Assert.Equal(MainControllerCodec.CanRxIndication, decoded[1].Command);
    }
}
