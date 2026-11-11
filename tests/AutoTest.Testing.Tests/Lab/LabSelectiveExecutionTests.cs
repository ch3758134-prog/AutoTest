using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Testing.Tests.Lab;

public sealed class LabSelectiveExecutionTests
{
    private static LabNode Case(string name, string message)
    {
        var node = LabNode.New("TestCase", name);
        node.Slots["body"] = new List<LabNode>();
        var action = LabNode.New("Log", message);
        action.Parameters["message"] = message;
        node.Slots["body"].Add(action);
        return node;
    }

    private static LabDocument Program()
    {
        var doc = LabDocument.New("EOL test");
        var setup = LabNode.New("Log", "prepare");
        setup.Parameters["message"] = "prepare";
        doc.Phases[LabPhase.Prepare].Add(setup);
        doc.Phases[LabPhase.Test].Add(Case("CAN signal", "CAN test"));
        doc.Phases[LabPhase.Test].Add(Case("UDS reading", "UDS test"));
        var cleanup = LabNode.New("Log", "cleanup");
        cleanup.Parameters["message"] = "cleanup";
        doc.Phases[LabPhase.Cleanup].Add(cleanup);
        return doc;
    }

    [Fact]
    public void DiscoverIdentifiesRealTestCasesRatherThanIndividualActions()
    {
        var cases = LabSelectiveExecution.Discover(Program());
        Assert.Equal(2, cases.Length);
        Assert.Equal(new[] {"CAN signal", "UDS reading"}, cases.Select(x => x.Name));
        Assert.All(cases, x => Assert.Equal(1, x.StepCount));
        Assert.All(cases, x => Assert.False(x.IsLegacy));
    }

    [Fact]
    public void SelectedPlanDoesNotMutateOriginalAndPreservesPrepareAndCleanup()
    {
        var doc = Program();
        var sourceJson = LabDocumentCodec.Save(doc);
        var cases = LabSelectiveExecution.Discover(doc);
        var result = LabSelectiveExecution.Prepare(doc, new[] { cases[1].Id }, new LabPlanCompiler());
        Assert.True(result.Plan.Ready);
        Assert.Single(result.SelectedCaseIds);
        Assert.Equal(cases[1].Id, result.SelectedCaseIds[0]);
        var selected = result.Plan.Restore();
        Assert.Single(selected.Phases[LabPhase.Prepare]);
        Assert.Single(selected.Phases[LabPhase.Cleanup]);
        Assert.Single(selected.Phases[LabPhase.Test]);
        Assert.Equal("UDS reading", selected.Phases[LabPhase.Test][0].Name);
        Assert.Equal(sourceJson, LabDocumentCodec.Save(doc));
        Assert.NotEqual(result.SourceProgramDigest, result.Plan.Digest);
    }

    [Fact]
    public void SelectionFailsClosedWhenEmptyOrUnknown()
    {
        var doc = Program();
        Assert.Throws<InvalidOperationException>(() =>
            LabSelectiveExecution.Prepare(doc, Array.Empty<Guid>(), new LabPlanCompiler()));
        Assert.Throws<InvalidOperationException>(() =>
            LabSelectiveExecution.Prepare(doc, new[] { Guid.NewGuid() }, new LabPlanCompiler()));
    }

    [Fact]
    public void LegacyFlatTestProgramRemainsSelectableAsOneCase()
    {
        var doc = LabDocument.New("legacy");
        var old = LabNode.New("Log");
        old.Parameters["message"] = "old action";
        doc.Phases[LabPhase.Test].Add(old);
        var caseInfo = Assert.Single(LabSelectiveExecution.Discover(doc));
        Assert.True(caseInfo.IsLegacy);
        Assert.Equal(doc.Id, caseInfo.Id);
        var filtered = LabSelectiveExecution.Prepare(doc, new[] { caseInfo.Id }, new LabPlanCompiler());
        Assert.Single(filtered.Plan.Restore().Phases[LabPhase.Test]);
    }

    [Fact]
    public void NestedTestCaseAndCaseOutsideTestPhaseAreRejected()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Prepare].Add(Case("bad", "not a test case"));
        Assert.False(new LabPlanCompiler().Prepare(doc).Ready);
        doc.Phases[LabPhase.Prepare].Clear();
        var outer = Case("valid", "ok");
        outer.Slots["body"].Add(Case("invalid nested", "bad"));
        doc.Phases[LabPhase.Test].Add(outer);
        Assert.False(new LabPlanCompiler().Prepare(doc).Ready);
    }
}
