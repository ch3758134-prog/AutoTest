using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Contract tests use in-memory test ports only; never claimed as ECU acceptance.</summary>
public sealed class RealOccurrenceTests
{
    private sealed class Signals(params TestSignalResult[] samples) : ITestSignalPort
    {
        public Task<TestSignalResult> ReadAsync(TestTargetMapping target, TestReadMode mode,
            TimeSpan timeout, TimeSpan maxAge, CancellationToken ct) =>
            Task.FromResult(samples.Length == 0
                ? new TestSignalResult(TestSignalStatus.NoFrame, null, "No data")
                : samples[0]);

        public async IAsyncEnumerable<TestSignalResult> ObserveAsync(TestTargetMapping target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var sample in samples)
            {
                ct.ThrowIfCancellationRequested();
                yield return sample;
            }
            await Task.CompletedTask;
        }
    }

    private sealed class Actions : ITestActuationPort
    {
        public int Writes;
        public int CleanupCalls;
        public ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target, string action,
            CancellationToken ct)
        {
            Writes++;
            return ValueTask.FromResult(new TestOperationResult(true, false, "DEVICE_ACK",
                "Not proof of DUT state", DateTimeOffset.UtcNow));
        }
        public ValueTask CleanupAsync(CancellationToken ct)
        {
            CleanupCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Waves : ITestWavePort
    {
        public Task StartAsync(TestTargetMapping t, string key, int ms, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<TestWaveResult> StopAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<TestWaveResult> AnalyzeAsync(string key, string reference, double tolerance,
            double coverage, CancellationToken ct) => throw new NotSupportedException();
        public Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Collector : IRealRunEventSink
    {
        public readonly List<RealStepStarted> Starts = [];
        public readonly List<RealStepFact> Ends = [];
        public readonly List<string> Order = [];
        public bool RejectStart;
        public ValueTask StepStartedAsync(RealStepStarted started, CancellationToken ct)
        {
            if (RejectStart) throw new IOException("Durable step begin rejected");
            Starts.Add(started);
            Order.Add("start:" + started.StepOccurrenceId);
            return ValueTask.CompletedTask;
        }
        public ValueTask StepCompletedAsync(RunId run, RealStepFact completed, CancellationToken ct)
        {
            Ends.Add(completed);
            Order.Add("end:" + completed.StepOccurrenceId);
            return ValueTask.CompletedTask;
        }
        public ValueTask RunCompletedAsync(RealRunResult completed, CancellationToken ct)
        {
            Order.Add("run:" + completed.Verdict);
            return ValueTask.CompletedTask;
        }
    }

    private static PreparedTestPlan Prepare(TestDocument document)
    {
        var plan = new PlanCompiler().Prepare(document, allowRealOperations: true);
        Assert.Empty(plan.Diagnostics);
        return plan;
    }

    private static TestDocument WithTest(TestDocument doc, params StepNode[] steps) =>
        doc with { Phases = doc.Phases.SetItem(TestPhase.Test, steps.ToImmutableArray()) };

    [Fact]
    public async Task LoopReusesStepIdButEmitsDistinctOccurrencesWithCapture()
    {
        var set = StepNode.Create("SetVariable").WithProperty("variable", "measured")
            .WithProperty("value", "2");
        var check = new StepResultRecordingOptions(ExtraResultCapture.Always,
            ImmutableArray.Create("measured"), "REQ-CAN-01", "Measured at comparison", true)
            .Apply(StepNode.Create("Expect").WithProperty("left", "$measured")
                .WithProperty("operator", "==").WithProperty("right", "2"));
        var loop = StepNode.Create("Loop").WithProperty("count", "2").WithSlot("body", set, check);
        var plan = Prepare(WithTest(TestDocument.Create("LoopTest"), loop));
        var collector = new Collector();
        var runId = RunId.New();
        var result = await new RealRunCoordinator(new Signals(), new Actions(), new Waves())
            .StartAsync(plan, runId, collector);

        Assert.Equal(runId, result.Run);
        Assert.Equal(RealTestVerdict.Passed, result.Verdict);
        var loops = result.Facts.Where(f => f.Step == check.Id).ToArray();
        var parent = Assert.Single(result.Facts.Where(f => f.Step == loop.Id));
        Assert.Equal(2, loops.Length);
        Assert.NotEqual(loops[0].StepOccurrenceId, loops[1].StepOccurrenceId);
        Assert.All(loops, f =>
        {
            Assert.Equal(parent.StepOccurrenceId, f.ParentOccurrenceId!.Value);
            Assert.Equal(TestPhase.Test, f.Phase!.Value);
            Assert.Equal("REQ-CAN-01", f.RequirementId);
            Assert.Equal("2", f.AdditionalResults["measured"].Value);
            Assert.NotNull(f.StartedUtc);
            Assert.NotNull(f.CompletedUtc);
            Assert.True(f.Duration >= TimeSpan.Zero);
        });
        Assert.Equal(new[] { 1, 2 }, loops.Select(f => f.LoopIteration!.Value));
        Assert.Equal(result.Facts.Length, collector.Ends.Count);
        Assert.EndsWith("run:Passed", collector.Order[^1]);
        Assert.Equal(result.Facts.Select(f => f.StepOccurrenceId).Distinct().Count(),
            result.Facts.Length);
    }

    [Fact]
    public void InvalidResultOptionsFailBeforeRealExecution()
    {
        var step = StepNode.Create("Expect").WithProperty("left", "1")
            .WithProperty("operator", "==").WithProperty("right", "1")
            .WithProperty("result.capture", "Off")
            .WithProperty("result.extraVariables", "voltage");
        var doc = WithTest(TestDocument.Create("InvalidCapture"), step);
        Assert.Contains(new PlanCompiler().Prepare(doc, allowRealOperations: true).Diagnostics,
            d => d.Contains("result.capture", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonDurableStepBeginPreventsPhysicalWrite()
    {
        var can = new TestTargetMapping("CAN", TestTargetKind.RawCan,
            default, 0, "", BusRole.DutCan, 0x123);
        var write = StepNode.Create("Write").WithProperty("target", "CAN")
            .WithProperty("value", "HEX:01");
        var document = WithTest(TestDocument.Create("WriteGuard") with
            { Mappings = ImmutableArray.Create(can) }, write);
        var actions = new Actions();
        var result = await new RealRunCoordinator(new Signals(), actions, new Waves())
            .StartAsync(Prepare(document), RunId.New(), new Collector { RejectStart = true });
        Assert.Equal(RealTestVerdict.Error, result.Verdict);
        Assert.Equal(0, actions.Writes);
        Assert.Equal(1, actions.CleanupCalls);
    }

    [Fact]
    public async Task CapturesRealSignalQualityAndEvidenceReference()
    {
        var target = new TestTargetMapping("EcuStatus", TestTargetKind.Signal,
            AssetId.New(), 1, new string('a', 64), BusRole.DutCan, 0x123,
            SignalName: "Status", Unit: "count");
        var reference = new SignalRef(new AssetRef(target.Asset,
            AssetRevision.Create(target.AssetRevision),
            RevisionFingerprint.FromSha256(target.AssetSha256)),
            target.Role, target.CanId, target.SignalName, target.Unit);
        var evidence = EvidenceRef.New();
        var observation = new SignalObservation(reference, 2, "count",
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 42),
            EvidenceQuality.HostObserved, evidence, false);
        var check = StepNode.Create("AssertSignal").WithProperty("target", target.Name)
            .WithProperty("operator", "==").WithProperty("expected", "2")
            .WithProperty("timeoutMs", "100");
        var doc = WithTest(TestDocument.Create("DBC") with
            { Mappings = ImmutableArray.Create(target) }, check);
        var collected = new Collector();
        var run = await new RealRunCoordinator(
            new Signals(new TestSignalResult(TestSignalStatus.Valid, observation, "OK")),
            new Actions(), new Waves())
            .StartAsync(Prepare(doc), RunId.New(), collected);
        Assert.Equal(RealTestVerdict.Passed, run.Verdict);
        var fact = Assert.Single(collected.Ends);
        Assert.Equal("count", fact.Observation?.Unit);
        Assert.Equal(evidence, fact.Observation!.Evidence);
        Assert.Equal(TestSignalStatus.Valid, fact.ObservationStatus!.Value);
        Assert.NotEqual(Guid.Empty, fact.StepOccurrenceId);
    }
}
