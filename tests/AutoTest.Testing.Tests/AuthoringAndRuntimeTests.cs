using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Authoring and immutable-plan tests; actual execution has a separate real-device runtime.</summary>
public sealed class AuthoringAndRuntimeTests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }

    private static StepNode Wait(string ms = "0") => N("Wait", ("milliseconds", ms));

    private static TestDocument Sample()
    {
        var tx = new StepEditTransaction(TestDocument.Create("editor contract"));
        tx.Insert(TestPhase.Prepare, null, null, 0,
            N("SetVariable", ("variable", "mode"), ("value", "run")));
        var condition = N("If", ("variable", "mode"), ("equals", "run"))
            .WithSlot("then", Wait("50"))
            .WithSlot("else", N("SetVariable", ("variable", "mode"), ("value", "skip")));
        tx.Insert(TestPhase.Test, null, null, 0, condition);
        tx.Insert(TestPhase.Cleanup, null, null, 0,
            N("SetVariable", ("variable", "mode"), ("value", "clean")));
        return tx.Current;
    }

    [Fact]
    public void EditCopyMoveUndoRedo()
    {
        var tx = new StepEditTransaction(TestDocument.Create("edit"));
        var a = Wait();
        tx.Insert(TestPhase.Test, null, null, 0, a);
        tx.Copy(a.Id, TestPhase.Test, null, null, 1);
        Assert.NotEqual(tx.Current.Get(TestPhase.Test)[0].Id, tx.Current.Get(TestPhase.Test)[1].Id);
        tx.Move(a.Id, TestPhase.Prepare, null, null, 0);
        Assert.Single(tx.Current.Get(TestPhase.Prepare));
        tx.Undo();
        Assert.Equal(2, tx.Current.Get(TestPhase.Test).Length);
        tx.Redo();
        Assert.Single(tx.Current.Get(TestPhase.Prepare));
        tx.Delete(a.Id);
        Assert.Empty(tx.Current.Get(TestPhase.Prepare));
        tx.Undo();
        Assert.Single(tx.Current.Get(TestPhase.Prepare));
    }

    [Fact]
    public void RejectIllegalSlotsAndCaseOutsideSwitch()
    {
        var tx = new StepEditTransaction(TestDocument.Create("bad"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Case", ("equals", "1")).WithSlot("body"));
        Assert.Contains(StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()),
            e => e.Contains("outside Switch"));
    }

    [Fact]
    public void RejectLoopOutOfRange()
    {
        var tx = new StepEditTransaction(TestDocument.Create("loop"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Loop", ("count", "1001")).WithSlot("body"));
        Assert.Contains(StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()),
            e => e.Contains("Loop count"));
    }

    [Fact]
    public void SaveReloadRetainsIdentitiesAndPhases()
    {
        var original = Sample();
        Assert.Empty(StepTreeValidator.ValidateTree(original, StepDescriptorCatalog.Builtins()));
        var restored = TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(original));
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Walk().Select(n => n.Id), restored.Walk().Select(n => n.Id));
        Assert.Equal(original.Get(TestPhase.Cleanup).Length, restored.Get(TestPhase.Cleanup).Length);
    }

    [Fact]
    public void FailedMutationDoesNotCorruptHistory()
    {
        var tx = new StepEditTransaction(TestDocument.Create("edit"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tx.Insert(TestPhase.Test, null, null, 5, Wait()));
        Assert.False(tx.CanUndo);
        var a = Wait();
        tx.Insert(TestPhase.Test, null, null, 0, a);
        Assert.Throws<InvalidOperationException>(() =>
            tx.Insert(TestPhase.Test, null, null, 1, a));
        Assert.Single(tx.Current.Get(TestPhase.Test));
        tx.Undo();
        Assert.Empty(tx.Current.Get(TestPhase.Test));
    }

    [Fact]
    public void PreparedPlanKeepsIndependentSnapshotAndFingerprint()
    {
        var original = Sample();
        var prepared = new PlanCompiler().Prepare(original);
        Assert.Empty(prepared.Diagnostics);
        Assert.True(prepared.ValidateFingerprint());
        var tx = new StepEditTransaction(original);
        var wait = original.Walk().First(n => n.Type == "Wait");
        tx.SetProperty(wait.Id, "milliseconds", "250");
        Assert.NotEqual(prepared.Fingerprint, new PlanCompiler().Prepare(tx.Current).Fingerprint);
        var restored = prepared.RestoreDocument();
        Assert.Equal("50", restored.Walk().First(n => n.Type == "Wait").Properties["milliseconds"]);
    }

    [Fact]
    public void UnsupportedExecutionNodesCannotClaimToBeRunnable()
    {
        var tx = new StepEditTransaction(TestDocument.Create("unsupported"));
        tx.Insert(TestPhase.Test, null, null, 0, N("Flash"));
        Assert.NotEmpty(new PlanCompiler().Prepare(tx.Current).Diagnostics);
    }

    [Fact]
    public void MissingResourceBlocksPreparation()
    {
        var tx = new StepEditTransaction(TestDocument.Create("asset"));
        tx.Insert(TestPhase.Test, null, null, 0,
            Wait().WithProperty("asset", "missing.dbc"));
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            e => e.Contains("unresolved asset"));
    }

    [Fact]
    public void RemovedOrUnrecognizedStepsAreRejectedRatherThanExecuted()
    {
        var tx = new StepEditTransaction(TestDocument.Create("unknown"));
        tx.Insert(TestPhase.Test, null, null, 0, N("ObsoleteUnsupportedStep"));
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            e => e.Contains("unknown node"));
    }
}
