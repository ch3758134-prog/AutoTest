using System.Collections.Immutable;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Records.Model;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class R1ProductionAccountingTests
{
    private static R1PhysicalScanBinding Binding(bool production) =>
        new("S1234", "S1234", "R1", "revision", "EOL", "1",
            Guid.NewGuid().ToString("D"), 1, new string('a', 64),
            "C:\\station.json", "1", new string('b', 64), production);

    private static LabRunReport Report(LabSource source = LabSource.Physical,
        LabVerdict verdict = LabVerdict.Pass, string? trace = null)
    {
        var run = Guid.NewGuid();
        var fact = new LabStepFact(Guid.NewGuid(), Guid.NewGuid(), run, Guid.NewGuid(),
            LabPhase.Test, "Assert", "ready", "ready", LabQuality.Observed,
            verdict, source, 9, DateTimeOffset.UtcNow, trace, null);
        return new LabRunReport(run, new string('c', 64), source,
            verdict, false, ImmutableArray.Create(fact));
    }

    [Fact]
    public void ExplicitPublishedProductionRecipeCountsRealPhysicalPass()
    {
        var report = Report(trace: Guid.NewGuid().ToString("D"));
        Assert.Equal(RecordRunMode.Production,
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(true), report));
        Assert.Equal(RecordRunMode.Diagnostic,
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(false), report));
    }

    [Fact]
    public void ProductionFailRemainsRecordedAsProductionAttempt()
    {
        var report = Report(verdict: LabVerdict.Fail);
        Assert.Equal(RecordRunMode.Production,
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(true), report));
    }

    [Fact]
    public void ProductionPassCannotBeInventedWithoutPhysicalObservationTrace()
    {
        var report = Report();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(true), report));
        Assert.Contains("PRODUCTION_PASS_WITHOUT_VERIFIED_TEST_STEP", exception.Message);
    }

    [Fact]
    public void ConfirmedPhysicalControllerActionCanFormAProductionPass()
    {
        var run = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var receipt = new LabStepFact(operationId, Guid.NewGuid(), run, Guid.NewGuid(),
            LabPhase.Test, "ControllerState", "KEY_SIM hold", null,
            LabQuality.Observed, LabVerdict.Pass, LabSource.Physical,
            9, DateTimeOffset.UtcNow, operationId.ToString("D"), null);
        var report = new LabRunReport(run, new string('d', 64), LabSource.Physical,
            LabVerdict.Pass, false, ImmutableArray.Create(receipt));
        Assert.Equal(RecordRunMode.Production,
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(true), report));
    }

    [Fact]
    public void RecordedReplayNeverCountsAsActualProductionRun()
    {
        var report = Report(LabSource.Replay, trace: Guid.NewGuid().ToString("D"));
        Assert.Throws<InvalidOperationException>(() =>
            R1PhysicalRunRecordBridge.ClassifyMode(Binding(true), report));
    }
}
