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
        public ArtifactRepository Artifacts { get; }
        public string ReportFolder => Path.Combine(_root, "reports");
        public Fixture()
        {
            Database = new RecordDatabase(Path.Combine(_root, "runs.db"));
            Writer = new RunRecordWriter(Database);
            Artifacts = new ArtifactRepository(Path.Combine(_root, "objects"));
            Reports = new RunReportExporter(Database, Artifacts);
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
    private sealed class EvidenceFinalizer(
        RunRecordWriter writer, ArtifactRepository artifacts, EvidenceRef evidence,
        bool reject = false) : IRealRunEvidenceFinalizer
    {
        public int Calls;
        public async ValueTask FinalizeEvidenceAsync(RunId run, CancellationToken ct)
        {
            Calls++;
            if (reject) throw new IOException("UNIT_TEST_ARTIFACT_FLUSH_FAILED");
            // Synthetic test bytes; no real CAN frame or ECU measurement claim.
            await using var bytes = new MemoryStream(new byte[] { 0x12, 0x34, 0x56 });
            var artifact = await artifacts.PutAsync(bytes, "UnitTestCanBytes",
                "application/octet-stream", "SYNTHETIC_UNIT_TEST", evidence, ct);
            await writer.AttachArtifactAsync(run, artifact, ct);
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
            Assert.NotNull(step.SignalProvenance);
            Assert.Equal(mapping.Asset.Value, step.SignalProvenance.DbcAssetId);
            Assert.Equal(mapping.AssetSha256, step.SignalProvenance.DbcSha256);
            Assert.Equal(mapping.CanId, step.SignalProvenance.FrameId);
            Assert.Equal("HostArrival", step.SignalProvenance.TimeSource);
            Assert.Equal("HostArrivalOnly", step.SignalProvenance.TimeQuality);
        });
        var raw = await fixture.Database.ReadFactsAsync(new RecordQuery(record.RunId, 100));
        Assert.Contains(raw, f => f.Description.StartsWith("STEP_BEGIN", StringComparison.Ordinal));
        Assert.Contains(raw, f => f.Description.StartsWith("STEP_END", StringComparison.Ordinal));
        Assert.Contains(raw, f => f.Description == "RUN_TERMINAL Passed");
        var html = await File.ReadAllTextAsync(completed.Reports.HtmlPath);
        Assert.Contains(assertions[0].StepOccurrenceId!.Value.ToString("D"), html);
        Assert.Contains(assertions[1].StepOccurrenceId!.Value.ToString("D"), html);
        Assert.Contains("证据归档不完整", html);
        Assert.Contains(mapping.AssetSha256, html);
        var json = await File.ReadAllTextAsync(completed.Reports.JsonPath);
        Assert.Contains("\"evidenceArchiveIncomplete\": true", json);
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
        var step = Assert.Single(await fixture.Database.ReadStepFactsAsync(record.RunId));
        if (testOutcome == "Failed")
        {
            Assert.Equal("CONDITION_FALSE", step.FailureCode);
            var events = await fixture.Database.ReadFactsAsync(new RecordQuery(record.RunId, 20));
            Assert.Contains(events, f => f.Kind == RunFactKind.Judgement &&
                f.Description.StartsWith("STEP_END", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal("NO_DATA", step.FailureCode);
        }
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
    [Fact]
    public async Task UnexpectedExceptionAfterBeginIsFinalizedUnknownAndReportRemainsReadable()
    {
        using var fixture = new Fixture();
        var invalidJson = "{invalid-json";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(invalidJson))).ToLowerInvariant();
        // Valid fingerprint but malformed payload models a corrupted prepared plan
        // discovered only after RECORD Begin. No hardware is touched.
        var corrupt = new PreparedTestPlan(hash, invalidJson, ImmutableArray<string>.Empty);
        var record = fixture.Record(corrupt);
        var (_, sample) = Signal(1);
        var actions = new Actions();
        var session = new RealTestRecordSession(record, fixture.Writer, fixture.Reports);
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            session.ExecuteAsync(new RealRunCoordinator(new Signals(sample), actions, new Waves()),
                corrupt, fixture.ReportFolder));
        Assert.Contains(record.RunId.ToString(), ex.Message);
        Assert.Equal(0, actions.Writes);

        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId);
        Assert.NotNull(history);
        Assert.Equal(RecordPersistenceState.Committed, history.Persistence);
        Assert.Equal("Unknown", history.TerminalState);
        Assert.Equal(Verdict.Error, history.Verdict);
        Assert.Equal("TEST_UNEXPECTED_TERMINATION", history.FailureReason);
        Assert.True(File.Exists(Path.Combine(fixture.ReportFolder,
            record.RunId.Value.ToString("N") + ".html")));
    }

    [Fact]
    public async Task CompletedNonAssertionDoesNotProduceDutJudgementFact()
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(2);
        var doc = TestDocument.Create("ReadOnly") with
            { Mappings = ImmutableArray.Create(mapping) };
        var read = StepNode.Create("Read").WithProperty("target", mapping.Name)
            .WithProperty("mode", "Latest").WithProperty("timeoutMs", "30")
            .WithProperty("maxAgeMs", "100").WithProperty("variable", "observation");
        doc = doc with
            { Phases = doc.Phases.SetItem(TestPhase.Test, ImmutableArray.Create(read)) };
        var plan = new PlanCompiler().Prepare(doc, allowRealOperations: true);
        Assert.Empty(plan.Diagnostics);
        var record = fixture.Record(plan);
        var outcome = await new RealTestRecordSession(record, fixture.Writer, fixture.Reports)
            .ExecuteAsync(new RealRunCoordinator(new Signals(sample), new Actions(), new Waves()),
                plan, fixture.ReportFolder);
        Assert.Equal(RealTestVerdict.Inconclusive, outcome.Result.Verdict);
        var rows = await fixture.Database.ReadFactsAsync(new RecordQuery(record.RunId, 100));
        Assert.Contains(rows, f => f.Kind == RunFactKind.RecordWrite &&
            f.Description.Contains("STEP_END", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, f => f.Kind == RunFactKind.Judgement &&
            f.Description.Contains("STEP_END", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkippedStepStillHasDurableBeginAndCompletion()
    {
        using var fixture = new Fixture();
        var (_, sample) = Signal(2);
        var disabled = StepNode.Create("Write").WithProperty("enabled", "false");
        var empty = TestDocument.Create("Disabled");
        var doc = empty with
        {
            Phases = empty.Phases.SetItem(TestPhase.Test, ImmutableArray.Create(disabled))
        };
        var plan = new PlanCompiler().Prepare(doc, allowRealOperations: true);
        Assert.Empty(plan.Diagnostics);
        var record = fixture.Record(plan);
        var actions = new Actions();
        var outcome = await new RealTestRecordSession(record, fixture.Writer, fixture.Reports)
            .ExecuteAsync(new RealRunCoordinator(new Signals(sample), actions, new Waves()),
                plan, fixture.ReportFolder);
        Assert.Equal(RealTestVerdict.Inconclusive, outcome.Result.Verdict);
        Assert.Equal(0, actions.Writes);
        var steps = await fixture.Database.ReadStepFactsAsync(record.RunId);
        var skipped = Assert.Single(steps);
        Assert.Equal("Skipped", skipped.Disposition);
        Assert.NotNull(skipped.StepOccurrenceId);
        var audit = await fixture.Database.ReadFactsAsync(new RecordQuery(record.RunId, 20));
        Assert.Contains(audit, f => f.Description.StartsWith("STEP_BEGIN", StringComparison.Ordinal));
        Assert.Contains(audit, f => f.Description.StartsWith("STEP_END", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvidenceMustBeAttachedBeforeCommitToCountAsArchived()
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(2);
        var plan = Plan(mapping, "2");
        var runRecord = fixture.Record(plan);
        var evidence = sample.Observation!.Evidence;
        var finalizer = new EvidenceFinalizer(fixture.Writer, fixture.Artifacts, evidence);
        var done = await new RealTestRecordSession(
            runRecord, fixture.Writer, fixture.Reports, finalizer)
            .ExecuteAsync(new RealRunCoordinator(new Signals(sample), new Actions(), new Waves()),
                plan, fixture.ReportFolder);
        Assert.Equal(RealTestVerdict.Passed, done.Result.Verdict);
        Assert.Equal(1, finalizer.Calls);
        var reportJson = await File.ReadAllTextAsync(done.Reports.JsonPath);
        Assert.Contains("\"evidenceArchiveIncomplete\": false", reportJson);
        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(runRecord.RunId);
        Assert.Equal(RecordPersistenceState.Committed, history!.Persistence);
    }

    [Fact]
    public async Task FailedEvidenceFinalizationCannotCommitAConfirmedPass()
    {
        using var fixture = new Fixture();
        var (mapping, sample) = Signal(2);
        var plan = Plan(mapping, "2");
        var record = fixture.Record(plan);
        var finalizer = new EvidenceFinalizer(fixture.Writer, fixture.Artifacts,
            sample.Observation!.Evidence, reject: true);
        await Assert.ThrowsAsync<IOException>(() =>
            new RealTestRecordSession(record, fixture.Writer, fixture.Reports, finalizer)
                .ExecuteAsync(new RealRunCoordinator(new Signals(sample), new Actions(), new Waves()),
                    plan, fixture.ReportFolder));
        Assert.Equal(1, finalizer.Calls);
        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId);
        Assert.NotNull(history);
        Assert.Equal(RecordPersistenceState.Committed, history.Persistence);
        Assert.Equal("Unknown", history.TerminalState);
        Assert.Equal(Verdict.Error, history.Verdict);
        Assert.True(File.Exists(Path.Combine(fixture.ReportFolder,
            record.RunId.Value.ToString("N") + ".html")));
    }

    [Theory]
    [InlineData(TestSignalStatus.Timeout)]
    [InlineData(TestSignalStatus.Disconnected)]
    [InlineData(TestSignalStatus.DecodeError)]
    public async Task MissingOrDisconnectedSignalCannotCommitPassAndRetainsQuality(
        TestSignalStatus failure)
    {
        using var fixture = new Fixture();
        var (mapping, _) = Signal(null);
        var plan = Plan(mapping, "2");
        var record = fixture.Record(plan);
        var completed = await new RealTestRecordSession(record, fixture.Writer, fixture.Reports)
            .ExecuteAsync(new RealRunCoordinator(
                new Signals(new TestSignalResult(failure, null, "REAL_SAMPLE_NOT_AVAILABLE")),
                new Actions(), new Waves()), plan, fixture.ReportFolder);
        Assert.Equal(RealTestVerdict.Inconclusive, completed.Result.Verdict);
        var history = await new RunHistoryQuery(fixture.Database).FindByRunAsync(record.RunId);
        Assert.NotNull(history);
        Assert.Equal("Unknown", history.TerminalState);
        Assert.Equal(Verdict.Inconclusive, history.Verdict);
        var fact = Assert.Single(await fixture.Database.ReadStepFactsAsync(record.RunId));
        Assert.Equal("NoData", fact.Disposition);
        Assert.Equal("NO_DATA", fact.FailureCode);
        Assert.Equal(failure.ToString(), fact.SignalStatus);
        Assert.Null(fact.SignalProvenance);
        Assert.Empty(fact.Evidence);
        var json = await File.ReadAllTextAsync(completed.Reports.JsonPath);
        Assert.Contains(failure.ToString(), json);
    }


}
