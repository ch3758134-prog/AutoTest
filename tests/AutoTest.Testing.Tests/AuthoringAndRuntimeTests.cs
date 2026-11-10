using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class AuthoringAndRuntimeTests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }
    private static StepNode Observe(string value = "8", string quality = "Observed") =>
        N("MockObserve", ("variable", "pressure"), ("value", value), ("quality", quality));
    private static StepNode AssertPressure(string expected = "8") =>
        N("Assert", ("variable", "pressure"), ("equals", expected));
    private static TestDocument Sample()
    {
        var d = TestDocument.Create("mock acceptance");
        var tx = new StepEditTransaction(d);
        tx.Insert(TestPhase.Prepare, null, null, 0, N("SetVariable", ("variable", "mode"), ("value", "run")));
        var condition = N("If", ("variable", "mode"), ("equals", "run"))
            .WithSlot("then", Observe(), AssertPressure()).WithSlot("else");
        tx.Insert(TestPhase.Test, null, null, 0, condition);
        tx.Insert(TestPhase.Cleanup, null, null, 0, N("SetVariable", ("variable", "mode"), ("value", "clean")));
        return tx.Current;
    }
    [Fact]
    public void C1_T01_EditCopyMoveUndoRedo()
    {
        var tx = new StepEditTransaction(TestDocument.Create("edit"));
        var a = N("Wait", ("milliseconds", "0"));
        tx.Insert(TestPhase.Test, null, null, 0, a);
        tx.Copy(a.Id, TestPhase.Test, null, null, 1);
        Assert.NotEqual(tx.Current.Get(TestPhase.Test)[0].Id, tx.Current.Get(TestPhase.Test)[1].Id);
        tx.Move(a.Id, TestPhase.Prepare, null, null, 0);
        Assert.Single(tx.Current.Get(TestPhase.Prepare));
        tx.Undo(); Assert.Equal(2, tx.Current.Get(TestPhase.Test).Length);
        tx.Redo(); Assert.Single(tx.Current.Get(TestPhase.Prepare));
        tx.Delete(a.Id); Assert.Empty(tx.Current.Get(TestPhase.Prepare));
        tx.Undo(); Assert.Single(tx.Current.Get(TestPhase.Prepare));
    }
    [Fact]
    public void C1_T02_RejectIllegalSlotsAndCaseOutsideSwitch()
    {
        var tx = new StepEditTransaction(TestDocument.Create("bad"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Case", ("equals", "1")).WithSlot("body"));
        var errors = StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins());
        Assert.Contains(errors, e => e.Contains("outside Switch"));
    }
    [Fact]
    public void C1_T03_RejectLoopOutOfRange()
    {
        var tx = new StepEditTransaction(TestDocument.Create("loop"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Loop", ("count", "1001")).WithSlot("body"));
        Assert.Contains(StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()),
            x => x.Contains("Loop count"));
    }
    [Fact]
    public void C1_T06_SaveReloadRetainsIdentities()
    {
        var original = Sample();
        var restored = TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(original));
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Walk().Select(n => n.Id), restored.Walk().Select(n => n.Id));
    }
    [Fact]
    public void C1_UndoRedoAndFailedMutationDoNotCorruptHistory()
    {
        var tx = new StepEditTransaction(TestDocument.Create("edit"));
        Assert.Throws<ArgumentOutOfRangeException>(() => tx.Insert(TestPhase.Test, null, null, 5, Observe()));
        Assert.False(tx.CanUndo);
        var a = Observe(); tx.Insert(TestPhase.Test, null, null, 0, a);
        Assert.Throws<InvalidOperationException>(() => tx.Insert(TestPhase.Test, null, null, 1, a));
        Assert.Single(tx.Current.Get(TestPhase.Test));
        tx.Undo(); Assert.Empty(tx.Current.Get(TestPhase.Test));
    }
    [Theory]
    [InlineData("8", "Observed", "8", MockStepVerdict.Pass)]
    [InlineData("7", "Observed", "8", MockStepVerdict.Fail)]
    [InlineData("8", "Missing", "8", MockStepVerdict.Inconclusive)]
    public async Task C2_T01_VerifiedEvidenceVerdict(string actual, string quality,
        string expected, MockStepVerdict verdict)
    {
        var tx = new StepEditTransaction(TestDocument.Create("judge"));
        tx.Insert(TestPhase.Test, null, null, 0, Observe(actual, quality));
        tx.Insert(TestPhase.Test, null, null, 1, AssertPressure(expected));
        var run = await new RunCoordinator().StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        Assert.Equal(verdict, run.Verdict);
        Assert.Equal(verdict, run.Facts.Last().Verdict);
    }
    [Fact]
    public async Task C2_T02_PlanSnapshotIsIndependentOfEditorChanges()
    {
        var original = Sample(); var prepared = new PlanCompiler().Prepare(original);
        var tx = new StepEditTransaction(original);
        var assert = original.Walk().First(x => x.Type == "Assert");
        tx.SetProperty(assert.Id, "equals", "DIFFERENT");
        var run = await new RunCoordinator().StartAsync(prepared, new MockStation());
        Assert.Equal(MockStepVerdict.Pass, run.Verdict);
        Assert.NotEqual(prepared.Fingerprint, new PlanCompiler().Prepare(tx.Current).Fingerprint);
    }
    [Fact]
    public async Task C2_T03_StopExecutesCleanupAndSetsStopped()
    {
        var tx = new StepEditTransaction(TestDocument.Create("stop"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Wait", ("milliseconds", "5000")));
        tx.Insert(TestPhase.Cleanup, null, null, 0, N("SetVariable", ("variable", "cleanup"), ("value", "done")));
        var coordinator = new RunCoordinator();
        var task = coordinator.StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        coordinator.RequestStop();
        var result = await task;
        Assert.True(result.Stopped);
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, fact => fact.Detail == "Cleanup");
    }
    [Fact]
    public async Task C2_T04_LoopRunsExactIterations()
    {
        var tx = new StepEditTransaction(TestDocument.Create("loop"));
        tx.Insert(TestPhase.Test, null, null, 0,
            N("Loop", ("count", "3")).WithSlot("body", Observe(), AssertPressure()));
        var result = await new RunCoordinator().StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        Assert.Equal(7, result.Facts.Length);
        Assert.Equal(MockStepVerdict.Pass, result.Verdict);
    }
    [Fact]
    public void C2_UnsupportedNodesCannotRun()
    {
        var tx = new StepEditTransaction(TestDocument.Create("unsupported"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Flash"));
        Assert.NotEmpty(new PlanCompiler().Prepare(tx.Current).Diagnostics);
    }
    [Fact]
    public void C2_T06_MissingResourceBlocksPreparation()
    {
        var tx = new StepEditTransaction(TestDocument.Create("asset"));
        tx.Insert(TestPhase.Test, null, null, 0, Observe().WithProperty("asset", "missing.dbc"));
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            x => x.Contains("unresolved asset"));
    }
    [Fact]
    public async Task C2_NoAssertionMustNotPass()
    {
        var tx = new StepEditTransaction(TestDocument.Create("no-evidence"));
        tx.Insert(TestPhase.Test, null, null, 0,
            N("SetVariable", ("variable", "mode"), ("value", "ready")));
        var result = await new RunCoordinator().StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
    }
    [Fact]
    public async Task C2_PauseAndResumeOnlyAtStepBoundary()
    {
        var tx = new StepEditTransaction(TestDocument.Create("pause"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Wait", ("milliseconds", "50")));
        tx.Insert(TestPhase.Test, null, null, 1, Observe());
        tx.Insert(TestPhase.Test, null, null, 2, AssertPressure());
        var coordinator = new RunCoordinator();
        var running = coordinator.StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        coordinator.PauseAtBoundary();
        await Task.Delay(100);
        Assert.False(running.IsCompleted);
        coordinator.Resume();
        Assert.Equal(MockStepVerdict.Pass, (await running).Verdict);
    }

    [Theory]
    [InlineData("run", MockStepVerdict.Pass)]
    [InlineData("other", MockStepVerdict.Fail)]
    public async Task C2_SwitchExecutesMatchingOrDefaultCase(string value, MockStepVerdict expected)
    {
        var tx = new StepEditTransaction(TestDocument.Create("switch"));
        tx.Insert(TestPhase.Prepare, null, null, 0,
            N("SetVariable", ("variable", "mode"), ("value", value)));
        var branches = N("Switch", ("variable", "mode")).WithSlot("cases",
            N("Case", ("equals", "run")).WithSlot("body", Observe(), AssertPressure()),
            N("Case", ("equals", "*")).WithSlot("body", Observe("7"), AssertPressure()));
        tx.Insert(TestPhase.Test, null, null, 0, branches);
        var plan = new PlanCompiler().Prepare(tx.Current);
        Assert.Empty(plan.Diagnostics);
        var report = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(expected, report.Verdict);
        Assert.Contains(report.Facts, f => f.Type == "Switch");
        Assert.Single(report.Facts.Where(f => f.Type == "Case"));
    }

    [Fact]
    public async Task C2_SwitchWithoutMatchIsInconclusive()
    {
        var tx = new StepEditTransaction(TestDocument.Create("switch-none"));
        tx.Insert(TestPhase.Prepare, null, null, 0,
            N("SetVariable", ("variable", "mode"), ("value", "other")));
        tx.Insert(TestPhase.Test, null, null, 0,
            N("Switch", ("variable", "mode")).WithSlot("cases",
                N("Case", ("equals", "run")).WithSlot("body", Observe(), AssertPressure())));
        var report = await new RunCoordinator().StartAsync(
            new PlanCompiler().Prepare(tx.Current), new MockStation());
        Assert.Equal(MockStepVerdict.Inconclusive, report.Verdict);
        Assert.Contains(report.Facts, f => f.Detail is not null &&
            f.Detail.Contains("SWITCH_NO_MATCH"));
    }

    [Fact]
    public void C1_RejectDuplicateSwitchCasesBeforeRun()
    {
        var tx = new StepEditTransaction(TestDocument.Create("duplicate-switch"));
        tx.Insert(TestPhase.Test, null, null, 0,
            N("Switch", ("variable", "mode")).WithSlot("cases",
                N("Case", ("equals", "x")).WithSlot("body"),
                N("Case", ("equals", "x")).WithSlot("body")));
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            x => x.Contains("duplicate Switch Case values"));
    }

}
