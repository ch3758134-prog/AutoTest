using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class RealExecutionTests
{
    private sealed class SignalPort : ITestSignalPort
    {
        private readonly TestSignalResult _data;
        public SignalPort(TestSignalResult data) => _data = data;
        public Task<TestSignalResult> ReadAsync(TestTargetMapping target, TestReadMode mode,
            TimeSpan timeout, TimeSpan maxAge, CancellationToken ct) => Task.FromResult(_data);
        public async IAsyncEnumerable<TestSignalResult> ObserveAsync(TestTargetMapping target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return _data;
            await Task.CompletedTask;
        }
    }
    private sealed class ActuationPort : ITestActuationPort
    {
        public int Written;
        public bool CleanupCalled;
        public ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target,
            string action, CancellationToken ct)
        {
            Written++;
            return ValueTask.FromResult(new TestOperationResult(true, false,
                "ACK", "Not ECU feedback", DateTimeOffset.UtcNow));
        }
        public ValueTask CleanupAsync(CancellationToken ct)
        {
            CleanupCalled = true;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class WavePort : ITestWavePort
    {
        public Task StartAsync(TestTargetMapping t, string key, int durationMs, CancellationToken ct)
            => Task.CompletedTask;
        public Task<TestWaveResult> StopAsync(string key, CancellationToken ct)
            => Task.FromResult(new TestWaveResult(true, "CAPTURED", "3", "ref", ""));
        public Task<TestWaveResult> AnalyzeAsync(string key, string reference,
            double tolerance, double minimumCoverage, CancellationToken ct)
            => Task.FromResult(new TestWaveResult(true, "FAIL", "Fail", "evidence", ""));
        public TestTargetMapping? SelectedProfile { get; private set; }
        public Task<TestWaveResult> AnalyzeWithProfileAsync(string key, TestTargetMapping profile,
            string reference, CancellationToken ct)
        {
            SelectedProfile = profile;
            return Task.FromResult(new TestWaveResult(true, "FAIL", "Fail", "profile-evidence",
                "Analysis result is data, not a test verdict."));
        }
        public Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static TestTargetMapping Target() => new("EcuStatus", TestTargetKind.Signal,
        AssetId.New(), 1, new string('a', 64), BusRole.DutCan, 0x123,
        SignalName: "Status", Unit: "count");

    private static TestSignalResult Signal(TestTargetMapping target, double value)
    {
        var signal = new SignalRef(new AssetRef(target.Asset,
            AssetRevision.Create(target.AssetRevision),
            RevisionFingerprint.FromSha256(target.AssetSha256)),
            target.Role, target.CanId, target.SignalName, target.Unit);
        return new(TestSignalStatus.Valid, new SignalObservation(signal, value, target.Unit,
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 120),
            EvidenceQuality.Derived, EvidenceRef.New(), false), "OK");
    }
    private static StepNode N(string type, params (string key, string value)[] fields)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in fields) node = node.WithProperty(key, value);
        return node;
    }

    [Fact]
    public async Task RealReadIsDataAndOnlyExplicitExpectCreatesVerdict()
    {
        var mapping = Target();
        var doc = TestDocument.Create("Real") with
        { Mappings = ImmutableArray.Create(mapping) };
        var tx = new StepEditTransaction(doc);
        tx.Insert(TestPhase.Test, null, null, 0, N("Read", ("target", "EcuStatus"),
            ("variable", "response"), ("mode", "Latest"),
            ("timeoutMs", "1000"), ("maxAgeMs", "500")));
        var actions = new ActuationPort();
        var wave = new WavePort();
        var plan = new PlanCompiler().Prepare(tx.Current, allowRealOperations: true);
        Assert.Empty(plan.Diagnostics);
        var dataOnly = await new RealRunCoordinator(new SignalPort(Signal(mapping, 2)),
            actions, wave).StartAsync(plan);
        Assert.Equal(RealTestVerdict.Inconclusive, dataOnly.Verdict);
        Assert.Equal("2", dataOnly.Variables["response"].Value);
        tx.Insert(TestPhase.Test, null, null, 1, N("Expect",
            ("left", "$response"), ("operator", "=="), ("right", "2")));
        var full = await new RealRunCoordinator(new SignalPort(Signal(mapping, 2)),
            actions, wave).StartAsync(new PlanCompiler().Prepare(tx.Current, allowRealOperations: true));
        Assert.Equal(RealTestVerdict.Passed, full.Verdict);
        Assert.True(actions.CleanupCalled);
    }

    [Fact]
    public async Task RealExecutionDoesNotTurnMissingFrameIntoFalseComparison()
    {
        var mapping = Target();
        var tx = new StepEditTransaction(TestDocument.Create("Missing") with
        { Mappings = ImmutableArray.Create(mapping) });
        tx.Insert(TestPhase.Test, null, null, 0, N("Read", ("target", "EcuStatus"),
            ("variable", "response"), ("mode", "Next"),
            ("timeoutMs", "100"), ("maxAgeMs", "100")));
        tx.Insert(TestPhase.Test, null, null, 1, N("Expect",
            ("left", "$response"), ("operator", "=="), ("right", "2")));
        var plan = new PlanCompiler().Prepare(tx.Current, allowRealOperations: true);
        var result = await new RealRunCoordinator(new SignalPort(
            new TestSignalResult(TestSignalStatus.NoFrame, null, "Missing CAN")), new ActuationPort(),
            new WavePort()).StartAsync(plan);
        Assert.Equal(RealTestVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Facts, x => x.Status == RealStepStatus.NoData);
    }

    [Fact]
    public void RealPlanRejectsMockObservationEvenWhenMockPlanWouldAccept()
    {
        var tx = new StepEditTransaction(TestDocument.Create("Legacy"));
        tx.Insert(TestPhase.Test, null, null, 0, N("MockObserve",
            ("variable", "state"), ("value", "1"), ("quality", "Observed")));
        Assert.Empty(new PlanCompiler().Prepare(tx.Current).Diagnostics);
        Assert.NotEmpty(new PlanCompiler().Prepare(tx.Current, allowRealOperations: true).Diagnostics);
    }
    [Fact]
    public async Task PinnedWaveProfileAnalysis_ProducesDataUntilExplicitExpect()
    {
        var profile = new TestTargetMapping("MyWaveCriteria", TestTargetKind.WaveProfile,
            AssetId.New(), 2, new string('b', 64));
        var tx = new StepEditTransaction(TestDocument.Create("Wave") with
        { Mappings = ImmutableArray.Create(profile) });
        tx.Insert(TestPhase.Test, null, null, 0, N("WaveAnalyze",
            ("captureKey", "capture1"), ("reference", "approved-reference"),
            ("profile", profile.Name), ("variable", "analysis"),
            ("tolerance", "0.5"), ("coverage", "0.90")));
        var prepared = new PlanCompiler().Prepare(tx.Current, allowRealOperations: true);
        Assert.Empty(prepared.Diagnostics);
        var wave = new WavePort();
        var signals = new SignalPort(new TestSignalResult(TestSignalStatus.NoFrame, null, "No data"));
        var observed = await new RealRunCoordinator(signals, new ActuationPort(), wave)
            .StartAsync(prepared);
        Assert.Equal(RealTestVerdict.Inconclusive, observed.Verdict);
        Assert.Equal("Fail", observed.Variables["analysis"].Value);
        Assert.Equal(profile, wave.SelectedProfile);
        tx.Insert(TestPhase.Test, null, null, 1, N("Expect",
            ("left", "$analysis"), ("operator", "=="), ("right", "Fail")));
        var decided = await new RealRunCoordinator(signals, new ActuationPort(), new WavePort())
            .StartAsync(new PlanCompiler().Prepare(tx.Current, allowRealOperations: true));
        Assert.Equal(RealTestVerdict.Passed, decided.Verdict);
    }

    [Fact]
    public void WaveProfileStep_RejectsUnmappedOrWrongKindAnalysisResource()
    {
        var signal = Target();
        var tx = new StepEditTransaction(TestDocument.Create("Wave") with
        { Mappings = ImmutableArray.Create(signal) });
        tx.Insert(TestPhase.Test, null, null, 0, N("WaveAnalyze",
            ("captureKey", "capture1"), ("reference", "reference"),
            ("profile", signal.Name), ("variable", "analysis"),
            ("tolerance", "0.5"), ("coverage", "0.90")));
        Assert.Contains(new PlanCompiler().Prepare(tx.Current, allowRealOperations: true).Diagnostics,
            x => x.Contains("unmapped wave analysis profile", StringComparison.Ordinal));
    }


}
