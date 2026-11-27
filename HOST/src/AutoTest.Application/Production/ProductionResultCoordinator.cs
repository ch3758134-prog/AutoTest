using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionStageResult(Outcome Outcome, Verdict Verdict, string Code,
    bool SimulationOnly, ImmutableArray<EvidenceRef> Evidence);
public sealed record ProductionRunResult(RunId Run, ProductionState State,
    Verdict Verdict, bool Durable, bool PhysicalReleaseAllowed, string Code);

/// <summary>
/// The formal production RECORD implementation must prove and durably commit
/// the SAME Inspection RunId and verified final physical verdict. Plain
/// IRunFactSink.CommitAsync cannot certify a DUT PASS (the current record
/// writer commits Unknown by design).
/// </summary>
public sealed record VerifiedProductionCommit(RunId Run, bool Durable,
    bool EvidenceVerified, Verdict Verdict, string Code);

public interface IVerifiedProductionCommitSink : IRunFactSink
{
    ValueTask<VerifiedProductionCommit> CommitVerifiedAsync(RunId run,
        ProductionStageResult test, CancellationToken ct);
}

public sealed class ProductionResultCoordinator
{
    private readonly IRunFactSink _facts;
    private readonly TimeProvider _clock;
    public ProductionResultCoordinator(IRunFactSink facts, TimeProvider? clock = null)
    { _facts = facts; _clock = clock ?? TimeProvider.System; }

    public async ValueTask<OperationResult<Unit>> AppendAsync(ProductionJobSnapshot job,
        string code, RunFactKind kind, EvidenceQuality quality, CancellationToken ct,
        ImmutableArray<EvidenceRef> evidence = default)
    {
        var result = await _facts.AppendAsync(new RunFact(job.Run, Guid.NewGuid(), Guid.NewGuid(),
            kind, code, new TimePoint(_clock.GetUtcNow(), _clock.GetTimestamp(),
                TimeSource.HostArrival, TimeQuality.HostArrivalOnly), quality,
            evidence.IsDefault ? ImmutableArray<EvidenceRef>.Empty : evidence),
            ct).ConfigureAwait(false);
        if (!result.Durable)
            return OperationResult.Uncertain("RECORD_APPEND_NOT_DURABLE", "Run fact not durably stored.");
        return OperationResult.Accepted();
    }

    // Only a record sink that explicitly verifies the physical evidence and
    // commits the terminal verdict may report a real production DUT PASS.
    // An ordinary IRunFactSink.CommitAsync can durably record an UNKNOWN, but
    // cannot be promoted to a passing production conclusion.
    public async ValueTask<ProductionRunResult> FinalizeAsync(ProductionJobSnapshot job,
        ProductionStageResult test, CancellationToken ct)
    {
        if (_facts is IVerifiedProductionCommitSink verified)
        {
            VerifiedProductionCommit receipt;
            try
            {
                receipt = await verified.CommitVerifiedAsync(job.Run, test, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "PRODUCTION_VERDICT_COMMIT_EXCEPTION");
            }
            if (receipt.Run != job.Run || !receipt.Durable)
                return new(job.Run, ProductionState.AwaitingCommit, Verdict.Inconclusive,
                    false, false, "PRODUCTION_VERDICT_NOT_DURABLE");
            if (!receipt.EvidenceVerified || receipt.Verdict != test.Verdict ||
                test.Outcome != Outcome.Accepted || test.SimulationOnly ||
                test.Evidence.IsDefaultOrEmpty)
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    true, false, "PRODUCTION_VERDICT_NOT_PROVEN");
            return receipt.Verdict switch
            {
                Verdict.Pass => new(job.Run, ProductionState.Completed, Verdict.Pass,
                    true, false, "AWAIT_REAL_PRODUCTION_RELEASE_POLICY"),
                Verdict.Fail => new(job.Run, ProductionState.Failed, Verdict.Fail,
                    true, false, "RUN_FAILED"),
                _ => new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    true, false, "PRODUCTION_VERDICT_NOT_PROVEN")
            };
        }

        RecordCommitReceipt genericCommit;
        try { genericCommit = await _facts.CommitAsync(job.Run, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                false, false, "RECORD_COMMIT_EXCEPTION");
        }
        if (!genericCommit.Durable || genericCommit.Run != job.Run)
            return new(job.Run, ProductionState.AwaitingCommit, Verdict.Inconclusive,
                false, false, "RECORD_COMMIT_NOT_DURABLE");
        return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
            true, false, "PRODUCTION_VERIFIED_RECORD_COMMIT_NOT_CONNECTED");
    }

    public bool RequireCommit(ProductionRunResult result) => !result.Durable;
}
