using System.Collections.Immutable;
using AutoTest.Application.Production;
using AutoTest.Contracts;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class ProductionOrchestrationTests
{
    private readonly RevisionFingerprint _hash = RevisionFingerprint.FromBytes(new byte[] { 9 });
    private static RawScan Scan() => new("EPB-00001", "keyboard",
        TimePoint.FromSimulation(DateTimeOffset.UtcNow, 1), true);
    private ProductionConfiguration Config(bool flash = false) => new("TEST", 1, flash,
        "24V", ImmutableArray.Create(new AssetRef(AssetId.New(), AssetRevision.Create(1), _hash)));
    private static ProductionStationReadiness Station(bool mock = true, AuthorizationScope? auth = null) =>
        new(StationId.Create("SIM"), StationConnectionState.Ready, "24V", true, mock,
            auth ?? (mock ? AuthorizationScope.MockOnly : AuthorizationScope.PhysicalCommand));

    private static ProductionSession Matched(ProductionConfiguration config)
    {
        var session = new ProductionSession();
        Assert.Equal(Outcome.Accepted, session.StartDraft(Scan()).Outcome);
        Assert.Equal(Outcome.Accepted, session.SetMatch(new MatchDecision(true,
            "3:EPB5:00001", config.Assets, null)).Outcome);
        return session;
    }

    [Fact] // P1-T01
    public void P1_T01_ScanToMatchToReadyMock()
    {
        var config = Config();
        var session = Matched(config);
        var check = new ProductionPreflight().Validate(session, config, Station());
        Assert.True(check.Ready);
        Assert.Equal(Outcome.Accepted, session.Confirm(config,
            new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true).Outcome);
        Assert.Equal(ProductionState.Ready, session.State);
    }

    [Fact] // P1-T02
    public void P1_T02_WrongModelIsBlocked()
    {
        var config = Config();
        var session = new ProductionSession();
        session.StartDraft(Scan());
        var result = session.SetMatch(new MatchDecision(false, null,
            ImmutableArray<AssetRef>.Empty, "CONFIG_NO_MATCH"));
        Assert.Equal(Outcome.Rejected, result.Outcome);
        Assert.Equal(ProductionState.Blocked, session.State);
        Assert.Equal("CONFIG_NO_MATCH", session.FailureCode);
    }

    [Fact] // P1-T03
    public void P1_T03_RetestCreatesNewRunIdWithoutChangingUnitIdentity()
    {
        var config = Config();
        var session = Matched(config);
        var first = session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true).Value!;
        session.BeginExecution();
        session.Mark(ProductionState.Completed);
        Assert.Equal(Outcome.Accepted, session.Reset().Outcome);
        session.StartDraft(Scan());
        session.SetMatch(new MatchDecision(true, first.DutIdentity, config.Assets, null));
        var second = session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true).Value!;
        Assert.NotEqual(first.Run, second.Run);
        Assert.Equal(first.DutIdentity, second.DutIdentity);
    }

    [Fact] // P1-T04
    public async Task P1_T04_StatisticsComeOnlyFromRecordReader()
    {
        var reader = new FakeSummaryReader();
        var projection = new ProductionStatisticsProjection(reader);
        var summary = await projection.Refresh(projection.Filter(null, null), CancellationToken.None);
        Assert.Equal(2, summary.RunCount);
        Assert.Equal(1, summary.DistinctDutCount);
        Assert.Equal(1, reader.Calls);
    }

    [Fact] // P1-T05
    public void P1_T05_FlashWithoutProgrammingAuthorizationIsRejected()
    {
        var config = Config(true);
        var result = new ProductionPreflight().ValidateStation(config, Station(false,
            AuthorizationScope.PhysicalCommand));
        Assert.False(result.Ready);
        Assert.Contains("FLASH_AUTH_REQUIRED", result.Blockers);
    }

    [Fact]
    public void PhysicalInspectionCannotUseAnOldNoFlashRecipe()
    {
        var config = Config(flash: false);
        var status = new ProductionPreflight().ValidateStation(config,
            Station(false, AuthorizationScope.Programming));
        Assert.False(status.Ready);
        Assert.Contains("PRODUCTION_FLASH_REQUIRED", status.Blockers);
    }

    [Fact] // E1-T06
    public void E1_T06_ScanDuringExecutionCannotReplaceUnit()
    {
        var config = Config();
        var session = Matched(config);
        var initial = session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true).Value!;
        session.BeginExecution();
        var rejected = session.StartDraft(new RawScan("EPB-99999", "keyboard", Scan().Time, true));
        Assert.Equal("STATION_BUSY", rejected.FailureCode);
        Assert.Equal(initial.DutIdentity, session.Job!.DutIdentity);
    }

    [Fact]
    public async Task P1_MockAcknowledgementNeverReleasesPhysicalDut()
    {
        var config = Config();
        var session = Matched(config);
        session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true);
        var sink = new FakeSink(true);
        var workflow = new ProductionWorkflow(new FakeStageExecutor(),
            new ProductionResultCoordinator(sink), new ProductionPreflight());
        var result = await workflow.ExecuteAsync(session, CancellationToken.None);
        Assert.False(result.PhysicalReleaseAllowed);
        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.Equal("MOCK_COMPLETE_NOT_DUT_PASS", result.Code);
    }

    [Fact]
    public async Task P1_NonDurableRecordBlocksCompletion()
    {
        var config = Config();
        var session = Matched(config);
        session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true);
        var workflow = new ProductionWorkflow(new FakeStageExecutor(),
            new ProductionResultCoordinator(new FakeSink(false)), new ProductionPreflight());
        var result = await workflow.ExecuteAsync(session, CancellationToken.None);
        Assert.Equal(ProductionState.AwaitingCommit, result.State);
        Assert.False(result.Durable);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task E1_T06_RunningSecondScanIsImmediatelyRejectedAndDoesNotQueue()
    {
        var config = Config();
        var executor = new BlockingStageExecutor();
        var session = new ProductionSession();
        var matcher = new FakeMatcher(config.Assets);
        var workflow = new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FakeSink(true)), new ProductionPreflight());
        var handler = new ProductionCommandHandler(matcher, _ => config, session, workflow,
            new RuleRevision(_hash), new ProjectRevision(1));
        var first = handler.ScanAsync(Scan(), Station(), true, CancellationToken.None);
        Assert.Equal(ProductionState.Running, session.State);
        var second = await handler.ScanAsync(new RawScan("EPB-99999", "keyboard", Scan().Time, true),
            Station(), true, CancellationToken.None);
        Assert.Equal("STATION_BUSY", second.Code);
        Assert.Equal("3:EPB5:00001", session.Job!.DutIdentity);
        executor.Finish();
        await first;
    }

    [Fact]
    public void P1_UnknownSideEffectsCannotBeSilentlyResetOrReplaced()
    {
        var config = Config();
        var session = Matched(config);
        session.Confirm(config, new RuleRevision(_hash), new ProjectRevision(1), Station().Station, true);
        session.BeginExecution();
        session.Mark(ProductionState.Unknown, "UNKNOWN_FLASH_EFFECT");
        Assert.Equal("RECOVERY_REQUIRED", session.Reset().FailureCode);
        Assert.Equal("STATION_BUSY", session.StartDraft(Scan()).FailureCode);
    }

    [Fact]
    public async Task ClaimedVerifiedCommitWithoutStageEvidenceCannotYieldPhysicalPass()
    {
        var config = Config(flash: true);
        var session = Matched(config);
        var job = session.Confirm(config, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false).Value!;
        var test = new ProductionStageResult(Outcome.Accepted, Verdict.Pass,
            "UNPROVEN", false, ImmutableArray<EvidenceRef>.Empty);
        var result = await new ProductionResultCoordinator(new ClaimingVerifiedSink())
            .FinalizeAsync(job, test, CancellationToken.None);
        Assert.Equal("PRODUCTION_VERDICT_NOT_PROVEN", result.Code);
        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task DurableGenericUnknownCommitCannotBecomePhysicalDutPass()
    {
        var config = Config(flash: true);
        var session = Matched(config);
        var job = session.Confirm(config, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false).Value!;
        var test = new ProductionStageResult(Outcome.Accepted, Verdict.Pass,
            "CLAIMED_PASS", false, ImmutableArray<EvidenceRef>.Empty);
        var result = await new ProductionResultCoordinator(new FakeSink(true))
            .FinalizeAsync(job, test, CancellationToken.None);
        Assert.Equal("PRODUCTION_VERIFIED_RECORD_COMMIT_NOT_CONNECTED", result.Code);
        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task RealProduction_NeverContinuesToTestWithoutPhysicalFlashEvidence()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new FlashProofExecutor(simulated: false);
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FakeSink(true)),
            new ProductionPreflight(), new TestStationPort())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_EVIDENCE_MISSING", result.Code);
        Assert.Equal(0, executor.TestCalls);
        Assert.Equal(ProductionState.Unknown, result.State);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task RealProduction_RejectsSimulatedFlashEvenWhenItClaimsPass()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new FlashProofExecutor(simulated: true);
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FakeSink(true)),
            new ProductionPreflight(), new TestStationPort())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_SIMULATED", result.Code);
        Assert.Equal(0, executor.TestCalls);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task RealProductionCannotAutoStartFromBarcodeOrUseSoftwareStart()
    {
        var configuration = Config(flash: true);
        var executor = new CountingProductionStageExecutor();
        var session = new ProductionSession();
        var handler = new ProductionCommandHandler(
            new FakeMatcher(configuration.Assets), _ => configuration,
            session, new ProductionWorkflow(executor,
                new ProductionResultCoordinator(new FakeSink(true)),
                new ProductionPreflight()),
            new RuleRevision(_hash), new ProjectRevision(1));
        var admitted = await handler.ScanAsync(Scan(),
            Station(mock: false, auth: AuthorizationScope.Programming),
            autoStart: true, CancellationToken.None);
        Assert.Equal("REAL_PRODUCTION_PHYSICAL_START_REQUIRED", admitted.Code);
        Assert.Equal(ProductionState.Ready, admitted.State);
        Assert.NotNull(admitted.Run);
        var software = await handler.StartAsync(CancellationToken.None);
        Assert.Equal("REAL_PRODUCTION_PHYSICAL_START_REQUIRED", software.Code);
        Assert.Equal(ProductionState.Ready, software.State);
        Assert.Equal(0, executor.Commands);
    }

    [Fact]
    public async Task RawBoolPhysicalStartCannotClaimARealInspectionWithoutVerifiedInput()
    {
        var configuration = Config(flash: true);
        var executor = new CountingProductionStageExecutor();
        var session = new ProductionSession();
        var handler = new ProductionCommandHandler(
            new FakeMatcher(configuration.Assets), _ => configuration,
            session, new ProductionWorkflow(executor,
                new ProductionResultCoordinator(new FakeSink(true)),
                new ProductionPreflight()),
            new RuleRevision(_hash), new ProjectRevision(1));
        var admitted = await handler.ScanAsync(Scan(),
            Station(mock: false, auth: AuthorizationScope.Programming),
            autoStart: false, CancellationToken.None);
        Assert.Equal("READY_FOR_START", admitted.Code);
        var released = await handler.PhysicalStartSignalAsync(false, CancellationToken.None);
        var pressed = await handler.PhysicalStartSignalAsync(true, CancellationToken.None);
        Assert.Equal("PHYSICAL_START_SOURCE_NOT_CONNECTED", released.Code);
        Assert.Equal("PHYSICAL_START_SOURCE_NOT_CONNECTED", pressed.Code);
        Assert.Equal(ProductionState.Ready, session.State);
        Assert.Equal(0, executor.Commands);
    }

    [Fact]
    public async Task StationCycleCannotUseSoftwareStartOrRunWithoutPhysicalAdapter()
    {
        var config = Config(flash: true);
        var executor = new CountingProductionStageExecutor();
        var session = new ProductionSession();
        var workflow = new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new CapturingFactSink()),
            new ProductionPreflight());
        var handler = new ProductionCommandHandler(new FakeMatcher(config.Assets),
            _ => config, session, workflow,
            new RuleRevision(_hash), new ProjectRevision(1));
        var admitted = await handler.ScanAsync(Scan(),
            Station(mock: false, auth: AuthorizationScope.Programming),
            autoStart: false, CancellationToken.None);
        Assert.Equal("READY_FOR_START", admitted.Code);
        var cycle = await handler.BeginStationCycleAsync(CancellationToken.None);
        Assert.Equal("PHYSICAL_STATION_ADAPTER_NOT_CONNECTED", cycle.Code);
        Assert.Equal(ProductionState.Ready, cycle.State);
        Assert.Equal(0, executor.Commands);
        Assert.Null(handler.LastStartSource);
        var cancel = handler.RetryWithNewRun();
        Assert.Equal(Outcome.Accepted, cancel.Outcome);
        Assert.Equal(ProductionState.Empty, session.State);
    }

    [Fact]
    public async Task RealRunCannotTouchFixtureOrFlashWithoutTrustedStationPort()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new CountingProductionStageExecutor();
        var sink = new CapturingFactSink();
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(sink), new ProductionPreflight())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("PHYSICAL_STATION_ADAPTER_NOT_CONNECTED", result.Code);
        Assert.Equal(ProductionState.Ready, result.State);
        Assert.Equal(0, executor.Commands);
        Assert.Empty(sink.Facts);
    }

    [Fact]
    public async Task UnverifiedClampPositionNeverStartsProgramming()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new CountingProductionStageExecutor();
        var station = new TestStationPort(closureVerified: false);
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FakeSink(true)),
            new ProductionPreflight(), station)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("MECHANICAL_FIXTURE_NOT_VERIFIED", result.Code);
        Assert.Equal(0, station.StartRequests);
        Assert.Equal(0, executor.Commands);
        Assert.False(result.PhysicalReleaseAllowed);
    }

    [Fact]
    public async Task MissingFreshStartEdgeNeverStartsFlash()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new CountingProductionStageExecutor();
        var station = new TestStationPort(startVerified: false);
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FakeSink(true)),
            new ProductionPreflight(), station)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("PHYSICAL_START_NOT_VERIFIED", result.Code);
        Assert.Equal(1, station.StartRequests);
        Assert.Equal(0, executor.Commands);
    }

    [Fact]
    public async Task FixtureIntentMustBeDurableBeforeStationOutput()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var station = new TestStationPort();
        var executor = new CountingProductionStageExecutor();
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FixtureIntentRejectSink()),
            new ProductionPreflight(), station)
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FIXTURE_INTENT_NOT_DURABLE", result.Code);
        Assert.Equal(0, station.PressRequests);
        Assert.Equal(0, executor.Commands);
    }

    // Only used inside domain unit tests. No runtime adapter is registered.
    private sealed class TestStationPort(
        bool closureVerified = true, bool startVerified = true)
        : IProductionPhysicalStation
    {
        public int PressRequests { get; private set; }
        public int StartRequests { get; private set; }
        public ValueTask<FixtureAdmissionEvidence> PressAndVerifyAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            PressRequests++;
            return ValueTask.FromResult(new FixtureAdmissionEvidence(
                job.Run, job.Station, closureVerified, true,
                closureVerified ? "TEST_ONLY_FIXTURE_PROOF" : ""));
        }
        public ValueTask<PhysicalStartEvidence> WaitForStartAsync(
            ProductionJobSnapshot job, FixtureAdmissionEvidence fixture,
            CancellationToken ct)
        {
            StartRequests++;
            return ValueTask.FromResult(new PhysicalStartEvidence(
                job.Run, job.Station, startVerified,
                startVerified ? "TEST_ONLY_START_EDGE" : ""));
        }
    }

    private sealed class FixtureIntentRejectSink : IRunFactSink
    {
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct)
        {
            var durable = fact.Description != "FIXTURE_PRESS_INTENT_EFFECT_UNKNOWN";
            return ValueTask.FromResult(new RecordWriteReceipt(
                fact.Run, fact.EventId, durable, durable ? "DURABLE" : "DISK_FAILURE"));
        }
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct) =>
            throw new InvalidOperationException("Never commit unproven physical run.");
    }

    private sealed class CountingProductionStageExecutor : IProductionStageExecutor
    {
        public int Commands { get; private set; }
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            Commands++;
            throw new InvalidOperationException("No real hardware must be invoked.");
        }
        public ValueTask<ProductionStageResult> ExecuteTestAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            Commands++;
            throw new InvalidOperationException("No real TEST must be invoked.");
        }
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NondurableFlashAdmissionFactPreventsAnyDestructiveExecutorCall()
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var executor = new CountingProductionStageExecutor();
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(new FlashAdmissionRejectSink()),
            new ProductionPreflight(), new TestStationPort())
            .ExecuteAsync(session, CancellationToken.None);
        Assert.Equal("FLASH_START_FACT_NOT_DURABLE", result.Code);
        Assert.Equal(ProductionState.Unknown, result.State);
        Assert.False(result.PhysicalReleaseAllowed);
        Assert.Equal(0, executor.Commands);
    }

    private sealed class FlashAdmissionRejectSink : IRunFactSink
    {
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct)
        {
            var durable = fact.Description != "FLASH_STAGE_ENTERED_EFFECT_UNKNOWN";
            return ValueTask.FromResult(new RecordWriteReceipt(
                fact.Run, fact.EventId, durable, durable ? "DURABLE" : "DISK_FAILURE"));
        }
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct) =>
            throw new InvalidOperationException("No verdict is permitted.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalFlashFailureRecordsActualInterruptedStageWithoutRunningTest(
        bool operatorCancelled)
    {
        var configuration = Config(flash: true);
        var session = Matched(configuration);
        session.Confirm(configuration, new RuleRevision(_hash),
            new ProjectRevision(1), Station(false, AuthorizationScope.Programming).Station,
            isMock: false);
        var sink = new CapturingFactSink();
        var executor = new ThrowingFlashExecutor(operatorCancelled);
        var result = await new ProductionWorkflow(executor,
            new ProductionResultCoordinator(sink), new ProductionPreflight(),
            new TestStationPort()).ExecuteAsync(session, CancellationToken.None);
        Assert.Equal(operatorCancelled ? ProductionState.Aborted :
            ProductionState.Unknown, result.State);
        Assert.False(result.PhysicalReleaseAllowed);
        Assert.Equal(0, executor.TestCalls);
        Assert.Contains(sink.Facts, f =>
            f.Description == "FLASH_STAGE_ENTERED_EFFECT_UNKNOWN");
        Assert.Contains(sink.Facts, f => f.Description ==
            (operatorCancelled ? "RUN_CANCELLED_AT_FLASH" :
                "RUN_EXCEPTION_AT_FLASH"));
    }

    private sealed class ThrowingFlashExecutor(bool cancelled) : IProductionStageExecutor
    {
        public int TestCalls { get; private set; }
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            if (cancelled)
                throw new OperationCanceledException("Operator stop during FLASH");
            throw new IOException("Real FLASH transport failure");
        }
        public ValueTask<ProductionStageResult> ExecuteTestAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            TestCalls++;
            throw new InvalidOperationException("No TEST after uncertain FLASH.");
        }
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class CapturingFactSink : IRunFactSink
    {
        public List<RunFact> Facts { get; } = [];
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct)
        {
            Facts.Add(fact);
            return ValueTask.FromResult(
                new RecordWriteReceipt(fact.Run, fact.EventId, true, "DURABLE"));
        }
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct) =>
            ValueTask.FromResult(new RecordCommitReceipt(run, true, "DURABLE"));
    }

    private sealed class FlashProofExecutor(bool simulated) : IProductionStageExecutor
    {
        public int TestCalls { get; private set; }
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(
            ProductionJobSnapshot job, CancellationToken ct) =>
            ValueTask.FromResult(new ProductionStageResult(Outcome.Accepted, Verdict.Pass,
                "CLAIMED_PASS", simulated, ImmutableArray<EvidenceRef>.Empty));
        public ValueTask<ProductionStageResult> ExecuteTestAsync(
            ProductionJobSnapshot job, CancellationToken ct)
        {
            TestCalls++;
            return ValueTask.FromResult(new ProductionStageResult(Outcome.Accepted,
                Verdict.Pass, "UNEXPECTED_TEST", false,
                ImmutableArray<EvidenceRef>.Empty));
        }
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeMatcher : IIdentityMatcher
    {
        private readonly ImmutableArray<AssetRef> _assets;
        public FakeMatcher(ImmutableArray<AssetRef> assets) => _assets = assets;
        public MatchDecision Match(RawScan scan, RuleRevision rule, ProjectRevision project) =>
            new(true, "3:EPB5:00001", _assets, null);
    }
    private sealed class BlockingStageExecutor : IProductionStageExecutor
    {
        private readonly TaskCompletionSource<ProductionStageResult> _finish =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(ProductionJobSnapshot job, CancellationToken ct) =>
            throw new InvalidOperationException("Not required in this scenario.");
        public ValueTask<ProductionStageResult> ExecuteTestAsync(ProductionJobSnapshot job, CancellationToken ct) =>
            new(_finish.Task);
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) => ValueTask.CompletedTask;
        public void Finish() => _finish.SetResult(new(Outcome.Accepted, Verdict.Inconclusive,
            "MOCK_DONE", true, ImmutableArray<EvidenceRef>.Empty));
    }

    private sealed class FakeSummaryReader : IProductionSummaryReader
    {
        public int Calls { get; private set; }
        public ValueTask<ProductionSummary> QuerySummaryAsync(ProductionSummaryFilter filter, CancellationToken ct)
        { Calls++; return ValueTask.FromResult(new ProductionSummary(2, 1, 1, 1, 0, true)); }
    }
    private sealed class ClaimingVerifiedSink : IVerifiedProductionCommitSink
    {
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact,
            CancellationToken ct) =>
            ValueTask.FromResult(new RecordWriteReceipt(
                fact.Run, fact.EventId, true, "DURABLE"));
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run,
            CancellationToken ct) =>
            ValueTask.FromResult(new RecordCommitReceipt(run, true, "DURABLE"));
        public ValueTask<VerifiedProductionCommit> CommitVerifiedAsync(RunId run,
            ProductionStageResult test, CancellationToken ct) =>
            ValueTask.FromResult(new VerifiedProductionCommit(
                run, true, true, Verdict.Pass, "CLAIMED"));
    }

    private sealed class FakeSink : IRunFactSink
    {
        private readonly bool _durable;
        public FakeSink(bool durable) => _durable = durable;
        public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct) =>
            ValueTask.FromResult(new RecordWriteReceipt(fact.Run, fact.EventId, _durable, "FAKE"));
        public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct) =>
            ValueTask.FromResult(new RecordCommitReceipt(run, _durable, "FAKE"));
    }
    private sealed class FakeStageExecutor : IProductionStageExecutor
    {
        public ValueTask<ProductionStageResult> ExecuteFlashAsync(ProductionJobSnapshot job, CancellationToken ct) =>
            ValueTask.FromResult(new ProductionStageResult(Outcome.Accepted, Verdict.Pass,
                "MOCK_FLASH", true, ImmutableArray<EvidenceRef>.Empty));
        public ValueTask<ProductionStageResult> ExecuteTestAsync(ProductionJobSnapshot job, CancellationToken ct) =>
            ValueTask.FromResult(new ProductionStageResult(Outcome.Accepted, Verdict.Pass,
                "MOCK_TEST", true, ImmutableArray<EvidenceRef>.Empty));
        public ValueTask RequestStopAsync(RunId run, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
