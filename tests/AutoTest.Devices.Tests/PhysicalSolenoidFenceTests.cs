using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class PhysicalSolenoidFenceTests
{
    private static byte[] Set(byte action, ushort seq = 1) =>
        UsbWireCodec.EncodeRequest(seq, FirmwareReadOnlyProtocol.SetSolenoid,
            new byte[] { action });

    [Fact]
    public void SetSolenoidDefaultDenyThenOneShot()
    {
        var fence = new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(0), 11));
        fence.AuthorizeSingleSolenoid(0, "AUTHORIZE SOL 0 GEN 11", 11);
        fence.AssertAllowedAndConsume(Set(0), 11);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(0), 11));
    }

    [Fact]
    public void RaiseConsentNeverAuthorizesLowerCommandOrReconnect()
    {
        var fence = new PhysicalCommandFence();
        fence.AuthorizeSingleSolenoid(0, "AUTHORIZE SOL 0 GEN 4", 4);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(1), 4));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(0), 5));
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(0), 4));
    }

    [Fact]
    public void MalformedSolenoidActionsAndWrongOperatorApprovalFailClosed()
    {
        var fence = new PhysicalCommandFence();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fence.AuthorizeSingleSolenoid(2, "AUTHORIZE SOL 2 GEN 8", 8));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleSolenoid(0, "AUTHORIZE SOL 1 GEN 8", 8));
        fence.AuthorizeSingleSolenoid(1, "AUTHORIZE SOL 1 GEN 8", 8);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(Set(2), 8));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(
                UsbWireCodec.EncodeRequest(2, FirmwareReadOnlyProtocol.SetSolenoid,
                    new byte[] { 1, 0 }), 8));
        fence.AssertAllowedAndConsume(Set(1), 8);
    }

    [Fact]
    public void SolenoidReadbackIsOnlyReadOnlyFirmwareCode()
    {
        var fence = new PhysicalCommandFence();
        var query = UsbWireCodec.EncodeRequest(6, FirmwareReadOnlyProtocol.GetSolenoid,
            ReadOnlySpan<byte>.Empty);
        fence.AssertAllowedAndConsume(query, 3); // READ has no actuation grant
        Assert.True(FirmwareReadOnlyProtocol.ValidReadOnlyPayload(
            FirmwareReadOnlyProtocol.GetSolenoid, new byte[] { 1 }));
        Assert.False(FirmwareReadOnlyProtocol.ValidReadOnlyPayload(
            FirmwareReadOnlyProtocol.GetSolenoid, new byte[] { 2 }));
    }
}
