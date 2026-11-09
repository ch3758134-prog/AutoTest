using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Station;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class StationPhysicalControlTests
{
    [Theory]
    [InlineData(0x10, 1, "AUTHORIZE ECU POWER ON")]
    [InlineData(0x10, 0, "AUTHORIZE ECU POWER OFF")]
    [InlineData(0x12, 1, "AUTHORIZE IGN ON")]
    [InlineData(0x14, 0, "AUTHORIZE KEY POWER OFF")]
    [InlineData(0x16, 0, "AUTHORIZE SOLENOID UP")]
    [InlineData(0x16, 1, "AUTHORIZE SOLENOID DOWN")]
    public void StationConsentNamesAreExact(byte command, byte value, string expected)
    {
        Assert.Equal(expected, PhysicalCommandFence.StationConfirmation(command, value));
    }

    [Fact]
    public void StationWritesAreDefaultDeniedAndSingleUse()
    {
        var fence = new PhysicalCommandFence();
        byte[] request = UsbWireCodec.EncodeRequest(11, 0x10, new byte[] { 1 });
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 2));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleStationCommand(0x10, 1, "yes", 2));
        fence.AuthorizeSingleStationCommand(0x10, 1, "AUTHORIZE ECU POWER ON", 2);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 3));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(UsbWireCodec.EncodeRequest(12, 0x10, new byte[] { 0 }), 2));
        fence.AssertAllowedAndConsume(request, 2);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 2));
    }

    [Fact]
    public void KeyMaskIsCompleteTwentyBitsOnCan0AndExplicitRelease()
    {
        const uint mask = 0xA5A5F;
        byte[] frame = PhysicalKeypadProtocol.EncodeUsbCanPayload(mask);
        Assert.Equal(15, frame.Length);
        Assert.Equal(0, frame[0]); // CAN0
        Assert.Equal(0, frame[1]); // standard frame
        Assert.Equal(new byte[] { 0x30, 0x07, 0, 0 }, frame.Skip(2).Take(4).ToArray());
        Assert.Equal(8, frame[6]);
        Assert.Equal(new byte[] { 0x01, 0x5F, 0x5A, 0x0A, 0, 0, 0, 0 }, frame.Skip(7).ToArray());
        Assert.True(PhysicalKeypadProtocol.TryDecodeExactKeyFrame(frame, out uint decoded));
        Assert.Equal(mask, decoded);

        byte[] release = PhysicalKeypadProtocol.EncodeUsbCanPayload(0);
        Assert.Equal(0x80, release[7]);
        Assert.True(PhysicalKeypadProtocol.TryDecodeExactKeyFrame(release, out uint released));
        Assert.Equal(0u, released);
        Assert.Equal("AUTHORIZE KEY RELEASE ALL", PhysicalKeypadProtocol.Confirmation(0));
    }

    [Fact]
    public void KeyFrameAuthorizationRejectsArbitraryCanAndExpiresOnReset()
    {
        var fence = new PhysicalCommandFence();
        const uint mask = 0x0000F;
        byte[] frame = PhysicalKeypadProtocol.EncodeUsbCanPayload(mask);
        byte[] request = UsbWireCodec.EncodeRequest(22, 0x40, frame);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 7));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleKeyFrame(frame, "incorrect", 7));
        var can1 = (byte[])frame.Clone();
        can1[0] = 1;
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AuthorizeSingleKeyFrame(can1, PhysicalKeypadProtocol.Confirmation(mask), 7));
        fence.AuthorizeSingleKeyFrame(frame, PhysicalKeypadProtocol.Confirmation(mask), 7);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 8));
        fence.AssertAllowedAndConsume(request, 7);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 7));
        fence.AuthorizeSingleKeyFrame(frame, PhysicalKeypadProtocol.Confirmation(mask), 7);
        fence.Reset();
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(request, 7));
    }

    [Fact]
    public void MissingOrCorruptKeyFramesCannotBeAuthorized()
    {
        Assert.False(PhysicalKeypadProtocol.TryParseCompleteMask("100000", out _));
        Assert.False(PhysicalKeypadProtocol.TryParseCompleteMask("GGG", out _));
        Assert.True(PhysicalKeypadProtocol.TryParseCompleteMask("0xFFFFF", out uint parsed));
        Assert.Equal(0xFFFFFu, parsed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PhysicalKeypadProtocol.EncodeUsbCanPayload(0x100000u));

        var frame = PhysicalKeypadProtocol.EncodeUsbCanPayload(0xF0F0F);
        frame[14] = 1;
        Assert.False(PhysicalKeypadProtocol.TryDecodeExactKeyFrame(frame, out _));
    }

    [Fact]
    public void ManualVoltageIsHumanEvidenceNotAdcOrEcuConfirmation()
    {
        var tracker = new KeySimVoltageEvidenceTracker();
        tracker.Pending(0x12003, 5);
        Assert.Throws<InvalidOperationException>(() => tracker.ControllerAccepted(0x12004, 5, 9));
        tracker.ControllerAccepted(0x12003, 5, 9);
        var channels = new[]
        {
            new KeySimChannelManualReading(1, 2.35m),
            new KeySimChannelManualReading(2, null),
            new KeySimChannelManualReading(3, null),
            new KeySimChannelManualReading(4, null),
            new KeySimChannelManualReading(5, null)
        };
        var fact = tracker.RecordManual("device-id",
            new KeySimManualVoltageInput("ECU-A", "rev1", "meter", "J1", "12V",
                "connected", "operator", channels, "observed on V1"));
        Assert.Equal("OPERATOR_MANUAL_METER", fact.Source);
        Assert.Equal(0x12003u, fact.LastRequestedCompleteMask);
        Assert.Equal((ushort)9, fact.MainControllerUsbCanSendSequence);
        Assert.Null(fact.Channels.Single(c => c.Channel == 2).MeasuredVolts);
        Assert.Equal("UNKNOWN", fact.EcuResponded);
        Assert.Equal("NOT_EVALUATED", fact.BusinessVerified);
        Assert.Equal("NOT_AVAILABLE", fact.KeyBoardAcknowledgement);
        tracker.Invalidate("DISCONNECT", 6);
        Assert.Contains("UNKNOWN", tracker.Status);
        Assert.DoesNotContain("0x12003", tracker.Status);
    }

    [Fact]
    public void NewPhysicalConsentRevokesOtherPendingPhysicalGrants()
    {
        var fence = new PhysicalCommandFence();
        byte[] station = UsbWireCodec.EncodeRequest(10, 0x10, new byte[] { 1 });
        byte[] key = PhysicalKeypadProtocol.EncodeUsbCanPayload(0x12A45u);
        byte[] keyRequest = UsbWireCodec.EncodeRequest(11, 0x40, key);
        byte[] dutCan = { 1, 0, 0x23, 0x01, 0, 0, 1, 0xA5, 0, 0, 0, 0, 0, 0, 0 };
        byte[] dutRequest = UsbWireCodec.EncodeRequest(12, 0x40, dutCan);

        fence.AuthorizeSingleStationCommand(0x10, 1, "AUTHORIZE ECU POWER ON", 5);
        fence.AuthorizeSingleKeyFrame(key, PhysicalKeypadProtocol.Confirmation(0x12A45u), 5);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(station, 5));
        fence.AuthorizeSingleCanFrame(dutCan, "AUTHORIZE CAN TX " + Convert.ToHexString(dutCan), 5);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(keyRequest, 5));
        fence.AssertAllowedAndConsume(dutRequest, 5);
        Assert.Throws<UnauthorizedAccessException>(() => fence.AssertAllowedAndConsume(dutRequest, 5));
    }

    [Fact]
    public void NewerSessionKeyStateSurvivesLateFailureFromOldGeneration()
    {
        var tracker = new KeySimVoltageEvidenceTracker();
        tracker.Pending(0x0000Fu, 8);
        tracker.Invalidate("DISCONNECT", 9);
        Assert.Throws<InvalidOperationException>(() => tracker.ControllerAccepted(0x0000Fu, 8, 17));
        tracker.Pending(0xA0000u, 10);
        tracker.ControllerAccepted(0xA0000u, 10, 23);
        tracker.Invalidate("FAILED_OR_CANCELLED", 8); // old request finishes late
        Assert.Contains("0xA0000", tracker.Status);
        Assert.Contains("MAIN_CONTROLLER_CAN_SEND_ACCEPTED", tracker.Status);
        Assert.Throws<InvalidOperationException>(() => tracker.Pending(0x0000Fu, 8));
    }

    [Fact]
    public void ManualVoltageRejectsUnmeasuredAndDuplicatedChannels()
    {
        var tracker = new KeySimVoltageEvidenceTracker();
        tracker.Pending(0x00003u, 4);
        tracker.ControllerAccepted(0x00003u, 4, 17);
        var unmeasured = Enumerable.Range(1, 5)
            .Select(i => new KeySimChannelManualReading(i, null)).ToArray();
        var input = new KeySimManualVoltageInput("ECU-X", "rev-2", "meter", "J1",
            "12V", "load", "operator", unmeasured, "calibration");
        Assert.Throws<ArgumentException>(() => tracker.RecordManual("device", input));
        var duplicate = new[]
        {
            new KeySimChannelManualReading(1, 1.05m), new KeySimChannelManualReading(1, null),
            new KeySimChannelManualReading(3, null), new KeySimChannelManualReading(4, null),
            new KeySimChannelManualReading(5, null)
        };
        Assert.Throws<ArgumentException>(() => tracker.RecordManual("device",
            input with { Channels = duplicate }));
        var oneMeasured = new[]
        {
            new KeySimChannelManualReading(1, 1m), new KeySimChannelManualReading(2, null),
            new KeySimChannelManualReading(3, null), new KeySimChannelManualReading(4, null),
            new KeySimChannelManualReading(5, null)
        };
        Assert.Throws<ArgumentException>(() => tracker.RecordManual("device",
            input with { EcuProfile = "", Channels = oneMeasured }));
    }

}
