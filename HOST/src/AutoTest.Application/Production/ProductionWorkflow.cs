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
    private readonly IProductionPhysicalStation? _physicalStation;

    public ProductionWorkflow(IProductionStageExecutor executor,
        ProductionResultCoordinator records, ProductionPreflight preflight,
        IProductionPhysicalStation? physicalStation = null)
    {
        _executor = executor;
        _records = records;
        _preflight = preflight;
        _physicalStation = physicalStation;
    }

    public ProductionPreflightResult Prepare(ProductionSession session,
        ProductionConfiguration configuration, ProductionStationReadiness station) =>
        _preflight.Validate(session, configuration, station);

    public async ValueTask<ProductionRunResult> ExecuteAsync(ProductionSession session,
        CancellationToken ct)
    {
        // Reject an unconnected physical station BEFORE entering Running,
        // writing a record, moving a clamp or touching ECU FLASH.
        if (session.Job is { IsMock: false } &&
            _physicalStation is null)
        {
            var pending = session.Job;
            return new(pending.Run, ProductionState.Ready, Verdict.Inconclusive,
                false, false, "PHYSICAL_STATION_ADAPTER_NOT_CONNECTED");
        }
        var begin = session.BeginExecution();
        if (begin.Outcome != Outcome.Accepted || session.Job is null)
            throw new InvalidOperationException("RUN_NOT_READY");
        var job = session.Job;
        // Persist the stage where physical side effects may have started.
        // This is an uncertainty marker, NOT a successful stage proof.
        var stage = "PREFLIGHT";
        try
        {
            if (!job.IsMock && !job.Configuration.RequiresFlash)
            {
                session.Mark(ProductionState.Unknown, "PRODUCTION_FLASH_REQUIRED");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "PRODUCTION_FLASH_REQUIRED");
            }
            var request = await _records.AppendAsync(job, "PRODUCTION_STARTED:" + job.DutIdentity,
                RunFactKind.Request, EvidenceQuality.HostObserved, ct).ConfigureAwait(false);
            if (request.Outcome != Outcome.Accepted)
            {
                session.Mark(ProductionState.Unknown, "RECORD_APPEND_UNCERTAIN");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive, false,
                    false, "RECORD_APPEND_UNCERTAIN");
            }
            if (!job.IsMock)
            {
                stage = "FIXTURE";
                // The fixture may move; persist intent before energizing any
                // output. An ACK alone is never physical position proof.
                var intent = await _records.AppendAsync(job,
                    "FIXTURE_PRESS_INTENT_EFFECT_UNKNOWN",
                    RunFactKind.Request, EvidenceQuality.HostObserved, ct)
                    .ConfigureAwait(false);
                if (intent.Outcome != Outcome.Accepted)
                {
                    session.Mark(ProductionState.Unknown,
                        "FIXTURE_INTENT_NOT_DURABLE");
                    return new(job.Run, ProductionState.Unknown,
                        Verdict.Inconclusive, false, false,
                        "FIXTURE_INTENT_NOT_DURABLE");
                }
                var fixture = await _physicalStation!.PressAndVerifyAsync(job, ct)
                    .ConfigureAwait(false);
                if (fixture.Run != job.Run || fixture.Station != job.Station ||
                    !fixture.MechanicalClosureVerified ||
                    !fixture.SafetyInterlockVerified ||
                    string.IsNullOrWhiteSpace(fixture.EvidenceReference))
                {
                    session.Mark(ProductionState.Unknown,
                        "MECHANICAL_FIXTURE_NOT_VERIFIED");
                    return new(job.Run, ProductionState.Unknown,
                        Verdict.Inconclusive, false, false,
                        "MECHANICAL_FIXTURE_NOT_VERIFIED");
                }
                var fixtureFact = await _records.AppendAsync(job,
                    "STATION_FIXTURE_VERIFIED:" + fixture.EvidenceReference,
                    RunFactKind.CommandAck, EvidenceQuality.HostObserved, ct)
                    .ConfigureAwait(false);
                if (fixtureFact.Outcome != Outcome.Accepted)
                {
                    session.Mark(ProductionState.Unknown,
                        "FIXTURE_PROOF_NOT_DURABLE");
                    return new(job.Run, ProductionState.Unknown,
                        Verdict.Inconclusive, false, false,
                        "FIXTURE_PROOF_NOT_DURABLE");
                }
                stage = "PHYSICAL_START";
                var start = await _physicalStation.WaitForStartAsync(
                    job, fixture, ct).ConfigureAwait(false);
                if (start.Run != job.Run || start.Station != job.Station ||
                    !start.FreshPhysicalEdgeVerified ||
                    string.IsNullOrWhiteSpace(start.EvidenceReference))
                {
                    session.Mark(ProductionState.Unknown,
                        "PHYSICAL_START_NOT_VERIFIED");
                    return new(job.Run, ProductionState.Unknown,
                        Verdict.Inconclusive, false, false,
                        "PHYSICAL_START_NOT_VERIFIED");
                }
                var startFact = await _records.AppendAsync(job,
                    "STATION_PHYSICAL_START_VERIFIED:" + start.EvidenceReference,
                    RunFactKind.CommandAck, EvidenceQuality.HostObserved, ct)
                    .ConfigureAwait(false);
                if (startFact.Outcome != Outcome.Accepted)
                {
                    session.Mark(ProductionState.Unknown,
                        "PHYSICAL_START_FACT_NOT_DURABLE");
                    return new(job.Run, ProductionState.Unknown,
                        Verdict.Inconclusive, false, false,
                        "PHYSICAL_START_FACT_NOT_DURABLE");
                }
            }
            if (job.Configuration.RequiresFlash)
            {
                stage = "FLASH";
                // Persist a pre-side-effect intent before any destructive ECU
                // programming. It is NOT proof that a byte was transferred.
                var flashStarted = await _records.AppendAsync(job,
                    "FLASH_STAGE_ENTERED_EFFECT_UNKNOWN",
                    RunFactKind.Request, EvidenceQuality.HostObserved, ct)
                    .ConfigureAwait(false);
                if (flashStarted.Outcome != Outcome.Accepted)
                {
                    session.Mark(ProductionState.Unknown, "FLASH_START_FACT_NOT_DURABLE");
                    return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                        false, false, "FLASH_START_FACT_NOT_DURABLE");
                }
                var flash = await _executor.ExecuteFlashAsync(job, ct).ConfigureAwait(false);
                // FLASH is destructive. A simulated stage, missing physical
                // evidence or a non-durable fact can NEVER authorize TEST.
                var flashProven = flash.Outcome == Outcome.Accepted &&
                    flash.Verdict == Verdict.Pass && !flash.SimulationOnly &&
                    !flash.Evidence.IsDefaultOrEmpty;
                var flashFact = await _records.AppendAsync(job, "FLASH:" + flash.Code,
                    flashProven ? RunFactKind.CommandAck : RunFactKind.Uncertainty,
                    flashProven ? EvidenceQuality.HostObserved : EvidenceQuality.Unknown,
                    ct, flash.Evidence).ConfigureAwait(false);
                if (flashFact.Outcome != Outcome.Accepted)
                {
                    session.Mark(ProductionState.Unknown, "FLASH_FACT_NOT_DURABLE");
                    return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                        false, false, "FLASH_FACT_NOT_DURABLE");
                }
                if (!flashProven)
                {
                    var reason = flash.SimulationOnly ? "FLASH_SIMULATED" :
                        flash.Evidence.IsDefaultOrEmpty ? "FLASH_EVIDENCE_MISSING" :
                        "FLASH_NOT_CONFIRMED";
                    session.Mark(ProductionState.Unknown, reason);
                    return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                        false, false, reason);
                }
            }
            stage = "TEST";
            var testStarted = await _records.AppendAsync(job,
                "TEST_STAGE_ENTERED", RunFactKind.Request,
                EvidenceQuality.HostObserved, ct).ConfigureAwait(false);
            if (testStarted.Outcome != Outcome.Accepted)
            {
                session.Mark(ProductionState.Unknown, "TEST_START_FACT_NOT_DURABLE");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "TEST_START_FACT_NOT_DURABLE");
            }
            var test = await _executor.ExecuteTestAsync(job, ct).ConfigureAwait(false);
            // A development/simulation judgement must not enter a physical
            // production result path. The caller still gets an explicit fact.
            var usableTest = test.Outcome == Outcome.Accepted && !test.SimulationOnly &&
                !test.Evidence.IsDefaultOrEmpty;
            var persisted = await _records.AppendAsync(job, "TEST:" + test.Code,
                usableTest ? RunFactKind.Judgement : RunFactKind.Uncertainty,
                usableTest ? EvidenceQuality.HostObserved : EvidenceQuality.Unknown,
                ct, test.Evidence).ConfigureAwait(false);
            if (persisted.Outcome != Outcome.Accepted)
            {
                session.Mark(ProductionState.Unknown, "TEST_FACT_NOT_DURABLE");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "TEST_FACT_NOT_DURABLE");
            }
            if (!job.IsMock && test.SimulationOnly)
            {
                session.Mark(ProductionState.Unknown, "TEST_SIMULATED");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "TEST_SIMULATED");
            }
            if (!job.IsMock && test.Evidence.IsDefaultOrEmpty)
            {
                session.Mark(ProductionState.Unknown, "TEST_EVIDENCE_MISSING");
                return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                    false, false, "TEST_EVIDENCE_MISSING");
            }
            stage = "RECORD_COMMIT";
            session.Mark(ProductionState.AwaitingCommit);
            var result = await _records.FinalizeAsync(job, test, ct).ConfigureAwait(false);
            if (result.State != ProductionState.AwaitingCommit)
                session.Mark(result.State, result.Code);
            return result;
        }
        catch (OperationCanceledException)
        {
            await TryAppendTerminalUncertaintyAsync(job,
                "RUN_CANCELLED_AT_" + stage);
            session.Mark(ProductionState.Aborted, "OPERATION_CANCELLED");
            return new(job.Run, ProductionState.Aborted, Verdict.Inconclusive,
                false, false, "OPERATION_CANCELLED");
        }
        catch (Exception)
        {
            // An exception cannot establish that hardware did nothing. Fail closed.
            await TryAppendTerminalUncertaintyAsync(job,
                "RUN_EXCEPTION_AT_" + stage);
            session.Mark(ProductionState.Unknown, "RUN_UNEXPECTED_EXCEPTION");
            return new(job.Run, ProductionState.Unknown, Verdict.Inconclusive,
                false, false, "RUN_UNEXPECTED_EXCEPTION");
        }
    }

    private async Task TryAppendTerminalUncertaintyAsync(
        ProductionJobSnapshot job, string stageCode)
    {
        // A cancelled operator token must not suppress the terminal
        // uncertainty fact. Use a bounded independent commit attempt; if
        // durable storage is unavailable, leave the run UNKNOWN/ABORTED
        // rather than claiming an inspection verdict.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _ = await _records.AppendAsync(job, stageCode,
                RunFactKind.Uncertainty, EvidenceQuality.Unknown,
                deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Production terminal uncertainty could not be persisted: " +
                stageCode + " " + error);
        }
    }

    public ValueTask RequestStopAsync(RunId id, CancellationToken ct) => _executor.RequestStopAsync(id, ct);
}
