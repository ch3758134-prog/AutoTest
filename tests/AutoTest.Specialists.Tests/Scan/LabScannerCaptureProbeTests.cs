using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Specialists.Tests.Scan;

public sealed class LabScannerCaptureProbeTests
{
    [Fact]
    public void FocusedEnterRecordsUnverifiedSourceAndNewTextEvidence()
    {
        var probe = new LabScannerCaptureProbe();
        probe.FocusAcquired();
        probe.TextChanged("M0", true);
        probe.TextChanged("M000001", true);
        var result = probe.CompleteEnter("M000001", true, false);
        Assert.True(result.Accepted);
        Assert.Equal("SCAN_CAPTURED", result.DecisionCode);
        Assert.Equal(7, result.CharacterCount);
        Assert.Equal(2, result.TextChangeEvents);
        Assert.Contains("UNVERIFIED", result.InputMechanism);
        Assert.True(result.EnterTerminated);
    }

    [Fact]
    public void BareEnterOnPrefilledTextIsNotScannerEvidence()
    {
        var probe = new LabScannerCaptureProbe();
        probe.FocusAcquired();
        var result = probe.CompleteEnter("M000001", true, false);
        Assert.False(result.Accepted);
        Assert.Equal("SCAN_NO_NEW_INPUT", result.DecisionCode);
    }

    [Fact]
    public void LostFocusAndBusyStationFailClosed()
    {
        var probe = new LabScannerCaptureProbe();
        probe.FocusAcquired();
        probe.TextChanged("M000001", true);
        probe.FocusLost();
        Assert.Equal("SCANNER_FOCUS_REQUIRED", probe.CompleteEnter("M000001", false, false).DecisionCode);
        probe.FocusAcquired();
        probe.TextChanged("M000002", true);
        Assert.Equal("STATION_BUSY_SCAN_REJECTED", probe.CompleteEnter("M000002", true, true).DecisionCode);
    }

    [Fact]
    public void RepeatedScanRequiresSecondEditAndNewAttemptId()
    {
        var probe = new LabScannerCaptureProbe();
        probe.FocusAcquired();
        probe.TextChanged("M000001", true);
        var first = probe.CompleteEnter("M000001", true, false);
        var bare = probe.CompleteEnter("M000001", true, false);
        probe.TextChanged("M000001", true);
        var again = probe.CompleteEnter("M000001", true, false);
        Assert.True(first.Accepted);
        Assert.False(bare.Accepted);
        Assert.True(again.Accepted);
        Assert.NotEqual(first.AttemptId, again.AttemptId);
    }
}
