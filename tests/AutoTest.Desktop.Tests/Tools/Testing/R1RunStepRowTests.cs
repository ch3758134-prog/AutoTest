using AutoTest.Desktop.Tools.Testing;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class R1RunStepRowTests
{
    [Fact]
    public void PlannedTestStepCannotClaimAnEcuMeasurement()
    {
        var step = LabNode.New("Assert", "DUT voltage");
        step.Parameters["variable"] = "battery";
        step.Parameters["equals"] = "12.0";
        var row = new R1RunStepRow(step, LabPhase.Test, 3, 1);
        Assert.Equal("未执行", row.Verdict);
        Assert.Equal("—", row.Actual);
        Assert.Equal("UNKNOWN", row.Quality);
        Assert.Contains("equals=12.0", row.Expected);
    }

    [Fact]
    public void OnlyMatchingActualFactChangesVerdictAndTrace()
    {
        var step = LabNode.New("Observe", "Physical ECU response");
        var row = new R1RunStepRow(step, LabPhase.Test, 1, 0);
        var fact = new LabStepFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            step.Id, LabPhase.Test, "Observe", "50 03", "7F 10 22",
            LabQuality.Observed, LabVerdict.Fail, LabSource.Physical, 5,
            DateTimeOffset.UtcNow, "raw-can-session.jsonl", "NRC 0x22");
        row.Observe(fact);
        Assert.Equal("FAIL", row.Verdict);
        Assert.Equal("7F 10 22", row.Actual);
        Assert.Equal("PHYSICAL", row.Source);
        Assert.Equal("NRC 0x22", row.Diagnostic);
        row.Reset();
        Assert.Equal("未执行", row.Verdict);
        Assert.Equal("—", row.Actual);
    }
}
