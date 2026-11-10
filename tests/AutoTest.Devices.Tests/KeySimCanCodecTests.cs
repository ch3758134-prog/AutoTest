using AutoTest.Contracts;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class KeySimCanCodecTests
{
    [Theory]
    [InlineData(0x00001u, "000030070000080101000000000000")]
    [InlineData(0x08000u, "000030070000080100800000000000")]
    [InlineData(0xFFFFFu, "0000300700000801FFFF0F00000000")]
    public void ExactActiveFirmwareFrameIsStandardCAN0WithZeroTail(uint mask, string raw)
    {
        var frame = KeySimCanCodec.SetMask(mask);
        Assert.Equal(BusRole.KeypadCan, frame.Role);
        Assert.False(frame.ExtendedId);
        Assert.Equal(0x730u, frame.Id);
        Assert.Equal(8, frame.Dlc);
        var encoded = CanFrameCodec.ToUsbPayload(frame,
            new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
        Assert.Equal(raw, Convert.ToHexString(encoded));
        Assert.True(KeySimCanCodec.TryParse(frame, out var reconstructed, out var release));
        Assert.False(release);
        Assert.Equal(mask, reconstructed);
    }

    [Fact]
    public void ExplicitReleaseUsesRealFirmwareOpcodeAndNoSwitchBits()
    {
        var frame = KeySimCanCodec.ReleaseAll();
        Assert.Equal("8000000000000000", Convert.ToHexString(frame.Data.AsSpan()));
        Assert.True(KeySimCanCodec.TryParse(frame, out var mask, out var released));
        Assert.Equal(0u, mask);
        Assert.True(released);
        Assert.Equal(frame, KeySimCanCodec.SetMask(0));
    }

    [Fact]
    public void HighBitsNonzeroTailAndWrongBusAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KeySimCanCodec.SetMask(0x100000));
        Assert.False(KeySimCanCodec.TryParse(
            CanFrame.Create(BusRole.DutCan, false, 0x730,
                new byte[] { 0x01, 1, 0, 0, 0, 0, 0, 0 }),
            out _, out _));
        Assert.False(KeySimCanCodec.TryParse(
            CanFrame.Create(BusRole.KeypadCan, false, 0x730,
                new byte[] { 0x01, 1, 0, 0, 1, 0, 0, 0 }),
            out _, out _));
        Assert.False(KeySimCanCodec.TryParse(
            CanFrame.Create(BusRole.KeypadCan, false, 0x730,
                new byte[] { 0x80, 1, 0, 0, 0, 0, 0, 0 }),
            out _, out _));
    }
    [Fact]
    public async Task WithoutVerifiedPhysicalUsbNoKeySwitchCanBeSent()
    {
        await using var controller = new HardwareBenchController();
        var auth = HardwareBenchController.PhysicalKeySimConfirmation(1, 1);
        var result = await controller.SetPhysicalKeySimMaskAsync(1, auth);
        Assert.Equal(Outcome.Rejected, result.Outcome);
        Assert.Equal("KEYSIM_SESSION_NOT_READY", result.FailureCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task InvalidMaskCannotIssueHardwareAction()
    {
        await using var controller = new HardwareBenchController();
        var result = await controller.SetPhysicalKeySimMaskAsync(0x100000u, "");
        Assert.Equal(Outcome.Rejected, result.Outcome);
        Assert.Equal("KEYSIM_MASK_INVALID", result.FailureCode);
    }

}
