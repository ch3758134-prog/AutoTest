using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionAdmissionReviewTests
{
    private static ProductionAdmissionFacts All(bool start = false, bool recorder = false) =>
        new(true, true, true, true, true, true, true,
            true, true, true, start, recorder);

    [Fact]
    public void FrozenRealScanNeverTurnsIntoFormalStartOrPass()
    {
        var decision = ProductionAdmissionReview.Evaluate(All());
        Assert.False(decision.CanBeginFormalInspection);
        Assert.Equal("PRODUCTION_PHYSICAL_START_NOT_CONNECTED", decision.Code);
        Assert.Equal(10, decision.SatisfiedChecks);
        Assert.Equal(12, decision.TotalChecks);
    }

    [Theory]
    [InlineData("usb", "PRODUCTION_USB_DISCONNECTED")]
    [InlineData("resource", "PRODUCTION_ASSETS_NOT_READY")]
    [InlineData("scan", "PRODUCTION_IDENTITY_NOT_ACCEPTED")]
    [InlineData("project", "PRODUCTION_PROJECT_SNAPSHOT_MISMATCH")]
    [InlineData("epoch", "PRODUCTION_CONTROLLER_SESSION_STALE")]
    [InlineData("com", "PRODUCTION_SCANNER_SESSION_STALE")]
    [InlineData("baud", "PRODUCTION_CAN_RATE_MISMATCH")]
    public void StaleOrMissingProductionInputsAreFailClosed(string missing, string expected)
    {
        var facts = All() with
        {
            ControllerConnected = missing != "usb",
            ResourcesExecutable = missing != "resource",
            AcceptedScannerObservation = missing != "scan",
            FrozenProjectMatches = missing != "project",
            SameControllerSession = missing != "epoch",
            SameScannerPort = missing != "com",
            SameEcuCanRate = missing != "baud"
        };
        var decision = ProductionAdmissionReview.Evaluate(facts);
        Assert.Equal(expected, decision.Code);
        Assert.False(decision.CanBeginFormalInspection);
    }

    [Fact]
    public void TrustedStartWithoutVerifiedProductionRecordCannotAdmit()
    {
        var decision = ProductionAdmissionReview.Evaluate(All(start: true));
        Assert.Equal("PRODUCTION_INSPECTION_NOT_CONNECTED", decision.Code);
        Assert.False(decision.CanBeginFormalInspection);
    }

    [Fact]
    public void AllPreconditionsMeanAdmissionOnlyNotInspectionVerdict()
    {
        var decision = ProductionAdmissionReview.Evaluate(All(start: true, recorder: true));
        Assert.True(decision.CanBeginFormalInspection);
        Assert.Equal("PRODUCTION_ADMISSION_CONDITIONS_MET", decision.Code);
        Assert.DoesNotContain("PASS", decision.Headline);
    }
}
