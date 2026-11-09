using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

// TEST/FLASH each own their engine. Application only invokes their explicit adapters.
public interface IProductionStageExecutor
{
    ValueTask<ProductionStageResult> ExecuteFlashAsync(ProductionJobSnapshot job, CancellationToken ct);
    ValueTask<ProductionStageResult> ExecuteTestAsync(ProductionJobSnapshot job, CancellationToken ct);
    ValueTask RequestStopAsync(RunId run, CancellationToken ct);
}

public sealed class ProductionWorkflow
{
    private readonly IProductionStageExecutor _executor;
    private readonly ProductionResultCoordinator _records;
    private readonly ProductionPreflight _preflight;
    public ProductionWorkflow(IProductionStageExecutor executor,
        ProductionResultCoordinator records, ProductionPreflight preflight)
    { _executor = executor; _records = records; _preflight = preflight; }

    public ProductionPreflightResult Prepare(ProductionSession session,
        ProductionConfiguration configuration, ProductionStationReadiness station) =>
        _preflight.Validate(session, configuration, station);

    public async ValueTask<ProductionRunResult> ExecuteAsync(ProductionSession session,
        CancellationToken ct)
    {
        var begin = session.BeginExecution();
        if (begin.Outcome != Outcome.Accepted || session.Job is null)
            throw new InvalidOperationException("RUN_NOT_READY");
        var job = session.Job;
        try
        {
            var request = await _records.AppendAsync(job, "PRODUCTION_STARTED:" + job.DutIdentity,
                RunFactKind.Request, EvidenceQuality.HostObserved, ct).ConfigureAwait(false);
            if (request.Outcome != Outcome.Accepted)
            {
                session.Mark(ProductionState.Unknown, "RECORD_APPEND_UNCERTAIN");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive, false,
                    false, "RECORD_APPEND_UNCERTAIN");
            }
            if (job.Configuration.RequiresFlash)
            {
                var flash = await _executor.ExecuteFlashAsync(job, ct).ConfigureAwait(false);
                await _records.AppendAsync(job, "FLASH:" + flash.Code,
                    flash.Outcome == Outcome.Accepted ? RunFactKind.MockObservation : RunFactKind.Uncertainty,
                    EvidenceQuality.Unknown, ct).ConfigureAwait(false);
                if (flash.Outcome != Outcome.Accepted || flash.Verdict != Verdict.Pass)
                {
                    session.Mark(ProductionState.Unknown, "FLASH_NOT_CONFIRMED");
                    return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                        false, false, "FLASH_NOT_CONFIRMED");
                }
            }
            var test = await _executor.ExecuteTestAsync(job, ct).ConfigureAwait(false);
            var persisted = await _records.AppendAsync(job, "TEST:" + test.Code,
                RunFactKind.Judgement, test.SimulationOnly ? EvidenceQuality.Derived :
                    EvidenceQuality.Unknown, ct).ConfigureAwait(false);
            if (persisted.Outcome != Outcome.Accepted)
            {
                session.Mark(ProductionState.Unknown, "TEST_FACT_NOT_DURABLE");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "TEST_FACT_NOT_DURABLE");
            }
            session.Mark(ProductionState.AwaitingCommit);
            var result = await _records.FinalizeAsync(job, test, ct).ConfigureAwait(false);
            if (result.State != ProductionState.AwaitingCommit)
                session.Mark(result.State, result.Code);
            return result;
        }
        catch (OperationCanceledException)
        {
            session.Mark(ProductionState.Aborted, "OPERATION_CANCELLED");
            return new(job.Run, ProductionState.Aborted, Verdict.Inconclusive,
                false, false, "OPERATION_CANCELLED");
        }
        catch (Exception)
        {
            // An exception cannot establish that hardware did nothing. Fail closed.
            session.Mark(ProductionState.Unknown, "RUN_UNEXPECTED_EXCEPTION");
            return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                false, false, "RUN_UNEXPECTED_EXCEPTION");
        }
    }

    public ValueTask RequestStopAsync(RunId id, CancellationToken ct) => _executor.RequestStopAsync(id, ct);
}
