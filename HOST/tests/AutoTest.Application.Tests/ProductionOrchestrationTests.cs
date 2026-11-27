using System.Collections.Immutable;
using AutoTest.Application.Production;
using AutoTest.Contracts;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class ProductionOrchestrationTests
{
    private readonly RevisionFingerprint _hash =
        RevisionFingerprint.FromBytes(new byte[] { 9 });

    private static RawScan Scan(string code = "HYDEP-00001") =>
        new(code, "COM7",
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 1), true);

    private ProductionProjectExecution Project(bool flash = true) =>
        new(ProjectId.New(), new ProjectRevision(1), _hash, flash, "24V",
            ImmutableArray.Create(new AssetRef(
                AssetId.New(), AssetRevision.Create(1), _hash)));

    private static ProductionStationReadiness Station(
        AuthorizationScope auth = AuthorizationScope.Programming) =>
        new(StationId.Create("STATION-1"), StationConnectionState.Ready,
            "24V", true, 101, auth);

    private ProductionSession Ready(ProductionProjectExecution project)
    {
        var session = new ProductionSession();
        Assert.Equal(Outcome.Accepted, session.StartDraft(Scan()).Outcome);
        Assert.Equal(Outcome.Accepted,
            session.SetMatch(new MatchDecision(true, Scan().Text,
                project.Assets, null)).Outcome);
        Assert.Equal(Outcome.Accepted, session.Confirm(project,
            new RuleRevision(_hash), new ProjectRevision(1),
            Station()).Outcome);
        return session;
    }

    private ProductionCommandHandler Handler(ProductionProjectExecution project,
        ProductionSession session, TestStageExecutor executor,
        CapturingSink sink, IProductionPhysicalStation? physical = null,
        bool rewriteIdentity = false)
    {
        var workflow = new ProductionWorkflow(executor,
            new ProductionResultCoordinator(sink),
            new ProductionPreflight(), physical);
        return new ProductionCommandHandler(
            new TestMatcher(project.Assets, rewriteIdentity),
            _ => project, session, workflow,
            new RuleRevision(_hash), new ProjectRevision(1));
    }

    [Fact]
    public async Task ScanSelectsOneProjectButDoesNotActuateOrStart()
    {
        var project = Project();
        var session = new ProductionSession();
        var station = new TestPhysicalStation();
        var stages = new TestStageExecutor();
        var sink = new CapturingSink();
        var handler = Handler(project, session, stages, sink, station);
        var scanned = await handler.ScanAsync(Scan(), Station(), CancellationToken.None);
        Assert.Equal("READY_FOR_STATION", scanned.Code);
        Assert.Equal("HYDEP-00001", scanned.FullBarcode);
        Assert.NotNull(scanned.Run);
        Assert.Equal(ProductionState.Ready, scanned.State);
        Assert.Equal(0, station.PressRequests);
        Assert.Equal(0, station.StartRequests);
        Assert.Equal(0, stages.FlashCalls);
        Assert.Empty(sink.Facts);
    }

    [Theory]
    [InlineData("HYDEP")]
    [InlineData("HYDEP-")]
    [InlineData("HYDEP-001\n")]
    [InlineData("HYDEP 001")]
    [InlineData("OTHER?A-001")]
    public async Task InvalidBarcodeIsRejectedBeforeMatcher(string raw)
    {
        var project = Project();
        var session = new ProductionSession();
        var handler = Handler(project, session,
            new TestStageExecutor(), new CapturingSink());
        var decision = await handler.ScanAsync(Scan(raw), Station(),
            CancellationToken.None);
        Assert.Equal("BARCODE_INVALID", decision.Code);
        Assert.Equal(ProductionState.Empty, session.State);
    }

    [Fact]
    public async Task MatcherCannotRewriteActualScannerIdentity()
    {
        var project = Project();
        var session = new ProductionSession();
        var handler = Handler(project, session,
            new TestStageExecutor(), new CapturingSink(),
            rewriteIdentity: true);
        var decision = await handler.ScanAsync(Scan(), Station(),
            CancellationToken.None);
        Assert.Equal("SCAN_IDENTITY_CHANGED", decision.Code);
        Assert.Equal("HYDEP-00001", decision.FullBarcode);
        Assert.Equal(ProductionState.Blocked, session.State);
    }

    [Fact]
    public void AProjectWithoutMandatoryFlashIsNotProductionReady()
    {
        var project = Project(flash: false);
        var result = new ProductionPreflight().ValidateStation(project, Station());
        Assert.False(result.Ready);
        Assert.Contains("PRODUCTION_FLASH_REQUIRED", result.Blockers);
    }

    [Fact]
    public void ProgrammingRequiresRealStationAuthorization()
    {
        var project = Project();
        var result = new ProductionPreflight().ValidateStation(project,
            Station(AuthorizationScope.PhysicalCommand));
        Assert.False(result.Ready);
        Assert.Contains("FLASH_AUTH_REQUIRED", result.Blockers);
    }

    [Fact]
    public async Task UnconnectedPhysicalAdapterCannotRunAnyStage()
    {
        var project = Project();
        var session = new ProductionSession();
        var stages = new TestStageExecutor();
        var sink = new CapturingSink();
        var handler = Handler(project, session, stages, sink);
        await handler.ScanAsync(Scan(), Station(), CancellationToken.None);
        var result = await handler.BeginStationCycleAsync(CancellationToken.None);
        Assert.Equal("PHYSICAL_STATION_ADAPTER_NOT_CONNECTED", result.Code);
        Assert.Equal(ProductionState.Ready, result.State);
        Assert.Equal(0, stages.FlashCalls);
        Assert.Empty(sink.Facts);
        Assert.Equal(Outcome.Accepted, handler.ClearForNextScan().Outcome);
        Assert.Equal(ProductionState.Empty, session.State);
    }

    [Fact]
    public async Task FixtureIntentMustBeDurableBeforeOutputEnergizes()
    {
        var project = Project();
        var session = Ready(project);
        var physical = new TestPhysicalStation();
        var stages = new TestStageExecutor();
        var sink = new CapturingSink("FIXTURE_PRESS_INTENT_EFFECT_UNKNOWN");
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(sink),
            new ProductionPreflight(), physical)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FIXTURE_INTENT_NOT_DURABLE", result.Code);
        Assert.Equal(0, physical.PressRequests);
        Assert.Equal(0, stages.FlashCalls);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MechanicalClosureAndInterlockMustBothBeVerified(
        bool closed, bool interlock)
    {
        var project = Project();
        var session = Ready(project);
        var physical = new TestPhysicalStation(closed: closed, interlock: interlock);
        var stages = new TestStageExecutor();
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), physical)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("MECHANICAL_FIXTURE_NOT_VERIFIED", result.Code);
        Assert.Equal(0, physical.StartRequests);
        Assert.Equal(0, stages.FlashCalls);
        Assert.Equal(ProductionState.Unknown, result.State);
    }

    [Fact]
    public async Task PressedWithoutFreshStartProofNeverErasesEcu()
    {
        var project = Project();
        var session = Ready(project);
        var physical = new TestPhysicalStation(start: false);
        var stages = new TestStageExecutor();
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), physical)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("PHYSICAL_START_NOT_VERIFIED", result.Code);
        Assert.Equal(1, physical.StartRequests);
        Assert.Equal(0, stages.FlashCalls);
    }

    [Theory]
    [InlineData(true, false, "MECHANICAL_FIXTURE_NOT_VERIFIED")]
    [InlineData(false, true, "PHYSICAL_START_NOT_VERIFIED")]
    public async Task ReconnectedControllerSessionCannotReusePreviousPhysicalProof(
        bool staleFixture, bool staleStart, string expected)
    {
        var project = Project();
        var session = Ready(project);
        var physical = new TestPhysicalStation(
            staleFixture: staleFixture, staleStart: staleStart);
        var stages = new TestStageExecutor();
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), physical)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal(expected, result.Code);
        Assert.Equal(0, stages.FlashCalls);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task PhysicalStartProofMustBeDurablyRecordedBeforeFlash()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor();
        var sink = new CapturingSink("STATION_PHYSICAL_START_VERIFIED:TEST_EDGE");
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(sink),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("PHYSICAL_START_FACT_NOT_DURABLE", result.Code);
        Assert.Equal(0, stages.FlashCalls);
    }

    [Fact]
    public async Task FlashIntentMustBeDurableBeforeProgramming()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor();
        var sink = new CapturingSink("FLASH_STAGE_ENTERED_EFFECT_UNKNOWN");
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(sink),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_START_FACT_NOT_DURABLE", result.Code);
        Assert.Equal(0, stages.FlashCalls);
    }

    [Fact]
    public async Task MissingFlashEvidenceCannotReachTest()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor(flashEvidence: false);
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_EVIDENCE_MISSING", result.Code);
        Assert.Equal(1, stages.FlashCalls);
        Assert.Equal(0, stages.TestCalls);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task SimulatedFlashResultCannotReachTest()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor(simulatedFlash: true);
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_SIMULATED", result.Code);
        Assert.Equal(0, stages.TestCalls);
    }

    [Fact]
    public async Task OrdinaryDurableRecordCanNeverYieldFormalPass()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor();
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new CapturingSink()),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("PRODUCTION_VERIFIED_RECORD_COMMIT_NOT_CONNECTED", result.Code);
        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.Equal(ProductionState.Unknown, result.State);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task VerifiedCommitStillDoesNotAutomaticallyReleaseMechanicalFixture()
    {
        var project = Project();
        var session = Ready(project);
        var stages = new TestStageExecutor();
        var result = await new ProductionWorkflow(stages,
            new ProductionResultCoordinator(new TestVerifiedSink()),
            new ProductionPreflight(), new TestPhysicalStation())
            .ExecuteAsync(session, CancellationToken.None);
        // Test-only port; never supplied by Desktop or real device wiring.
        Assert.Equal(ProductionState.Completed, result.State);
        Assert.True(result.Durable);
        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.False(result.PhysicalReleaseAllowed);
        Assert.Equal(1, stages.FlashCalls);
        Assert.Equal(1, stages.TestCalls);
    }

    [Fact]
    public void SameBarcodeRetestMustCreateDistinctRunId()
    {
        var project = Project();
        var session = Ready(project);
        var original = session.Job!;
        session.BeginExecution();
        session.Mark(ProductionState.Completed);
        Assert.Equal(Outcome.Accepted, session.Reset().Outcome);
        session.StartDraft(Scan());
        session.SetMatch(new MatchDecision(true, Scan().Text,
            project.Assets, null));
        var next = session.Confirm(project,
            new RuleRevision(_hash), new ProjectRevision(1),
            Station()).Value!;
        Assert.NotEqual(original.Run, next.Run);
        Assert.Equal(original.DutIdentity, next.DutIdentity);
    }

    [Fact]
    public void UnknownPhysicalSideEffectsCannotBeClearedOrReplaced()
    {
        var project = Project();
        var session = Ready(project);
        session.BeginExecution();
        session.Mark(ProductionState.Unknown, "FLASH_EFFECT_UNCERTAIN");
        Assert.Equal("RECOVERY_REQUIRED", session.Reset().FailureCode);
        Assert.Equal("STATION_BUSY", session.StartDraft(Scan("HYDEP-00002")).FailureCode);
    }

    [Fact]
    public void ProductionCommandHandlerHasNoSoftwareOrRawBoolStartApi()
    {
        var names = typeof(ProductionCommandHandler).GetMethods()
            .Select(m => m.Name).ToArray();
        Assert.DoesNotContain("StartAsync", names);
        Assert.DoesNotContain("PhysicalStartSignalAsync", names);
    }

    [Fact]
    public async Task StatisticsCountUniqueUnitsOnlyFromRecordStore()
    {
        var reader = new TestSummaryReader();
        var projection = new ProductionStatisticsProjection(reader);
        var summary = await projection.Refresh(projection.Filter(null, null),
            CancellationToken.None);
        Assert.Equal(2, summary.RunCount);
        Assert.Equal(1, summary.DistinctDutCount);
        Assert.Equal(1, reader.Calls);
    }

    private sealed class TestMatcher(
        ImmutableArray<AssetRef> assets, bool rewriteIdentity) : IIdentityMatcher
    {
        public MatchDecision Match(RawScan scan, RuleRevision rule, ProjectRevision project) =>
            new(true, rewriteIdentity ? "FORGED-00001" : scan.Text, assets, null);
    }

    /// <summary>Test-only external ports. Not registered in a production host.</summary>
    private sealed class TestPhysicalStation(
        bool closed = true, bool interlock = true, bool start = true,
        bool staleFixture = false, bool staleStart = false)
        : IProductionPhysicalStation
    {
        public int PressRequests { get; private set; }
        public int StartRequests { get; private set; }
        public ValueTask<FixtureAdmissionEvidence> PressAndVerifyAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            PressRequests++;
            return ValueTask.FromResult(new FixtureAdmissionEvidence(
                job.Run, job.Station,
                job.ControllerEpoch + (staleFixture ? 1 : 0), closed, interlock,
                closed && interlock ? "TEST_FIXTURE" : ""));
        }
        public ValueTask<PhysicalStartEvidence> WaitForStartAsync(
            ProductionJobSnapshot job, FixtureAdmissionEvidence fixture,
            CancellationToken ct)
        {
            StartRequests++;
            return ValueTask.FromResult(new PhysicalStartEvidence(
                job.Run, job.Station,
                job.ControllerEpoch + (staleStart ? 1 : 0), start,
                start ? "TEST_EDGE" : ""));
        }
    }

    private sealed class TestStageExecutor(
        bool flashEvidence = true, bool simulatedFlash = false,
        bool testEvidence = true) : IProductionStageExecutor
    {
        public int FlashCalls { get; private set; }
        public int TestCalls { get; private set; }
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            FlashCalls++;
            return ValueTask.FromResult(new ProductionStageResult(
                Outcome.Accepted, Verdict.Pass, "FLASH_TEST_ONLY",
                simulatedFlash,
                flashEvidence ? ImmutableArray.Create(EvidenceRef.New()) :
                    ImmutableArray<EvidenceRef>.Empty));
        }
        public ValueTask<ProductionStageResult> ExecuteTestAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            TestCalls++;
            return ValueTask.FromResult(new ProductionStageResult(
                Outcome.Accepted, Verdict.Pass, "TEST_ONLY",
                false, testEvidence ? ImmutableArray.Create(EvidenceRef.New()) :
                    ImmutableArray<EvidenceRef>.Empty));
        }
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class CapturingSink(string? failOn = null) : IRunFactSink
    {
        public List<RunFact> Facts { get; } = [];
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact,
            CancellationToken ct)
        {
            Facts.Add(fact);
            var durable = !string.Equals(fact.Description, failOn,
                StringComparison.Ordinal);
            return ValueTask.FromResult(new RecordWriteReceipt(
                fact.Run, fact.EventId, durable,
                durable ? "DURABLE" : "DISK_FAILURE"));
        }
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct) =>
            ValueTask.FromResult(new RecordCommitReceipt(run, true, "DURABLE"));
    }

    private sealed class TestVerifiedSink : IVerifiedProductionCommitSink
    {
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact,
            CancellationToken ct) =>
            ValueTask.FromResult(new RecordWriteReceipt(
                fact.Run, fact.EventId, true, "TEST_DURABLE"));
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run,
            CancellationToken ct) =>
            ValueTask.FromResult(new RecordCommitReceipt(run, true, "TEST_DURABLE"));
        public ValueTask<VerifiedProductionCommit> CommitVerifiedAsync(RunId run,
            ProductionStageResult test, CancellationToken ct) =>
            ValueTask.FromResult(new VerifiedProductionCommit(run,
                true, true, test.Verdict, "TEST_ONLY_PROOF"));
    }

    private sealed class TestSummaryReader : IProductionSummaryReader
    {
        public int Calls { get; private set; }
        public ValueTask<ProductionSummary> QuerySummaryAsync(
            ProductionSummaryFilter filter, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(new ProductionSummary(2, 1, 1, 1, 0, true));
        }
    }
}
