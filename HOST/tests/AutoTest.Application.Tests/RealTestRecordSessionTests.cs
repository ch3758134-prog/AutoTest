using System.Collections.Immutable;
using AutoTest.Application.TestRecord;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Application.Tests;

/// <summary>
/// Offline adapter contract tests. The fixtures are synthetic and MUST NOT be
/// used as any evidence of real ECU behavior, physical safety or production PASS.
/// </summary>
public sealed class RealTestRecordSessionTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "AutoTest-TestRecord-Contract-" + Guid.NewGuid().ToString("N"));
        public RecordDatabase Database { get; }
        public RunRecordWriter Writer { get; }
        public RunReportExporter Reports { get; }
        public string ReportFolder => Path.Combine(_root, "reports");
        public Fixture()
        {
            Database = new RecordDatabase(Path.Combine(_root, "runs.db"));
            Writer = new RunRecordWriter(Database);
            Reports = new RunReportExporter(Database,
                new ArtifactRepository(Path.Combine(_root, "objects")));
        }
        public RunRecord Record(PreparedTestPlan plan)
        {
            var project = new ProjectSnapshot(ProjectId.New(), new ProjectRevision(1),
                RevisionFingerprint.FromSha256(new string('a', 64)), ImmutableArray<AssetRef>.Empty, 1);
            var station = new StationProfile(StationId.Create("UNIT-TEST-STATION"),
                new BusRoleMap(RevisionFingerprint.FromSha256(new string('b', 64)), 1, 0),
                "UNIT-TEST-NO-PHYSICAL-VOLTAGE", RevisionFingerprint.FromSha256(new string('c', 64)));
            var snapshot = new RunSnapshot(RunId.New(), project, station,
                RevisionFingerprint.FromSha256(plan.Fingerprint), ImmutableArray<AssetRef>.Empty);
            return RunRecord.CreateFromSnapshot(snapshot,
                new SessionIdentity("UNIT-TEST-DUT", new string('d', 64), "UNIT-TEST-NOT-SCAN"),
                RecordRunMode.Development, DateTimeOffset.UtcNow, "UNIT-TEST-BUILD");
        }
        public void Dispose()
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) { /* best effort */ }
        }
    }
    private sealed class Signals : ITestSignalPort
    {
        private readonly TestSignalResult _result;
        public Signals(TestSignalResult result) => _result = result;
        public Task<TestSignalResult> ReadAsync(TestTargetMapping target, TestReadMode mode,
            TimeSpan timeout, TimeSpan maxAge, CancellationToken ct) => Task.FromResult(_result);
        public async IAsyncEnumerable<TestSignalResult> ObserveAsync(TestTargetMapping target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _result;
            await Task.CompletedTask;
        }
    }
    private sealed class Actions : ITestActuationPort
    {
        public int Writes;
        public int CleanupCalls;
        public ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target,
            string action, CancellationToken ct)
        {
            Writes++;
            return ValueTask.FromResult(new TestOperationResult(true, false,
                "ACK_ONLY", "Not DUT feedback", DateTimeOffset.UtcNow));
        }
        public ValueTask CleanupAsync(CancellationToken ct)
        {
            CleanupCalls++;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class Waves : ITestWavePort
    {
        public Task StartAsync(TestTargetMapping target, string key, int ms,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<TestWaveResult> StopAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<TestWaveResult> AnalyzeAsync(string key, string reference, double tolerance,
            double coverage, CancellationToken ct) => throw new NotSupportedException();
        public Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (TestTargetMapping Mapping, TestSignalResult Sample) Signal(double? actual,
        TestSignalStatus status = TestSignalStatus.Valid)
    {
        var target = new TestTargetMapping("EcuStatus", TestTargetKind.Signal,
            AssetId.New(), 1, new string('f', 64), BusRole.DutCan, 0x123,
            SignalName: "Status", Unit: "count");
        var source = new SignalRef(new AssetRef(target.Asset,
            AssetRevision.Create(target.AssetRevision),
            RevisionFingerprint.FromSha256(target.AssetSha256)), target.Role,
            target.CanId, target.SignalName, target.Unit);
        var observation = actual is null ? null : new SignalObservation(source, actual, "count",
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 42), EvidenceQuality.HostObserved,
            EvidenceRef.New(), status == TestSignalStatus.LostFrames);
        return (target, new TestSignalResult(status, observation, "UNIT_TEST_SIGNAL"));
    }
    private static PreparedTestPlan Plan(TestTargetMapping mapping, string expected,
        bool twoLoops = false)
    {
        var test = StepNode.Create("AssertSignal")
            .WithProperty("target", mapping.Name)
            .WithProperty("operator", "==")
            .WithProperty("expected", expected)
            .WithProperty("timeoutMs", "30");
        var doc = TestDocument.Create("Unit test DBC assertion") with
        { Mappings = ImmutableArray.Create(mapping) };
        if (twoLoops)
            test = StepNode.Create("Loop").WithProperty("count", "2")
                .WithSlot("body", test);
        doc = doc with
        {
            Phases = doc.Phases.SetItem(TestPhase.Test, ImmutableArray.Create(test))
        };
        var prepared = new PlanCompiler().Prepare(doc, allowRealOperations: true);
        Assert.Empty(prepared.Diagnostics);
        return prepared;
    }

    [Fact]
    public async Task DatabaseAndReportsReflectSameExecutedOccurrences()
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(2);
        var prepared = Plan(mapping, "2", twoLoops: true);
        var record = fixture.Record(prepared);
        var session = new RealTestRecordSession(record, fixture.Writer, fixture.Reports);
        var actions = new Actions();
        var completed = await session.ExecuteAsync(
            new RealRunCoordinator(new Signals(sample), actions, new Waves()),
            prepared, fixture.ReportFolder);
        Assert.Equal(RealTestVerdict.Passed, completed.Result.Verdict);
        Assert.Equal(record.RunId, completed.Result.Run);
        Assert.True(File.Exists(completed.Reports.HtmlPath));
        Assert.True(File.Exists(completed.Reports.JsonPath));
        Assert.True(File.Exists(completed.Reports.CsvPath));
        Assert.Equal(0, actions.Writes);
        Assert.Equal(1, actions.CleanupCalls);

        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId);
        Assert.NotNull(history);
        Assert.Equal(RecordRunMode.Development, history.Mode);
        Assert.Equal(RecordPersistenceState.Committed, history.Persistence);
        Assert.Equal(Verdict.Pass, history.Verdict);
        var steps = await fixture.Database.ReadStepFactsAsync(record.RunId);
        var assertions = steps.Where(x => x.Disposition == nameof(RealStepStatus.Completed) &&
            x.StepOccurrenceId is not null && x.Unit == "count").ToArray();
        Assert.Equal(2, assertions.Length);
        Assert.NotEqual(assertions[0].StepOccurrenceId, assertions[1].StepOccurrenceId);
        Assert.Equal(new[] { 1, 2 }, assertions.Select(x => x.LoopIteration!.Value));
        Assert.All(assertions, step =>
        {
            Assert.Equal("2", step.Expected);
            Assert.Equal("2", step.Actual);
            Assert.Equal(EvidenceQuality.HostObserved, step.Quality);
            Assert.Single(step.Evidence);
        });
        var raw = await fixture.Database.ReadFactsAsync(new RecordQuery(record.RunId, 100));
        Assert.Contains(raw, f => f.Description.StartsWith("STEP_BEGIN", StringComparison.Ordinal));
        Assert.Contains(raw, f => f.Description.StartsWith("STEP_END", StringComparison.Ordinal));
        Assert.Contains(raw, f => f.Description == "RUN_TERMINAL Passed");
        var html = await File.ReadAllTextAsync(completed.Reports.HtmlPath);
        Assert.Contains(assertions[0].StepOccurrenceId!.Value.ToString("D"), html);
        Assert.Contains(assertions[1].StepOccurrenceId!.Value.ToString("D"), html);
    }

    [Theory]
    [InlineData(2, "4", "Failed", "Failed", "Fail")]
    [InlineData(null, "2", "Inconclusive", "Unknown", "Inconclusive")]
    public async Task NonPassIsPersistedAndReported(
        double? sampleValue, string expected, string testOutcome,
        string terminal, string verdict)
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(sampleValue, sampleValue is null
            ? TestSignalStatus.NoFrame : TestSignalStatus.Valid);
        var plan = Plan(mapping, expected);
        var record = fixture.Record(plan);
        var done = await new RealTestRecordSession(record, fixture.Writer, fixture.Reports)
            .ExecuteAsync(new RealRunCoordinator(new Signals(sample), new Actions(), new Waves()),
                plan, fixture.ReportFolder);
        Assert.Equal(testOutcome, done.Result.Verdict.ToString());
        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId);
        Assert.NotNull(history);
        Assert.Equal(terminal, history.TerminalState);
        Assert.Equal(verdict, history.Verdict?.ToString());
        Assert.Equal(RecordPersistenceState.Committed, history.Persistence);
        Assert.NotEmpty(await fixture.Database.ReadStepFactsAsync(record.RunId));
        Assert.True(File.Exists(done.Reports.HtmlPath));
    }

    [Fact]
    public async Task PlanFingerprintMismatchCannotBeginRecordOrOperateEquipment()
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(2);
        var plan = Plan(mapping, "2");
        var different = Plan(mapping, "5");
        var record = fixture.Record(plan);
        var actions = new Actions();
        var session = new RealTestRecordSession(record, fixture.Writer, fixture.Reports);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ExecuteAsync(new RealRunCoordinator(new Signals(sample), actions, new Waves()),
                different, fixture.ReportFolder));
        Assert.Equal(0, actions.Writes);
        Assert.Null(await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId));
    }
}
