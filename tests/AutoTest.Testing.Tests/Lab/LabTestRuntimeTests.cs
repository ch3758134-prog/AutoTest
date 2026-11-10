using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Testing.Tests.Lab;

public sealed class LabTestRuntimeTests
{
    private static LabNode N(string type, params (string key, string value)[] arguments)
    {
        var n = LabNode.New(type);
        foreach (var (key, value) in arguments) n.Parameters.Add(key, value);
        return n;
    }
    private static LabNode Observe(string value = "ok") => N("MockObserve",
        ("variable", "signal"), ("value", value), ("quality", "Observed"));
    private static LabNode AssertSignal(string value = "ok") =>
        N("Assert", ("variable", "signal"), ("equals", value));
    private static LabDocument Simple()
    {
        var d = LabDocument.New();
        d.Phases[LabPhase.Test].Add(Observe());
        d.Phases[LabPhase.Test].Add(AssertSignal());
        d.Phases[LabPhase.Cleanup].Add(N("Log", ("message", "done")));
        return d;
    }
    private static LabRunCoordinator Runner() => new();
    private static Task<LabRunReport> Run(LabDocument doc) =>
        Runner().RunAsync(new LabPlanCompiler().Prepare(doc), new SimulatedLabStation(), LabSource.Mock);

    [Fact]
    public void EditUndoRedoAndFailedMoveAreAtomic()
    {
        var editor = new LabDocumentEditor(Simple());
        var node = editor.Snapshot.Phases[LabPhase.Test][0];
        editor.Copy(node.Id, LabPhase.Test, null, null, 1);
        Assert.Equal(3, editor.Snapshot.Phases[LabPhase.Test].Count);
        Assert.Equal(3, editor.Snapshot.Phases[LabPhase.Test].Select(n => n.Id).Distinct().Count());
        editor.Undo(); Assert.Equal(2, editor.Snapshot.Phases[LabPhase.Test].Count);
        editor.Redo(); Assert.Equal(3, editor.Snapshot.Phases[LabPhase.Test].Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Move(node.Id, LabPhase.Test, null, null, 99));
        Assert.Equal(3, editor.Snapshot.Phases[LabPhase.Test].Count);
        var snapshot = editor.Snapshot;
        snapshot.Phases[LabPhase.Test].Clear();
        Assert.Equal(3, editor.Snapshot.Phases[LabPhase.Test].Count);
    }
    [Fact]
    public void SaveOpenVersionAndIdsAreStable()
    {
        var doc = Simple();
        var opened = LabDocumentCodec.Open(LabDocumentCodec.Save(doc));
        Assert.Equal(doc.Id, opened.Id);
        Assert.Equal(doc.Walk().Select(n => n.Id), opened.Walk().Select(n => n.Id));
        var bad = LabDocumentCodec.Save(doc).Replace("\"FormatVersion\": 1", "\"FormatVersion\": 999");
        Assert.Throws<NotSupportedException>(() => LabDocumentCodec.Open(bad));
    }
    [Fact]
    public async Task ControllerRevisionMustBeExactAndPhysicalFenceIsClosed()
    {
        var doc = Simple();
        doc.Phases[LabPhase.Test].Insert(0, N("ControllerState", ("controllerId", "custom"),
            ("stateId", "neutral"), ("revision", "r1")));
        Assert.False(new LabPlanCompiler().Prepare(doc).Ready);
        Assert.False(new LabPlanCompiler(new Catalog("r2")).Prepare(doc).Ready);
        var plan = new LabPlanCompiler(new Catalog("r1")).Prepare(doc);
        Assert.True(plan.Ready);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner().RunAsync(plan, new SimulatedLabStation(), LabSource.Physical));
    }
    private sealed class Catalog(string revision) : ILabControllerCatalog
    {
        public LabControllerResolution Resolve(LabControllerRef value) =>
            new(value.ControllerId == "custom" && value.StateId == "neutral", revision);
    }
    [Theory]
    [InlineData("ok", LabVerdict.Pass)]
    [InlineData("wrong", LabVerdict.Fail)]
    public async Task AssertSeparatesActualAndExpected(string actual, LabVerdict verdict)
    {
        var doc = Simple();
        doc.Phases[LabPhase.Test][0].Parameters["value"] = actual;
        var result = await Run(doc);
        Assert.Equal(verdict, result.Verdict);
        var assertion = Assert.Single(result.Facts.Where(f => f.NodeType == "Assert"));
        Assert.Equal("ok", assertion.Expected);
        Assert.Equal(actual, assertion.Actual);
        Assert.Equal(LabSource.Mock, assertion.Source);
        Assert.False(result.PhysicalProductionPass);
    }
    [Fact]
    public async Task AuditFactsCarryOperationCorrelationSourceAndSession()
    {
        var audit = new InMemoryLabOperationAuditSink();
        var coordinator = new LabRunCoordinator(audit);
        var result = await coordinator.RunAsync(new LabPlanCompiler().Prepare(Simple()),
            new SimulatedLabStation(), LabSource.Mock);
        Assert.NotEmpty(audit.Facts);
        Assert.Equal(result.Facts.Length, audit.Facts.Count);
        Assert.Single(audit.Facts.Select(x => x.CorrelationId).Distinct());
        Assert.All(audit.Facts, f =>
        {
            Assert.NotEqual(Guid.Empty, f.OperationId);
            Assert.Equal(result.RunId, f.RunId);
            Assert.Equal(LabSource.Mock, f.Source);
            Assert.True(f.SessionGeneration > 0);
        });
    }
    [Fact]
    public async Task NoVerifiedAssertionCannotPass()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Test].Add(N("SetVariable", ("variable", "x"), ("value", "1")));
        Assert.Equal(LabVerdict.Inconclusive, (await Run(doc)).Verdict);
    }
    [Fact]
    public async Task IfSwitchLoopWhenAndSubroutineExecuteDeterministically()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Prepare].Add(N("SetVariable", ("variable", "mode"), ("value", "go")));
        var choice = N("Switch", ("variable", "mode"));
        var yes = N("Case", ("equals", "go"));
        var otherwise = N("Default");
        yes.Slots.Add("body", new List<LabNode> { Observe() });
        otherwise.Slots.Add("body", new List<LabNode> { N("Fail", ("message", "wrong switch")) });
        choice.Slots.Add("cases", new List<LabNode> { yes, otherwise });
        doc.Phases[LabPhase.Test].Add(choice);
        var condition = N("If", ("variable", "mode"), ("equals", "go"));
        condition.Slots.Add("then", new List<LabNode> { AssertSignal() });
        condition.Slots.Add("else", new List<LabNode>());
        doc.Phases[LabPhase.Test].Add(condition);
        var when = N("When", ("variable", "mode"), ("equals", "go"), ("timeoutMs", "50"));
        when.Slots.Add("body", new List<LabNode> { N("Log", ("message", "when reached")) });
        doc.Phases[LabPhase.Test].Add(when);
        var sub = N("Subroutine", ("name", "doit"));
        sub.Slots.Add("body", new List<LabNode> { AssertSignal() });
        doc.Phases[LabPhase.Prepare].Add(sub);
        var loop = N("Loop", ("count", "2"));
        loop.Slots.Add("body", new List<LabNode> { N("CallSubroutine", ("target", "doit")) });
        doc.Phases[LabPhase.Test].Add(loop);
        var plan = new LabPlanCompiler().Prepare(doc);
        Assert.True(plan.Ready, string.Join(";", plan.Diagnostics));
        var run = await Runner().RunAsync(plan, new SimulatedLabStation(), LabSource.Mock);
        Assert.Equal(LabVerdict.Pass, run.Verdict);
        Assert.Equal(3, run.Facts.Count(f => f.NodeType == "Assert"));
    }
    [Fact]
    public async Task TryCatchFinallyAndCleanupRecordDeniedOperation()
    {
        var doc = LabDocument.New();
        var control = N("ControllerState", ("controllerId", "custom"),
            ("stateId", "neutral"), ("revision", "r1"));
        var attempt = N("TryCatch");
        attempt.Slots.Add("try", new List<LabNode> { control });
        attempt.Slots.Add("catch", new List<LabNode> { N("Log", ("message", "catch")) });
        attempt.Slots.Add("finally", new List<LabNode> { N("Log", ("message", "finally")) });
        doc.Phases[LabPhase.Test].Add(attempt);
        doc.Phases[LabPhase.Cleanup].Add(N("Log", ("message", "cleanup")));
        var plan = new LabPlanCompiler(new Catalog("r1")).Prepare(doc);
        Assert.True(plan.Ready, string.Join(";", plan.Diagnostics));
        var report = await Runner().RunAsync(plan, new DenyStation(), LabSource.Mock);
        Assert.Equal(LabVerdict.Error, report.Verdict);
        Assert.Contains(report.Facts, f => f.Diagnostic == "STATION_DENIED");
        Assert.Contains(report.Facts, f => f.Actual == "catch");
        Assert.Contains(report.Facts, f => f.Actual == "finally");
        Assert.Contains(report.Facts, f => f.Actual == "cleanup");
    }
    private sealed class DenyStation : ILabStation
    {
        public long SessionGeneration => 1;
        public ValueTask<LabObservation> ObserveAsync(string name, Guid run, LabSource source,
            CancellationToken cancellation) => ValueTask.FromResult(
            new LabObservation(name, null, LabQuality.Missing, source, run, 1));
        public ValueTask<LabActionReceipt> SetControllerAsync(LabControllerRef reference, Guid run,
            LabSource source, CancellationToken cancellation) =>
            ValueTask.FromResult(new LabActionReceipt(false, "STATION_DENIED"));
    }

    [Fact]
    public async Task StaleObservationFromPreviousRunCannotBeAsserted()
    {
        var station = new SimulatedLabStation();
        station.SetObservation("signal", "ok", LabQuality.Observed, LabSource.Mock, Guid.NewGuid());
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Test].Add(N("Observe", ("variable", "signal")));
        doc.Phases[LabPhase.Test].Add(AssertSignal());
        var result = await Runner().RunAsync(new LabPlanCompiler().Prepare(doc), station, LabSource.Mock);
        Assert.Equal(LabVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.Quality == LabQuality.Stale);
    }
    [Fact]
    public async Task StopStillExecutesCleanup()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Test].Add(N("Wait", ("milliseconds", "30000")));
        doc.Phases[LabPhase.Cleanup].Add(N("Log", ("message", "cleanup executed")));
        var coordinator = Runner();
        var running = coordinator.RunAsync(new LabPlanCompiler().Prepare(doc),
            new SimulatedLabStation(), LabSource.Mock);
        coordinator.Stop();
        var result = await running;
        Assert.True(result.Stopped);
        Assert.Contains(result.Facts, f => f.Phase == LabPhase.Cleanup && f.Actual == "cleanup executed");
    }
    [Fact]
    public void PreflightRejectsInvalidControlFlowAndAsset()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Test].Add(N("Case", ("equals", "x")));
        doc.Phases[LabPhase.Test].Add(N("Wait", ("milliseconds", "70000")));
        doc.Phases[LabPhase.Test].Add(N("CallSubroutine", ("target", "undefined")));
        doc.Phases[LabPhase.Test].Add(N("Observe", ("variable", "x"), ("assetId", "missing"), ("assetRevision", "v1")));
        var plan = new LabPlanCompiler().Prepare(doc);
        Assert.False(plan.Ready);
        Assert.Contains(plan.Diagnostics, x => x.Contains("Case must be a Switch child"));
        Assert.Contains(plan.Diagnostics, x => x.Contains("milliseconds must"));
        Assert.Contains(plan.Diagnostics, x => x.Contains("missing subroutine"));
        Assert.Contains(plan.Diagnostics, x => x.Contains("asset"));
    }
    [Fact]
    public async Task ReplayConsumesStoredTraceButNeverAPhysicalPass()
    {
        var doc = LabDocument.New();
        doc.Phases[LabPhase.Test].Add(N("Observe", ("variable", "signal")));
        doc.Phases[LabPhase.Test].Add(AssertSignal());
        var replay = new RecordedReplayLabStation(new[]
        {
            new LabReplaySample("signal", "ok", LabQuality.Observed, "trace:sample-001")
        });
        var result = await Runner().RunAsync(new LabPlanCompiler().Prepare(doc), replay, LabSource.Replay);
        Assert.Equal(LabVerdict.Pass, result.Verdict);
        Assert.All(result.Facts, fact => Assert.Equal(LabSource.Replay, fact.Source));
        Assert.False(result.PhysicalProductionPass);
        Assert.Contains(result.Facts, f => f.TraceReference == "trace:sample-001");
    }
    [Fact]
    public async Task ReplayRejectsMockOnlyObservation()
    {
        var plan = new LabPlanCompiler().Prepare(Simple());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner().RunAsync(plan, new RecordedReplayLabStation(Array.Empty<LabReplaySample>()), LabSource.Replay));
    }
    [Fact]
    public async Task SessionReconnectInvalidatesEarlierObservation()
    {
        var doc = Simple();
        var station = new SimulatedLabStation();
        var runner = Runner();
        runner.FactProduced += f => { if (f.NodeType == "MockObserve") station.Reconnect(); };
        var result = await runner.RunAsync(new LabPlanCompiler().Prepare(doc), station, LabSource.Mock);
        Assert.Equal(LabVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.NodeType == "Assert" && f.Quality == LabQuality.Stale);
    }
    [Fact]
    public async Task SkipSelectedStepDoesNotSilentlyProducePass()
    {
        var doc = Simple();
        doc.Phases[LabPhase.Test].Insert(0, N("Wait", ("milliseconds", "100")));
        var skipId = doc.Phases[LabPhase.Test].Last().Id;
        var runner = Runner();
        var running = runner.RunAsync(new LabPlanCompiler().Prepare(doc), new SimulatedLabStation(), LabSource.Mock);
        runner.Skip(skipId);
        var result = await running;
        Assert.Equal(LabVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, f => f.StepId == skipId && f.Verdict == LabVerdict.Skipped);
    }
    [Fact]
    public async Task RunToPausesBeforeTargetAndResumeExecutesIt()
    {
        var doc = Simple();
        doc.Phases[LabPhase.Test].Insert(0, N("Wait", ("milliseconds", "100")));
        var target = doc.Phases[LabPhase.Test].Last().Id;
        var runner = Runner();
        var running = runner.RunAsync(new LabPlanCompiler().Prepare(doc), new SimulatedLabStation(), LabSource.Mock);
        runner.RunTo(target);
        await Task.Delay(160);
        Assert.False(running.IsCompleted);
        runner.Resume();
        Assert.Equal(LabVerdict.Pass, (await running).Verdict);
    }

    [Fact]
    public void RecursiveSubroutineIsRejected()
    {
        var doc = LabDocument.New();
        var sub = N("Subroutine", ("name", "a"));
        sub.Slots.Add("body", new List<LabNode> { N("CallSubroutine", ("target", "a")) });
        doc.Phases[LabPhase.Prepare].Add(sub);
        Assert.Contains(new LabPlanCompiler().Prepare(doc).Diagnostics, x => x.Contains("Recursive subroutine"));
    }
    [Fact]
    public async Task PreparedSnapshotIsIsolatedFromEditor()
    {
        var doc = Simple();
        var plan = new LabPlanCompiler().Prepare(doc);
        doc.Phases[LabPhase.Test][1].Parameters["equals"] = "modified";
        Assert.NotEqual(plan.Digest, new LabPlanCompiler().Prepare(doc).Digest);
        Assert.Equal(LabVerdict.Pass, (await Runner().RunAsync(plan, new SimulatedLabStation(), LabSource.Mock)).Verdict);
    }
    [Fact]
    public async Task IfAndSwitchFollowObservedSignalRatherThanOnlyLocalVariables()
    {
        var doc = LabDocument.New("Signal decisions");
        doc.Phases[LabPhase.Test].Add(N("Observe", ("variable", "signal")));
        var decision = N("If", ("variable", "signal"), ("equals", "ok"));
        decision.Slots.Add("then", new List<LabNode> { N("Log", ("message", "real observation selected")) });
        decision.Slots.Add("else", new List<LabNode> { N("Fail", ("message", "unexpected ECU response")) });
        doc.Phases[LabPhase.Test].Add(decision);
        var choice = N("Switch", ("variable", "signal"));
        var matched = N("Case", ("equals", "ok"));
        matched.Slots.Add("body", new List<LabNode> { AssertSignal("ok") });
        choice.Slots.Add("cases", new List<LabNode> { matched });
        doc.Phases[LabPhase.Test].Add(choice);

        var replay = new RecordedReplayLabStation(new[]
        {
            new LabReplaySample("signal", "ok", LabQuality.Observed, "recorded-real-can:23")
        });
        var report = await Runner().RunAsync(new LabPlanCompiler().Prepare(doc),
            replay, LabSource.Replay);
        Assert.Equal(LabVerdict.Pass, report.Verdict);
        Assert.Contains(report.Facts, x => x.NodeType == "If" &&
            x.Actual == "ok" && x.TraceReference == "recorded-real-can:23");
        Assert.Contains(report.Facts, x => x.NodeType == "Switch" && x.Actual == "ok");
        Assert.Contains(report.Facts, x => x.NodeType == "Assert" && x.Verdict == LabVerdict.Pass);
    }

    [Fact]
    public async Task WhenPerformsFreshSignalObservationAndExecutesItsOwnBody()
    {
        var doc = LabDocument.New("Wait actual signal");
        var when = N("When", ("variable", "signal"), ("equals", "ok"), ("timeoutMs", "500"));
        when.Slots.Add("body", new List<LabNode> { AssertSignal("ok") });
        doc.Phases[LabPhase.Test].Add(when);
        var station = new RecordedReplayLabStation(new[]
        {
            new LabReplaySample("signal", "ok", LabQuality.Observed, "recorded-real-can:29")
        });
        var report = await Runner().RunAsync(new LabPlanCompiler().Prepare(doc),
            station, LabSource.Replay);
        Assert.Equal(LabVerdict.Pass, report.Verdict);
        Assert.Contains(report.Facts, x => x.NodeType == "When" &&
            x.TraceReference == "recorded-real-can:29");
        Assert.Contains(report.Facts, x => x.NodeType == "Assert" && x.Verdict == LabVerdict.Pass);
    }

    [Fact]
    public async Task WhenMissingSignalTimesOutInconclusiveWithoutFakeSuccessfulBranch()
    {
        var doc = LabDocument.New("Missing ECU evidence");
        var when = N("When", ("variable", "signal"), ("equals", "ok"), ("timeoutMs", "40"));
        when.Slots.Add("body", new List<LabNode>
        {
            N("Fail", ("message", "should never execute without a received signal"))
        });
        doc.Phases[LabPhase.Test].Add(when);
        var missing = new RecordedReplayLabStation(Array.Empty<LabReplaySample>());
        var report = await Runner().RunAsync(new LabPlanCompiler().Prepare(doc),
            missing, LabSource.Replay);
        Assert.Equal(LabVerdict.Inconclusive, report.Verdict);
        Assert.DoesNotContain(report.Facts, x => x.NodeType == "Fail");
        Assert.Contains(report.Facts, x => x.NodeType == "When" &&
            x.Diagnostic == "WHEN_TIMEOUT_OR_CONDITION_NOT_MET");
    }

}
