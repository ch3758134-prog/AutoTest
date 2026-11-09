using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Diagnostics;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests.Diagnostics;

public sealed class FormalTestDiagnosticTests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }

    private static StepNode Observe(string value = "ready") => N("MockObserve",
        ("variable", "state"), ("value", value), ("quality", "Observed"));
    private static StepNode AssertState(string expected = "ready") =>
        N("Assert", ("variable", "state"), ("equals", expected));

    private static TestDocument Document(params StepNode[] steps)
    {
        var edit = new StepEditTransaction(TestDocument.Create("formal-test"));
        foreach (var step in steps)
            edit.Insert(TestPhase.Test, null, null, edit.Current.Get(TestPhase.Test).Length, step);
        return edit.Current;
    }

    [Fact]
    public async Task FormalDocumentDoesNotUseLabDocumentAndProducesTraceableRunId()
    {
        var document = Document(Observe(), AssertState());
        var serialized = TestDocumentCodec.Serialize(document);
        var reopened = TestDocumentCodec.Deserialize(serialized);
        Assert.Equal(document.Id, reopened.Id);
        Assert.Equal(document.Walk().Select(x => x.Id), reopened.Walk().Select(x => x.Id));
        var plan = new PlanCompiler().Prepare(reopened);
        Assert.Empty(plan.Diagnostics);
        Assert.True(plan.ValidateFingerprint());
        var facts = new List<StepFact>();
        var runner = new RunCoordinator();
        runner.FactProduced += facts.Add;
        var report = await runner.StartAsync(plan, new MockStation());
        Assert.Equal(MockStepVerdict.Pass, report.Verdict);
        Assert.Equal(2, report.Facts.Length);
        Assert.Equal(report.Facts.Select(f => f.OperationId), facts.Select(f => f.OperationId));
        Assert.NotEqual(Guid.Empty, report.Run.Value);
        Assert.All(report.Facts, f => Assert.NotEqual(Guid.Empty, f.OperationId));
        Assert.Equal(plan.Fingerprint, report.PlanFingerprint);
    }

    [Fact]
    public void TypedAssetReferenceMustHaveExactRevisionAndFingerprint()
    {
        var asset = AssetId.New();
        var sha = new string('a', 64);
        var refNode = Observe().WithProperty("assetId", asset.ToString())
            .WithProperty("assetRevision", "2").WithProperty("assetSha256", sha);
        var doc = Document(refNode, AssertState());
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            text => text.Contains("resolver is unavailable", StringComparison.OrdinalIgnoreCase));
        var ok = new StubResolver(new AssetRef(asset, AssetRevision.Create(2),
            RevisionFingerprint.FromSha256(sha)));
        Assert.Empty(new PlanCompiler().Prepare(doc, assetResolver: ok).Diagnostics);
        var stale = new StubResolver(new AssetRef(asset, AssetRevision.Create(2),
            RevisionFingerprint.FromSha256(new string('b', 64))));
        Assert.Contains(new PlanCompiler().Prepare(doc, assetResolver: stale).Diagnostics,
            x => x.Contains("stale asset binding", StringComparison.Ordinal));
    }

    private sealed class StubResolver(AssetRef? asset) : IAssetResolver
    {
        public OperationResult<AssetRef> Resolve(AssetId id, AssetRevision revision) =>
            asset is null ? OperationResult<AssetRef>.Rejected("NOT_FOUND", "Asset not found") :
                OperationResult<AssetRef>.Accepted(asset);
    }

    [Fact]
    public async Task PhysicalStepIsBlockedBeforeStartingMockRunner()
    {
        var doc = Document(N("Flash"));
        var plan = new PlanCompiler().Prepare(doc);
        Assert.Contains(plan.Diagnostics, x => x.Contains("not executable", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new RunCoordinator().StartAsync(plan, new MockStation());
        });
    }

    [Fact]
    public async Task SkipDoesNotProduceFalsePassAfterSuccessfulAssertion()
    {
        var target = AssertState();
        var doc = Document(N("Wait", ("milliseconds", "70")), Observe(), AssertState(), target);
        var runner = new RunCoordinator();
        var running = runner.StartAsync(new PlanCompiler().Prepare(doc), new MockStation());
        runner.Skip(target.Id);
        var result = await running;
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.StepId == target.Id && f.Verdict == MockStepVerdict.Skipped);
        Assert.DoesNotContain(result.Facts, f => f.StepId == target.Id && f.Verdict == MockStepVerdict.Pass);
    }

    [Fact]
    public async Task RunToBranchNotReachedIsExplicitlyInconclusive()
    {
        var unreachable = AssertState();
        var branch = N("If", ("variable", "mode"), ("equals", "go"))
            .WithSlot("then", unreachable).WithSlot("else");
        var doc = Document(N("Wait", ("milliseconds", "70")), Observe(), AssertState(), branch);
        var runner = new RunCoordinator();
        var running = runner.StartAsync(new PlanCompiler().Prepare(doc), new MockStation());
        runner.RunTo(unreachable.Id);
        var result = await running;
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.Type == "RunToNotReached" && f.StepId == unreachable.Id);
    }

    [Fact]
    public async Task UnknownOrCleanupTargetsCannotBeDebugControlled()
    {
        var doc = Document(N("Wait", ("milliseconds", "70")), Observe(), AssertState());
        var cleanup = N("SetVariable", ("variable", "cleanup"), ("value", "yes"));
        var tx = new StepEditTransaction(doc);
        tx.Insert(TestPhase.Cleanup, null, null, 0, cleanup);
        var runner = new RunCoordinator();
        var running = runner.StartAsync(new PlanCompiler().Prepare(tx.Current), new MockStation());
        Assert.Throws<ArgumentException>(() => runner.RunTo(StepId.New()));
        Assert.Throws<ArgumentException>(() => runner.Skip(cleanup.Id));
        runner.RequestStop();
        var result = await running;
        Assert.True(result.Stopped);
        Assert.Contains(result.Facts, f => f.StepId == cleanup.Id && f.Detail == "Cleanup");
    }

    [Fact]
    public async Task BreakpointPausesBeforeTargetAndStepReleasesExactlyOneBoundary()
    {
        var target = AssertState();
        var doc = Document(Observe(), target);
        var runner = new RunCoordinator();
        runner.SetBreakpoint(target.Id, true);
        var waiting = new TaskCompletionSource<StepId>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.BoundaryPaused += id => { waiting.TrySetResult(id); };
        var running = runner.StartAsync(new PlanCompiler().Prepare(doc), new MockStation());
        Assert.Equal(target.Id, await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(running.IsCompleted);
        runner.Step();
        Assert.Equal(MockStepVerdict.Pass, (await running).Verdict);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task ThrowingTraceObserverCannotChangeBusinessVerdict()
    {
        var runner = new RunCoordinator();
        runner.FactProduced += _ => throw new Exception("Diagnostic window closed");
        var result = await runner.StartAsync(new PlanCompiler().Prepare(
            Document(Observe(), AssertState())), new MockStation());
        Assert.Equal(MockStepVerdict.Pass, result.Verdict);
    }

    [Fact]
    public async Task RedactedAndUnredactedBundlesAreExplicitAndExportEscapesCsv()
    {
        var doc = Document(Observe("secret,\"value\""), AssertState("different"));
        var runner = new RunCoordinator();
        var events = new List<TestDiagnosticEvent>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        runner.FactProduced += f => events.Add(TestDiagnosticArchive.FromFact("pending", "Test", f,
            monotonicOffsetMs: watch.ElapsedMilliseconds));
        var report = await runner.StartAsync(new PlanCompiler().Prepare(doc), new MockStation());
        Assert.Equal(MockStepVerdict.Fail, report.Verdict);
        Assert.Contains(events, e => e.ErrorCode == "ASSERT_MISMATCH");
        var safe = TestDiagnosticArchive.Build(doc, report.PlanFingerprint, report, events);
        Assert.True(safe.ValuesRedacted);
        Assert.Equal("Mock", safe.SourceMode);
        Assert.Equal("DISABLED", safe.PhysicalPermission);
        Assert.Equal("IN_MEMORY_ONLY_NOT_DURABLE", safe.EvidenceCompleteness);
        Assert.DoesNotContain("secret,", TestDiagnosticArchive.ToJson(safe));
        Assert.Contains("[REDACTED]", TestDiagnosticArchive.ToJson(safe));
        var raw = TestDiagnosticArchive.Build(doc, report.PlanFingerprint, report, events,
            hostBuildGitSha: "not-built", projectRevision: "UNBOUND", redactValues: false);
        Assert.False(raw.ValuesRedacted);
        Assert.Contains("secret,", TestDiagnosticArchive.ToJson(raw));
        Assert.Contains("\"\"value\"\"", TestDiagnosticArchive.ToCsv(raw));
        Assert.Contains("MonotonicOffsetMs", TestDiagnosticArchive.ToCsv(raw));
    }
}
