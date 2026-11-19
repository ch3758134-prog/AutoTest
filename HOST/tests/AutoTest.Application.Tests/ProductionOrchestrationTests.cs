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
