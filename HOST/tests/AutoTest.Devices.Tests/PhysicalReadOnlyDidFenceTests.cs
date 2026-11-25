using System.Buffers.Binary;
using AutoTest.Contracts;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>
/// Pure physical-fence contract tests. No virtual ECU, hardware substitute,
/// or fake success response is created.
/// </summary>
public sealed class PhysicalReadOnlyDidFenceTests
{
    private const long Generation = 41;
    private const uint TxId = 0x18DA50F1;
    private const uint RxId = 0x18DAF150;
    private const ushort Did = 0xF190;

    private static byte[] Can(byte port, uint id, params byte[] data)
    {
        var payload = new byte[15];
        payload[0] = port;
        payload[1] = id > 0x7FF ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2, 4), id);
        payload[6] = (byte)data.Length;
        data.CopyTo(payload, 7);
        return UsbWireCodec.EncodeRequest(1, MainControllerCodec.CanSend, payload);
    }

    private static PhysicalCommandFence Authorized()
    {
        var fence = new PhysicalCommandFence();
        fence.AuthorizeReadOnlyDid(TxId, RxId, Did,
            PhysicalCommandFence.ReadDidConfirmation(TxId, RxId, Did), Generation);
        return fence;
    }

    [Fact]
    public void DidConsentMustBeExactAndBoundToOneConnection()
    {
        var fence = new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeReadOnlyDid(TxId, RxId, Did,
                "authorize did", Generation));
        Assert.Throws<ArgumentException>(() =>
            fence.AuthorizeReadOnlyDid(TxId, 0x7E8, Did,
                PhysicalCommandFence.ReadDidConfirmation(TxId, 0x7E8, Did), Generation));
        fence = Authorized();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x03, 0x22, 0xF1, 0x90), Generation + 1));
        fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
            0x03, 0x22, 0xF1, 0x90), Generation);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x03, 0x22, 0xF1, 0x90), Generation));
    }

    [Fact]
    public void ReadOnlyDidDoesNotGrantOtherServicesOrArbitraryFlowControl()
    {
        var fence = Authorized();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.KeypadPort, TxId,
                0x03, 0x22, 0xF1, 0x90), Generation));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x02, 0x10, 0x03), Generation));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x03, 0x22, 0xF1, 0x91), Generation));
        fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
            0x03, 0x22, 0xF1, 0x90), Generation);
        // FC is forbidden until a CRC-checked ECU RX FirstFrame with the
        // correct DID has been witnessed inside the real UsbSessionService.
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x30, 0x08, 0x00), Generation));
        fence.RevokeReadOnlyDid();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Can(StationCanWiring.DutPort, TxId,
                0x30, 0x08, 0x00), Generation));
    }
}
