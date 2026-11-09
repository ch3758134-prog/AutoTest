using AutoTest.Testing.Authoring;
using AutoTest.Testing.Diagnostics;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests.Diagnostics;

public sealed class FormalControlFlowR2Tests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }
    private static StepNode Set(string variable, string value) => N("SetVariable", ("variable", variable), ("value", value));
    private static StepNode Observe(string value, string quality = "Observed") => N("MockObserve",
        ("variable", "signal"), ("value", value), ("quality", quality));
    private static StepNode AssertSignal(string value) => N("Assert", ("variable", "signal"), ("equals", value));
    private static StepNode Case(string value, params StepNode[] steps) =>
        N("Case", ("equals", value)).WithSlot("body", steps);
    private static StepNode Default(params StepNode[] steps) => N("Default").WithSlot("body", steps);
    private static StepNode Switch(params StepNode[] arms) =>
        N("Switch", ("variable", "mode")).WithSlot("cases", arms);
    private static StepNode When(string value, string timeout, params StepNode[] children) =>
        N("When", ("variable", "signal"), ("equals", value), ("timeoutMs", timeout))
            .WithSlot("body", children);
    private static TestDocument Document(params StepNode[] nodes)
    {
        var transaction = new StepEditTransaction(TestDocument.Create("R2 formal flow"));
        foreach (var n in nodes)
            transaction.Insert(TestPhase.Test, null, null, transaction.Current.Get(TestPhase.Test).Length, n);
        return transaction.Current;
    }

    [Theory]
    [InlineData("go", "selected")]
    [InlineData("unknown", "fallback")]
    public async Task SwitchChoosesExactCaseOrDefaultOnly(string mode, string expected)
    {
        var branch = Switch(Case("go", Observe("selected"), AssertSignal("selected")),
            Default(Observe("fallback"), AssertSignal("fallback")));
        var plan = new PlanCompiler().Prepare(Document(Set("mode", mode), branch));
        Assert.Empty(plan.Diagnostics);
        var result = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Pass, result.Verdict);
        Assert.Equal(expected, result.Facts.Last(f => f.Type == "Assert").Actual);
        Assert.Single(result.Facts.Where(f => f.Type is "Case" or "Default"));
        Assert.Contains(result.Facts, f => f.Type == "Switch" &&
            f.Detail != null && f.Detail.Contains("branch selected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoMatchWithoutDefaultCannotPass()
    {
        var plan = new PlanCompiler().Prepare(Document(Set("mode", "unknown"),
            Switch(Case("go", Observe("not-selected")))));
        Assert.Empty(plan.Diagnostics);
        var result = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        var fact = Assert.Single(result.Facts.Where(x => x.Type == "Switch"));
        Assert.Contains("MOCK_SWITCH_NO_MATCH", fact.Detail);
        Assert.DoesNotContain(result.Facts, x => x.Type == "MockObserve");
        Assert.Equal("MOCK_SWITCH_UNRESOLVED", TestDiagnosticArchive.FromFact(
            result.Run.ToString(), "Test", fact).ErrorCode);
    }

    [Fact]
    public void ValidatorRejectsDuplicateCasesAndOrphanDefault()
    {
        var doc = Document(Switch(Case("same"), Case("same"), Default(), Default()), Default());
        var problems = new PlanCompiler().Prepare(doc).Diagnostics;
        Assert.Contains(problems, x => x.Contains("duplicate Switch Case", StringComparison.Ordinal));
        Assert.Contains(problems, x => x.Contains("one Default", StringComparison.Ordinal));
        Assert.Contains(problems, x => x.Contains("outside Switch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenWaitsForFreshMockObservationAndOnlyThenRunsBody()
    {
        var station = new MockStation();
        var plan = new PlanCompiler().Prepare(Document(When("ready", "500", AssertSignal("ready"))));
        Assert.Empty(plan.Diagnostics);
        var running = new RunCoordinator().StartAsync(plan, station);
        await Task.Delay(30);
        Assert.False(running.IsCompleted);
        station.Set("signal", "ready", MockQuality.Observed);
        var report = await running;
        Assert.Equal(MockStepVerdict.Pass, report.Verdict);
        Assert.Contains(report.Facts, f => f.Type == "When" && f.Verdict == MockStepVerdict.Pass);
        Assert.Contains(report.Facts, f => f.Type == "Assert" && f.Verdict == MockStepVerdict.Pass);
    }

    [Theory]
    [InlineData("wrong", "Observed", MockStepVerdict.Fail, "MOCK_WHEN_TIMEOUT")]
    [InlineData("unknown", "Missing", MockStepVerdict.Inconclusive, "MOCK_WHEN_NO_OBSERVATION")]
    public async Task WhenTimeoutNeverProducesFalsePass(string value, string quality,
        MockStepVerdict verdict, string error)
    {
        var station = new MockStation();
        station.Set("signal", value, Enum.Parse<MockQuality>(quality));
        var plan = new PlanCompiler().Prepare(Document(When("ready", "0", Observe("unreachable"))));
        var report = await new RunCoordinator().StartAsync(plan, station);
        Assert.Equal(verdict, report.Verdict);
        var fact = Assert.Single(report.Facts);
        Assert.Equal(error, TestDiagnosticArchive.FromFact(report.Run.ToString(), "Test", fact).ErrorCode);
        Assert.DoesNotContain(report.Facts, f => f.Type == "MockObserve");
    }

    [Fact]
    public async Task StopDuringWhenRunsCleanupAndHasNoPhysicalOutput()
    {
        var tx = new StepEditTransaction(Document(When("ready", "1000", Observe("unreachable"))));
        var cleanup = N("Log", ("message", "mock cleanup audit"));
        tx.Insert(TestPhase.Cleanup, null, null, 0, cleanup);
        var plan = new PlanCompiler().Prepare(tx.Current);
        Assert.Empty(plan.Diagnostics);
        var runner = new RunCoordinator();
        var running = runner.StartAsync(plan, new MockStation());
        await Task.Delay(30);
        runner.RequestStop();
        var report = await running;
        Assert.True(report.Stopped);
        Assert.Equal(MockStepVerdict.Inconclusive, report.Verdict);
        Assert.Contains(report.Facts, f => f.StepId == cleanup.Id && f.Detail == "Cleanup" &&
            f.Actual == "mock cleanup audit");
    }

    [Fact]
    public async Task ExplicitMockFailureHasSpecificDiagnosticCode()
    {
        var plan = new PlanCompiler().Prepare(Document(N("Fail", ("message", "assertion injected"))));
        Assert.Empty(plan.Diagnostics);
        var report = await new RunCoordinator().StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Fail, report.Verdict);
        Assert.Equal("MOCK_EXPLICIT_FAILURE", TestDiagnosticArchive.FromFact(
            report.Run.ToString(), "Test", report.Facts.Single()).ErrorCode);
    }

    [Fact]
    public void DeepTreeProducesPreflightDiagnosticWithoutRecursiveCodecTraversal()
    {
        var node = Observe("value");
        for (var i = 0; i < 34; i++) node = N("Sequence").WithSlot("body", node);
        var prepared = new PlanCompiler().Prepare(Document(node));
        Assert.Contains(prepared.Diagnostics, d => d.Contains("depth exceeds 32", StringComparison.Ordinal));
        Assert.False(prepared.ValidateFingerprint());
    }

    [Fact]
    public void UnsupportedStateChangingStepRemainsPreflightBlocked()
    {
        var plan = new PlanCompiler().Prepare(Document(N("UdsRequest")));
        Assert.Contains(plan.Diagnostics, e => e.Contains("UdsRequest not executable", StringComparison.Ordinal));
    }
}
