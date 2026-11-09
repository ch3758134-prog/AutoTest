using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionStageResult(Outcome Outcome, Verdict Verdict, string Code,
    bool SimulationOnly, ImmutableArray<EvidenceRef> Evidence);
public sealed record ProductionRunResult(RunId Run, ProductionState State,
    Verdict Verdict, bool Durable, bool PhysicalReleaseAllowed, string Code);

public sealed class ProductionResultCoordinator
{
    private readonly IRunFactSink _facts;
    private readonly TimeProvider _clock;
    public ProductionResultCoordinator(IRunFactSink facts, TimeProvider? clock = null)
    { _facts = facts; _clock = clock ?? TimeProvider.System; }

    public async ValueTask<OperationResult<Unit>> AppendAsync(ProductionJobSnapshot job,
        string code, RunFactKind kind, EvidenceQuality quality, CancellationToken ct)
    {
        var result = await _facts.AppendAsync(new RunFact(job.Run, Guid.NewGuid(), Guid.NewGuid(),
            kind, code, new TimePoint(_clock.GetUtcNow(), _clock.GetTimestamp(),
                TimeSource.HostArrival, TimeQuality.HostArrivalOnly), quality,
            ImmutableArray<EvidenceRef>.Empty), ct).ConfigureAwait(false);
        if (!result.Durable && !job.IsMock)
            return OperationResult.Uncertain("RECORD_APPEND_NOT_DURABLE", "Run fact not durably stored.");
        return OperationResult.Accepted();
    }

    // A non-durable commit is never reported as a completed production record.
    public async ValueTask<ProductionRunResult> FinalizeAsync(ProductionJobSnapshot job,
        ProductionStageResult test, CancellationToken ct)
    {
        RecordCommitReceipt commit;
        try { commit = await _facts.CommitAsync(job.Run, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
            false, false, "RECORD_COMMIT_EXCEPTION"); }
        if (!commit.Durable || commit.Run != job.Run)
            return new(job.Run, ProductionState.AwaitingCommit, Verdict.Inconclusive,
                false, false, "RECORD_COMMIT_NOT_DURABLE");
        if (test.Outcome != Outcome.Accepted || test.Verdict is Verdict.Error or Verdict.Inconclusive)
            return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                true, false, "RUN_NOT_PROVEN");
        if (test.Verdict == Verdict.Fail)
            return new(job.Run, ProductionState.Failed, Verdict.Fail,
                true, false, "RUN_FAILED");
        if (test.Verdict != Verdict.Pass)
            return new(job.Run, ProductionState.Unknown, test.Verdict,
                true, false, "RUN_NOT_PASS");
        // No physical-release authority is issued in G4. Mock PASS is never DUT PASS.
        if (job.IsMock || test.SimulationOnly)
            return new(job.Run, ProductionState.Completed, Verdict.Inconclusive,
                true, false, "MOCK_COMPLETE_NOT_DUT_PASS");
        return new(job.Run, ProductionState.Completed, Verdict.Pass,
            true, false, "AWAIT_REAL_PRODUCTION_RELEASE_POLICY");
    }

    public bool RequireCommit(ProductionRunResult result) => !result.Durable;
}
