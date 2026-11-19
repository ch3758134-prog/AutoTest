using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;

namespace AutoTest.Application.TestRecord;

/// <summary>
/// HOST-owned evidence capture must flush durable raw CAN/DBC/DEVICE bytes and
/// attach content-addressed artifacts before the run is finalized.
/// A failed finalization must never yield a committed PASS.
/// </summary>
public interface IRealRunEvidenceFinalizer
{
    ValueTask FinalizeEvidenceAsync(RunId run, CancellationToken ct);
}

/// <summary>
/// TEST -> existing RECORD database adapter. This is application orchestration,
/// not a new interpreter, recorder, result store or device port.
/// The caller supplies an already-validated real RunRecord (identity, project,
/// station, locked assets) and the existing TEST runner after station preflight.
/// </summary>
public sealed class RealTestRecordSession : IRealRunEventSink
{
    private readonly RunRecordWriter _writer;
    private readonly RunReportExporter _reports;
    private readonly RunRecord _record;
    private readonly IRealRunEvidenceFinalizer? _evidenceFinalizer;
    private readonly Dictionary<Guid, RealStepStarted> _active = new();
    private readonly Dictionary<StepId, int> _attempts = new();
    private bool _begun;
    private bool _completed;

    public RealTestRecordSession(RunRecord record, RunRecordWriter writer, RunReportExporter reports,
        IRealRunEvidenceFinalizer? evidenceFinalizer = null)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _evidenceFinalizer = evidenceFinalizer;
        record.Validate(); // Never fabricate missing DUT/station/plan identity.
    }

    public RunId RunId => _record.RunId;

    /// <summary>
    /// The host must finish device/DBC/plan safety preflight before entering this method.
    /// Begin is durable before any step can reach physical TEST action ports.
    /// A run failing to persist never starts the interpreter.
    /// </summary>
    public async Task<RealTestRecordOutcome> ExecuteAsync(RealRunCoordinator runner,
        PreparedTestPlan prepared, string reportDirectory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);
        if (_begun) throw new InvalidOperationException("This record session is single-use.");
        if (!prepared.ValidateFingerprint() || !prepared.Diagnostics.IsEmpty ||
            !string.Equals(_record.Snapshot.PlanFingerprint.Sha256, prepared.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The verified TEST plan must match the immutable RunSnapshot.");

        await _writer.BeginAsync(_record, ct).ConfigureAwait(false);
        _begun = true;
        // The coordinator awaits each StepStarted/StepCompleted callback and always
        // attempts scripted/station cleanup after errors or operator stop.
        RealRunResult result;
        try
        {
            result = await runner.StartAsync(prepared, RunId, this, ct).ConfigureAwait(false);
        }
        catch (Exception runtimeException)
        {
            // The TEST interpreter normally converts step exceptions to Error and runs
            // cleanup. For unexpected failures outside that boundary, fail closed:
            // persist Unknown if RECORD is still available, never create a PASS.
            // If SQLite itself failed, leave Writing for recovery at exclusive startup.
            if (!_completed)
            {
                try
                {
                    await AppendAuditAsync(Guid.NewGuid(), RunFactKind.Uncertainty,
                        "TEST_UNEXPECTED_TERMINATION " + runtimeException.GetType().Name,
                        EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty,
                        CancellationToken.None).ConfigureAwait(false);
                    var utc = DateTimeOffset.UtcNow;
                    if (utc < _record.StartedUtc) utc = _record.StartedUtc;
                    var receipt = await _writer.CompleteAsync(new RunCompletion(RunId, utc,
                        RunState.Unknown, Verdict.Error, "TEST_UNEXPECTED_TERMINATION"),
                        CancellationToken.None).ConfigureAwait(false);
                    _completed = receipt.Durable && receipt.Run == RunId;
                }
                catch
                {
                    // The existing persisted prefix is still authoritative.
                    // Uncommitted runs are marked Interrupted/Unknown on restart.
                }
            }
            if (_completed)
            {
                try
                {
                    await _reports.ExportFilesAsync(RunId, reportDirectory, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Report export can be retried from the database by RunId.
                }
            }
            throw new IOException("TEST ended unexpectedly. Inspect RECORD RunId=" + RunId,
                runtimeException);
        }
        if (!_completed)
            throw new IOException("TEST ended without a durable RECORD completion.");
        // Export is a separate derived view; database facts remain authoritative.
        var files = await _reports.ExportFilesAsync(RunId, reportDirectory, CancellationToken.None)
            .ConfigureAwait(false);
        return new RealTestRecordOutcome(result, files);
    }

    public async ValueTask StepStartedAsync(RealStepStarted started, CancellationToken ct)
    {
        EnsureWriting(started.Run);
        if (started.Step.Value == Guid.Empty || started.StepOccurrenceId == Guid.Empty ||
            !_active.TryAdd(started.StepOccurrenceId, started))
            throw new InvalidDataException("Duplicate or invalid StepOccurrenceId.");
        // A durable START fact precedes any physical step action. No async queue.
        await AppendAuditAsync(started.StepOccurrenceId, RunFactKind.Request,
            "STEP_BEGIN " + JsonSerializer.Serialize(new
            {
                stepId = started.Step.ToString(),
                occurrence = started.StepOccurrenceId,
                parent = started.ParentOccurrenceId,
                phase = started.Phase.ToString(),
                startedUtc = started.StartedUtc,
                path = started.ExecutionPath,
                stepType = started.Type
            }), EvidenceQuality.HostObserved, ImmutableArray<EvidenceRef>.Empty,
            ct).ConfigureAwait(false);
    }

    public async ValueTask StepCompletedAsync(RunId run, RealStepFact completed, CancellationToken ct)
    {
        EnsureWriting(run);
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.Step.Value == Guid.Empty || completed.StepOccurrenceId == Guid.Empty ||
            completed.StartedUtc is null || completed.CompletedUtc is null ||
            completed.Duration is null || completed.Phase is null)
            throw new InvalidDataException("A completed actual step requires occurrence, phase and time bounds.");
        // All occurrences, including skipped steps, must have a durably recorded START.
        // A duplicate completion is rejected after the active entry is removed.
        if (!_active.TryGetValue(completed.StepOccurrenceId, out var opened) ||
            opened.Step != completed.Step || opened.Phase != completed.Phase ||
            opened.StartedUtc != completed.StartedUtc)
            throw new InvalidDataException("Step completion lacks a matching durable begin.");

        var attempt = _attempts.GetValueOrDefault(completed.Step) + 1;
        var observed = completed.Observation;
        var evidence = observed is not null && observed.Evidence.Value != Guid.Empty
            ? ImmutableArray.Create(observed.Evidence) : ImmutableArray<EvidenceRef>.Empty;
        var quality = observed?.Quality ?? EvidenceQuality.Unknown;
        // The audit TimePoint describes persistence acceptance, whereas the
        // step's CompletedUtc is the execution-time boundary recorded separately.
        var instant = TimePoint.FromHostArrival(DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());
        var step = new RunStepFact(run, Guid.NewGuid(), completed.Step, attempt,
            completed.Status.ToString(), completed.Expected, completed.Actual,
            instant, quality, evidence)
        {
            StepOccurrenceId = completed.StepOccurrenceId,
            ParentOccurrenceId = completed.ParentOccurrenceId,
            Phase = completed.Phase.ToString(),
            ExecutionPath = completed.ExecutionPath,
            LoopIteration = completed.LoopIteration,
            StartedUtc = completed.StartedUtc,
            CompletedUtc = completed.CompletedUtc,
            Duration = completed.Duration,
            ObservationUtc = observed?.Time.Utc,
            SignalProvenance = observed is null ? null : new RunSignalProvenance(
                observed.Signal.Role.ToString(), observed.Signal.FrameId,
                observed.Signal.SignalName, observed.Unit,
                observed.Signal.DbcAsset.Id.Value,
                observed.Signal.DbcAsset.Revision.Value,
                observed.Signal.DbcAsset.Fingerprint.Sha256,
                observed.Time.Utc, observed.Time.MonotonicTicks,
                observed.Time.Source.ToString(), observed.Time.Quality.ToString()),
            Unit = observed?.Unit,
            SignalStatus = completed.ObservationStatus?.ToString(),
            FailureCode = completed.Status switch
            {
                RealStepStatus.NoData => "NO_DATA",
                RealStepStatus.Timeout => "TIMEOUT",
                RealStepStatus.InvalidData when
                    completed.Detail.Contains("SIGNAL_GAP", StringComparison.Ordinal) => "SIGNAL_GAP",
                RealStepStatus.InvalidData => "INVALID_DATA",
                RealStepStatus.ConditionFalse => "CONDITION_FALSE",
                RealStepStatus.DeviceError => "DEVICE_ERROR",
                RealStepStatus.Cancelled => "CANCELLED",
                _ => null
            },
            Detail = completed.Detail.Length > 4096 ? completed.Detail[..4096] : completed.Detail,
            RequirementId = completed.RequirementId,
            ReportNote = completed.ReportNote,
            ShowEvidence = completed.ShowEvidence,
            AdditionalResultsJson = completed.AdditionalResults.Count == 0 ? null :
                JsonSerializer.Serialize(completed.AdditionalResults)
        };
        // One durable STEP row per occurrence. The report consumes this same row.
        await _writer.AppendStepAsync(step, ct).ConfigureAwait(false);
        _attempts[completed.Step] = attempt;
        // Device acknowledgement, read completion and delay are NOT DUT verdicts.
        var isAssertion = completed.Type is "Expect" or "WaitUntil" or "AssertSignal";
        var eventKind = completed.Status switch
        {
            RealStepStatus.Completed or RealStepStatus.ConditionFalse when isAssertion
                => RunFactKind.Judgement,
            RealStepStatus.ConditionFalse or RealStepStatus.NoData or
            RealStepStatus.InvalidData or RealStepStatus.Timeout or
            RealStepStatus.DeviceError or RealStepStatus.Cancelled => RunFactKind.Uncertainty,
            _ => RunFactKind.RecordWrite
        };
        await AppendAuditAsync(completed.StepOccurrenceId, eventKind,
            "STEP_END " + JsonSerializer.Serialize(new
            {
                stepId = completed.Step.ToString(),
                occurrence = completed.StepOccurrenceId,
                attempt,
                disposition = completed.Status.ToString(),
                detail = completed.Detail.Length > 2048 ? completed.Detail[..2048] : completed.Detail
            }), quality, evidence, ct).ConfigureAwait(false);
        _active.Remove(completed.StepOccurrenceId);
    }

    public async ValueTask RunCompletedAsync(RealRunResult completed, CancellationToken ct)
    {
        EnsureWriting(completed.Run);
        // System-level failure and cleanup facts have no authored StepId.
        // They belong to audit stream rather than fictitious RunStepFact rows.
        foreach (var systemFact in completed.Facts.Where(f => f.Step.Value == Guid.Empty))
            await AppendAuditAsync(Guid.NewGuid(), RunFactKind.Uncertainty,
                "SYSTEM " + systemFact.Type + ": " + systemFact.Detail,
                EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct)
                .ConfigureAwait(false);

        // ArtifactRepository links are immutable and may be attached only while
        // the run header is Writing. A failed flush/attach aborts normal completion.
        // The host supplies real capture; this backend never invents evidence bytes.
        if (_evidenceFinalizer is not null)
            await _evidenceFinalizer.FinalizeEvidenceAsync(RunId, ct).ConfigureAwait(false);

        await AppendAuditAsync(Guid.NewGuid(),
            completed.Verdict is RealTestVerdict.Passed or RealTestVerdict.Failed
                ? RunFactKind.Judgement : RunFactKind.Uncertainty,
            "RUN_TERMINAL " + completed.Verdict,
            EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct).ConfigureAwait(false);

        var terminal = completed.Verdict switch
        {
            RealTestVerdict.Passed => RunState.Completed,
            RealTestVerdict.Failed => RunState.Failed,
            RealTestVerdict.Aborted => RunState.Aborted,
            _ => RunState.Unknown
        };
        var verdict = completed.Verdict switch
        {
            RealTestVerdict.Passed => Verdict.Pass,
            RealTestVerdict.Failed => Verdict.Fail,
            RealTestVerdict.Error => Verdict.Error,
            _ => Verdict.Inconclusive
        };
        var reason = completed.Verdict switch
        {
            RealTestVerdict.Passed => null,
            RealTestVerdict.Failed => "TEST_ASSERTION_FAILED",
            RealTestVerdict.Aborted => "TEST_OPERATOR_ABORTED",
            RealTestVerdict.Error => "TEST_EXECUTION_ERROR",
            _ => "TEST_NO_CONCLUSIVE_ECU_VERDICT"
        };
        var finish = DateTimeOffset.UtcNow;
        if (finish < _record.StartedUtc) finish = _record.StartedUtc;
        var receipt = await _writer.CompleteAsync(
            new RunCompletion(RunId, finish, terminal, verdict, reason), ct)
            .ConfigureAwait(false);
        if (!receipt.Durable || receipt.Run != RunId)
            throw new IOException("RunCompletion was not durably committed.");
        _completed = true;
    }

    private void EnsureWriting(RunId run)
    {
        if (!_begun || _completed || run != RunId)
            throw new InvalidOperationException("Real TEST record session has not been begun or is finalized.");
    }

    private async Task AppendAuditAsync(Guid correlation, RunFactKind kind,
        string description, EvidenceQuality quality, ImmutableArray<EvidenceRef> evidence,
        CancellationToken ct)
    {
        if (description.Length > 32768)
            description = description[..32768];
        var fact = new RunFact(RunId, Guid.NewGuid(), correlation, kind, description,
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()),
            quality, evidence);
        var receipt = await _writer.AppendAsync(fact, ct).ConfigureAwait(false);
        if (!receipt.Durable || receipt.Run != RunId || receipt.EventId != fact.EventId)
            throw new IOException("A mandatory TEST execution fact was not durable.");
    }
}

public sealed record RealTestRecordOutcome(RealRunResult Result, ExportedRunReportFiles Reports);
