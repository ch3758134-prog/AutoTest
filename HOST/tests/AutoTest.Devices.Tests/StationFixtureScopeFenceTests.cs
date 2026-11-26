using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>
/// These tests exercise the real byte-level default-deny fence, NOT mock
/// hardware actuation or a claim of physical clamp position.
/// </summary>
public sealed class StationFixtureScopeFenceTests
{
    private const long Epoch = 14;
    private static readonly string Sha = new('A', 64);
    private static byte[] Clamp(byte clamp, bool pressed, ushort seq = 1) =>
        UsbWireCodec.EncodeRequest(seq, MainControllerCodec.SetClamp,
            [clamp, pressed ? (byte)1 : (byte)0]);
    private static byte[] Ign(bool on, ushort seq = 1) =>
        UsbWireCodec.EncodeRequest(seq, MainControllerCodec.SetIgn,
            [on ? (byte)1 : (byte)0]);
    private static PhysicalCommandFence Granted(byte mask = 1, bool ign = false)
    {
        var guard = new PhysicalCommandFence();
        guard.AuthorizeStationFixtureScope(Sha, "AUTHORIZE STATION FIXTURE " + Sha,
            mask, ign, Epoch);
        return guard;
    }

    [Fact]
    public void UnauthorisedCommandAndUnselectedChannelAreDenied()
    {
        var guard = new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, true), Epoch));
        guard = Granted(1);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AuthorizeStationFixtureChange(Sha, 2, true, Epoch));
        guard.AuthorizeStationFixtureChange(Sha, 1, true, Epoch);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, false), Epoch));
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, true), Epoch + 1));
        guard.AssertAllowedAndConsume(Clamp(1, true), Epoch);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, true), Epoch));
    }

    [Fact]
    public void FixturePermitSurvivesFlashAndTestButCannotActuateDuringThem()
    {
        var guard = Granted(1, ign: true);
        guard.AuthorizeStationFixtureChange(Sha, 1, true, Epoch);
        var flash = guard.AuthorizeProgrammingJob(Sha,
            PhysicalCommandFence.FlashConfirmation(Sha),
            0x747, 0x18DA50F1, Epoch, 4);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, true), Epoch));
        guard.RevokeProgrammingJob(flash);
        // Pending fixture commands are invalidated by the FLASH stage.
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Clamp(1, true), Epoch));
        guard.AuthorizeStationIgnChange(Sha, true, Epoch);
        guard.AssertAllowedAndConsume(Ign(true), Epoch);
        guard.AuthorizePhysicalRun(Sha, "AUTHORIZE TEST RUN " + Sha,
            [], allowIgn: false, Epoch);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AssertAllowedAndConsume(Ign(true), Epoch));
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AuthorizeStationFixtureChange(Sha, 1, false, Epoch));
        guard.RevokePhysicalRun();
        guard.AuthorizeStationFixtureChange(Sha, 1, false, Epoch);
        guard.AssertAllowedAndConsume(Clamp(1, false), Epoch);
        guard.RevokeStationFixtureScope(Sha, Epoch);
        Assert.Throws<UnauthorizedAccessException>(
            () => guard.AuthorizeStationFixtureChange(Sha, 1, true, Epoch));
    }

    [Fact]
    public void OperatorPlanConsentDeviceGenerationAndManualClampRemainIsolated()
    {
        var guard = new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(() =>
            guard.AuthorizeStationFixtureScope(Sha, "YES", 1, false, Epoch));
        guard = Granted(1);
        Assert.Throws<InvalidOperationException>(() =>
            guard.AuthorizeSingleClamp(1, true, "AUTHORIZE CLAMP 1 PRESS", Epoch));
        Assert.Throws<UnauthorizedAccessException>(() =>
            guard.AuthorizeStationFixtureChange(Sha, 1, false, Epoch + 1));
        guard.Reset();
        Assert.Throws<UnauthorizedAccessException>(() =>
            guard.AuthorizeStationFixtureChange(Sha, 1, false, Epoch));
    }
}
