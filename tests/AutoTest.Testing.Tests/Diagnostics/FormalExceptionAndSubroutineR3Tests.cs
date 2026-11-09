using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests.Diagnostics;

public sealed class FormalExceptionAndSubroutineR3Tests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }

    private static StepNode Observe(string value = "ready") => N("MockObserve",
        ("variable", "signal"), ("value", value), ("quality", "Observed"));
    private static StepNode AssertObserved(string value = "ready") => N("Assert",
        ("variable", "signal"), ("equals", value));
    private static StepNode Definition(string name, params StepNode[] body) =>
        N("Subroutine", ("name", name)).WithSlot("body", body);
    private static StepNode Call(string name) => N("CallSubroutine", ("target", name));
    private static StepNode Try(IEnumerable<StepNode> body, IEnumerable<StepNode>? catchSteps = null,
        IEnumerable<StepNode>? finallySteps = null) => N("TryCatch")
        .WithSlot("try", body.ToArray()).WithSlot("catch", (catchSteps ?? []).ToArray())
        .WithSlot("finally", (finallySteps ?? []).ToArray());
    private static TestDocument Document(params StepNode[] testNodes)
    {
        var tx = new StepEditTransaction(TestDocument.Create("R3 flow"));
        foreach (var node in testNodes)
            tx.Insert(TestPhase.Test, null, null, tx.Current.Get(TestPhase.Test).Length, node);
        return tx.Current;
    }

    [Fact]
    public async Task NamedSubroutineExecutesOnlyOnCallAndProducesOfficialFacts()
    {
        var definition = Definition("observe", Observe(), AssertObserved());
        var plan = new PlanCompiler().Prepare(Document(definition, Call("observe")));
        Assert.Empty(plan.Diagnostics);
        var result = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Pass, result.Verdict);
        Assert.Single(result.Facts.Where(f => f.Type == "Assert"));
        Assert.Single(result.Facts.Where(f => f.Type == "CallSubroutine"));
        Assert.Contains(result.Facts, f => f.Type == "Subroutine" &&
            f.Detail is not null && f.Detail.Contains("Declaration only", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingDuplicateAndRecursiveSubroutinesBlockPreflight()
    {
        var missing = new PlanCompiler().Prepare(Document(Call("missing")));
        Assert.Contains(missing.Diagnostics, d => d.Contains("missing subroutine target"));

        var duplicate = new PlanCompiler().Prepare(Document(Definition("same"), Definition("same")));
        Assert.Contains(duplicate.Diagnostics, d => d.Contains("duplicate subroutine"));

        var recursive = new PlanCompiler().Prepare(Document(Definition("a", Call("b")),
            Definition("b", Call("a")), Call("a")));
        Assert.Contains(recursive.Diagnostics, d => d.Contains("recursive subroutine call cycle"));
    }

    [Fact]
    public async Task ExplicitFailRunsCatchAndFinallyButNeverBecomesPass()
    {
        var failure = N("Fail", ("message", "injected failure"));
        var skipped = Observe("wrong");
        var handler = N("Log", ("message", "handled"));
        var final = N("Log", ("message", "finally-complete"));
        var doc = Document(Try([failure, skipped], [handler], [final]));
        var plan = new PlanCompiler().Prepare(doc);
        Assert.Empty(plan.Diagnostics);
        var report = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Fail, report.Verdict);
        Assert.DoesNotContain(report.Facts, f => f.StepId == skipped.Id);
        Assert.Contains(report.Facts, f => f.StepId == handler.Id);
        Assert.Contains(report.Facts, f => f.StepId == final.Id && f.Detail == "Finally");
        Assert.Contains(report.Facts, f => f.StepId == failure.Id && f.Verdict == MockStepVerdict.Fail);
    }

    [Fact]
    public async Task SuccessSkipsCatchButAlwaysRunsFinally()
    {
        var catcher = N("Log", ("message", "not-needed"));
        var final = N("Log", ("message", "final"));
        var flow = Try([Observe(), AssertObserved()], [catcher], [final]);
        var plan = new PlanCompiler().Prepare(Document(flow));
        Assert.Empty(plan.Diagnostics);
        var report = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Pass, report.Verdict);
        Assert.DoesNotContain(report.Facts, f => f.StepId == catcher.Id);
        Assert.Contains(report.Facts, f => f.StepId == final.Id && f.Detail == "Finally");
    }

    [Fact]
    public async Task CancellationInsideTryRunsFinallyAndCleanup()
    {
        var final = N("Log", ("message", "finally after stop"));
        var cleanup = N("Log", ("message", "global cleanup"));
        var tx = new StepEditTransaction(Document(Try(
            [N("Wait", ("milliseconds", "1500"))], finallySteps: [final])));
        tx.Insert(TestPhase.Cleanup, null, null, 0, cleanup);
        var plan = new PlanCompiler().Prepare(tx.Current);
        Assert.Empty(plan.Diagnostics);
        var runner = new RunCoordinator();
        var running = runner.StartAsync(plan, new MockStation());
        await Task.Delay(30);
        runner.RequestStop();
        var result = await running;
        Assert.True(result.Stopped);
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.StepId == final.Id && f.Detail == "Finally");
        Assert.Contains(result.Facts, f => f.StepId == cleanup.Id && f.Detail == "Cleanup");
    }

    [Fact]
    public void UnsafeFinallyAndUnsupportedPhysicalStepAreNeverExecutable()
    {
        var unsafeFinally = new PlanCompiler().Prepare(Document(Try(
            [N("Log", ("message", "started"))], finallySteps: [Observe()])));
        Assert.Contains(unsafeFinally.Diagnostics, d => d.Contains("not safe for uncancellable Mock finally"));
        var physical = new PlanCompiler().Prepare(Document(N("CanSend")));
        Assert.Contains(physical.Diagnostics, d => d.Contains("not executable"));
    }
}
