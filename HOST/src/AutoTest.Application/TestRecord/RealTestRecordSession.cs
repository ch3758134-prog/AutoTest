using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;

namespace AutoTest.Application.TestRecord;

/// <summary>
/// HOST-owned evidence capture must flush durable raw CAN/DBC/DEVICE bytes and
/// attach content-addressed artifacts before the run is finalized.
/// A failed finalization must never yield a committed PASS.
/// </summary>
/// <summary>Actual recording health, not a claim inferred from a filename or ACK.</summary>
public sealed record RunEvidenceCaptureResult(bool RecordingComplete, long DroppedEvents,
    string? IncompleteReason)
{
    public void Validate()
    {
        if (DroppedEvents < 0 || (RecordingComplete && DroppedEvents != 0) ||
            (IncompleteReason?.Length ?? 0) > 1024)
            throw new InvalidDataException("Invalid raw evidence capture health.");
    }
}

public interface IRealRunEvidenceFinalizer
{
    /// <summary>
    /// Finish the real raw capture, fsync its bytes and attach each referenced
    /// EvidenceRef via ArtifactRepository before the run is committed.
    /// Do not claim complete unless the capture was active for the entire run.
    /// </summary>
    void ObserveCompletedRun(RealRunResult completed) { }
    ValueTask<RunEvidenceCaptureResult> FinalizeEvidenceAsync(RunId run, CancellationToken ct);
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
    private readonly Dictionary<StepId, TestTargetMapping> _frozenJudgementTargets = new();
    public Guid CaseAttemptId { get; } = Guid.NewGuid();
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
    /// A direct ECU judgement can cite only the DBC signal pinned in the same
    /// compiled TEST document and observed in that exact step occurrence.
    /// </summary>
    private bool MatchesFrozenJudgement(RealStepFact fact, SignalObservation observed)
    {
        if (!_frozenJudgementTargets.TryGetValue(fact.Step, out var target) ||
            target.Kind != TestTargetKind.Signal) return false;
        var signal = observed.Signal;
        return signal.DbcAsset.Id == target.Asset &&
            signal.DbcAsset.Revision.Value == target.AssetRevision &&
            string.Equals(signal.DbcAsset.Fingerprint.Sha256, target.AssetSha256,
                StringComparison.OrdinalIgnoreCase) &&
            signal.Role == target.Role && signal.FrameId == target.CanId &&
            string.Equals(signal.SignalName, target.SignalName, StringComparison.Ordinal) &&
            string.Equals(signal.Unit, target.Unit, StringComparison.Ordinal) &&
            string.Equals(observed.Unit, target.Unit, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host must finish device/DBC/plan safety preflight before entering this method.
    /// Begin is durable before any step can reach physical TEST action ports.
    /// A run failing to persist never starts the interpreter.
    /// </summary>
    public async Task<RealTestRecordOutcome> ExecuteAsync(RealRunCoordinator runner,
        PreparedTestPlan prepared, string reportDirectory, CancellationToken ct = default,
        bool retainProjectActuation = false,
        IDictionary<string, TestDatum>? projectVariables = null)
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
            await AppendAuditAsync(RunId.Value, RunFactKind.Request,
                "RUN_BEGIN " + JsonSerializer.Serialize(new
                {
                    runId = RunId.ToString(),
                    mode = _record.Mode.ToString(),
                    planSha256 = prepared.Fingerprint,
                    startedUtc = _record.StartedUtc
                }), EvidenceQuality.HostObserved, ImmutableArray<EvidenceRef>.Empty,
                ct).ConfigureAwait(false);
            var doc = prepared.RestoreDocument();
            var settings = doc.ReportSettings.Validate();
            var mappingByName = doc.Mappings.ToDictionary(x => x.Name,
                StringComparer.OrdinalIgnoreCase);
            foreach (var node in doc.Walk())
            {
                if (node.Type is not ("AssertSignal" or "WaitUntil") ||
                    !StepExecutionPolicy.IsEnabled(node)) continue;
                if (node.Properties.TryGetValue("target", out var name) &&
                    mappingByName.TryGetValue(name, out var target) &&
                    target.Kind == TestTargetKind.Signal)
                    _frozenJudgementTargets.Add(node.Id, target);
            }
            await AppendAuditAsync(CaseAttemptId, RunFactKind.RecordWrite,
                "TEST_CONTEXT " + JsonSerializer.Serialize(new
                {
                    caseAttemptId = CaseAttemptId,
                    testDocumentId = doc.Id.ToString(),
                    testName = doc.Name,
                    reportTitle = string.IsNullOrWhiteSpace(settings.Title) ? doc.Name : settings.Title,
                    caseNumber = settings.CaseNumber,
                    description = settings.Description,
                    preparedPlanSha256 = prepared.Fingerprint
                }), EvidenceQuality.HostObserved, ImmutableArray<EvidenceRef>.Empty,
                ct).ConfigureAwait(false);
            result = await runner.StartAsync(prepared, RunId, this, ct,
                retainProjectActuation, projectVariables).ConfigureAwait(false);
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
                caseAttemptId = CaseAttemptId,
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
        // Only a real FLASH capture reference (created at the hardware boundary)
        // may identify a FLASH artifact; a raw Boot ACK never makes a verdict.
        var flashEvidence = completed.Type == "Flash" &&
            Guid.TryParse(completed.Evidence, out var flashId) && flashId != Guid.Empty
                ? (EvidenceRef?)new EvidenceRef(flashId) : null;
        // Wave steps return evidence IDs from the immutable, checksum-verified
        // WaveEvidenceStore. They are archived by the Desktop finalizer; no
        // synthetic CAN observation or direct DBC claim is created here.
        var waveRefs = ImmutableArray.CreateBuilder<EvidenceRef>();
        if (completed.Type is "WaveStop" or "WaveAnalyze" or "WaveCompare")
            foreach (var token in (completed.Evidence ?? "").Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Guid.TryParse(token, out var waveId) && waveId != Guid.Empty)
                    waveRefs.Add(new EvidenceRef(waveId));
        var evidence = observed is not null && observed.Evidence.Value != Guid.Empty
            ? ImmutableArray.Create(observed.Evidence)
            : flashEvidence is { } physicalFlash
                ? ImmutableArray.Create(physicalFlash) : waveRefs.ToImmutable();
        var quality = observed?.Quality ??
            (flashEvidence is not null || waveRefs.Count > 0
                ? EvidenceQuality.HostObserved : EvidenceQuality.Unknown);
        // An observation in a Read (or an unrelated earlier step) cannot turn an
        // Expect literal into a proved ECU judgement. Tag only the occurrence
        // that compared this exact frozen DBC signal and actually passed.
        var directJudgement = completed.Phase == TestPhase.Test &&
            (completed.Type is "AssertSignal" or "WaitUntil") &&
            completed.Status == RealStepStatus.Completed &&
            completed.ObservationStatus == TestSignalStatus.Valid &&
            observed is { Value: double measured, HasGap: false } &&
            string.Equals(completed.Actual,
                measured.ToString("R", CultureInfo.InvariantCulture),
                StringComparison.Ordinal) &&
            observed.Evidence.Value != Guid.Empty &&
            (quality is EvidenceQuality.HostObserved or EvidenceQuality.DirectMeasured ||
                quality == EvidenceQuality.Derived &&
                observed.Time.Source == TimeSource.HostArrival &&
                observed.Time.Quality == TimeQuality.HostArrivalOnly) &&
            !string.IsNullOrWhiteSpace(completed.Actual) &&
            MatchesFrozenJudgement(completed, observed);
        // The audit TimePoint describes persistence acceptance, whereas the
        // step's CompletedUtc is the execution-time boundary recorded separately.
        var instant = TimePoint.FromHostArrival(DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());
        var step = new RunStepFact(run, Guid.NewGuid(), completed.Step, attempt,
            completed.Status.ToString(), completed.Expected, completed.Actual,
            instant, quality, evidence)
        {
            StepOccurrenceId = completed.StepOccurrenceId,
            CaseAttemptId = CaseAttemptId,
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
            JudgementEvidenceKind = directJudgement ? "DirectDbcSignal" :
                completed.Type == "WaveCompare" &&
                completed.Status == RealStepStatus.Completed &&
                waveRefs.Count == 2 ? "WaveComparisonPendingArchive" : null,
            FailureCode = completed.Status switch
            {
                RealStepStatus.NoData when
                    completed.Detail.Contains("DBC_SNAPSHOT_MISMATCH", StringComparison.Ordinal)
                    => "DBC_SNAPSHOT_MISMATCH",
                RealStepStatus.NoData => "NO_DATA",
                RealStepStatus.Timeout => "TIMEOUT",
                RealStepStatus.InvalidData when
                    completed.Detail.Contains("DBC_SNAPSHOT_MISMATCH", StringComparison.Ordinal)
                    => "DBC_SNAPSHOT_MISMATCH",
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
        var isAssertion = completed.Type is "Expect" or "WaitUntil" or "AssertSignal" or "WaveCompare";
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
                judgementEvidenceKind = step.JudgementEvidenceKind,
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
        _evidenceFinalizer?.ObserveCompletedRun(completed);
        var capture = _evidenceFinalizer is null
            ? new RunEvidenceCaptureResult(false, 0, "RAW_CAPTURE_FINALIZER_NOT_CONNECTED")
            : await _evidenceFinalizer.FinalizeEvidenceAsync(RunId, ct)
                .ConfigureAwait(false);
        capture.Validate();
        await AppendAuditAsync(Guid.NewGuid(), RunFactKind.RecordWrite,
            "EVIDENCE_CAPTURE " + JsonSerializer.Serialize(capture),
            capture.RecordingComplete ? EvidenceQuality.HostObserved : EvidenceQuality.Unknown,
            ImmutableArray<EvidenceRef>.Empty, ct).ConfigureAwait(false);

        // The TEST verdict is a fact about the configured assertions.
        // A formal recorded ECU PASS additionally requires an actual trace and
        // all referenced evidence bytes, verified from the original artifact store.
        var evidenceSnapshot = await _reports.ExportSummaryAsync(RunId, ct)
            .ConfigureAwait(false);
        var referenced = evidenceSnapshot.StepFacts.SelectMany(x => x.Evidence)
            .Concat(evidenceSnapshot.Facts.SelectMany(x => x.Evidence))
            .Select(x => x.Value).Distinct().ToArray();
        var verified = evidenceSnapshot.Artifacts.Where(x =>
            x.Integrity == ArtifactIntegrity.Present).Select(x => x.ArtifactId)
            .ToHashSet();
        var orphaned = referenced.Where(x => !verified.Contains(x)).ToArray();
        var corrupt = evidenceSnapshot.Artifacts.Any(x =>
            x.Integrity != ArtifactIntegrity.Present);
        // A PASS must be justified by its own successful ECU assertion(s).
        // Join actual TEST assertions to durable StepFacts using StepOccurrenceId
        // AND StepId/CaseAttemptId; no global Any(valid signal) is sufficient.
        // Read -> Expect($variable) lacks a P0 variable-dependency proof chain,
        // so Expect is deliberately NOT eligible for formal ECU PASS.
        var successfulAssertions = completed.Facts.Where(f =>
            f.Phase == TestPhase.Test && f.Status == RealStepStatus.Completed &&
            (f.Type is "AssertSignal" or "WaitUntil" or "Expect" or "WaveCompare")).ToArray();
        var byOccurrence = evidenceSnapshot.StepFacts
            .Where(x => x.StepOccurrenceId.HasValue)
            .GroupBy(x => x.StepOccurrenceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToArray());

        bool HasDirectJudgementProof(RealStepFact executed)
        {
            if (executed.Type is not ("AssertSignal" or "WaitUntil") ||
                executed.StepOccurrenceId == Guid.Empty ||
                executed.ObservationStatus != TestSignalStatus.Valid ||
                executed.Observation is not { Value: double measured, HasGap: false } sample ||
                !string.Equals(executed.Actual,
                    measured.ToString("R", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal) ||
                sample.Evidence.Value == Guid.Empty ||
                !MatchesFrozenJudgement(executed, sample) ||
                (sample.Quality == EvidenceQuality.Derived &&
                    (sample.Time.Source != TimeSource.HostArrival ||
                     sample.Time.Quality != TimeQuality.HostArrivalOnly)) ||
                !verified.Contains(sample.Evidence.Value) ||
                !byOccurrence.TryGetValue(executed.StepOccurrenceId, out var rows) ||
                rows.Length != 1)
                return false;
            var persisted = rows[0];
            return persisted.Step == executed.Step &&
                persisted.CaseAttemptId == CaseAttemptId &&
                persisted.Phase == "Test" &&
                persisted.Disposition == nameof(RealStepStatus.Completed) &&
                persisted.JudgementEvidenceKind == "DirectDbcSignal" &&
                persisted.SignalStatus == nameof(TestSignalStatus.Valid) &&
                persisted.Expected == executed.Expected &&
                persisted.Actual == executed.Actual &&
                persisted.ObservationUtc == sample.Time.Utc &&
                persisted.Quality == sample.Quality &&
                persisted.Evidence.Length == 1 &&
                persisted.Evidence[0] == sample.Evidence &&
                persisted.SignalProvenance is { } origin &&
                origin.DbcAssetId == sample.Signal.DbcAsset.Id.Value &&
                origin.DbcRevision == sample.Signal.DbcAsset.Revision.Value &&
                string.Equals(origin.DbcSha256,
                    sample.Signal.DbcAsset.Fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase) &&
                origin.FrameId == sample.Signal.FrameId &&
                origin.BusRole == sample.Signal.Role.ToString() &&
                origin.SignalName == sample.Signal.SignalName &&
                origin.Unit == sample.Unit;
        }
        bool HasArchivedWaveJudgementProof(RealStepFact executed)
        {
            if (executed.Type != "WaveCompare" ||
                executed.StepOccurrenceId == Guid.Empty ||
                executed.Status != RealStepStatus.Completed ||
                executed.Actual != "Pass" || executed.Expected != "Pass" ||
                !byOccurrence.TryGetValue(executed.StepOccurrenceId, out var rows) ||
                rows.Length != 1)
                return false;
            var tokens = (executed.Evidence ?? "").Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length != 2 ||
                !Guid.TryParse(tokens[0], out var captureId) ||
                !Guid.TryParse(tokens[1], out var comparisonId) ||
                captureId == Guid.Empty || comparisonId == Guid.Empty ||
                captureId == comparisonId ||
                !verified.Contains(captureId) || !verified.Contains(comparisonId))
                return false;
            // The physical finalizer already verified each immutable source
            // file and recomputed a PASS comparison against this exact capture.
            var persisted = rows[0];
            return persisted.Step == executed.Step &&
                persisted.CaseAttemptId == CaseAttemptId &&
                persisted.Phase == "Test" &&
                persisted.Disposition == nameof(RealStepStatus.Completed) &&
                persisted.JudgementEvidenceKind == "WaveComparisonPendingArchive" &&
                persisted.Quality == EvidenceQuality.HostObserved &&
                persisted.Expected == executed.Expected &&
                persisted.Actual == executed.Actual &&
                persisted.Evidence.Length == 2 &&
                persisted.Evidence[0].Value == captureId &&
                persisted.Evidence[1].Value == comparisonId;
        }

        var provenCount = successfulAssertions.Count(f =>
            HasDirectJudgementProof(f) || HasArchivedWaveJudgementProof(f));
        var unprovenCount = successfulAssertions.Length - provenCount;
        var formalPassEligible = capture.RecordingComplete &&
            capture.DroppedEvents == 0 && orphaned.Length == 0 && !corrupt &&
            referenced.Length > 0 &&
            successfulAssertions.Length > 0 && unprovenCount == 0;
        if (completed.Verdict == RealTestVerdict.Passed && !formalPassEligible)
            await AppendAuditAsync(Guid.NewGuid(), RunFactKind.Uncertainty,
                "EVIDENCE_GATE_NOT_MET " + JsonSerializer.Serialize(new
                {
                    capture.RecordingComplete,
                    capture.DroppedEvents,
                    capture.IncompleteReason,
                    unarchivedEvidenceCount = orphaned.Length,
                    corruptArtifact = corrupt,
                    successfulAssertionCount = successfulAssertions.Length,
                    provenDirectDbcOrVerifiedWaveAssertions = provenCount,
                    unprovenAssertions = unprovenCount
                }), EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct)
                .ConfigureAwait(false);

        var actionOnlyComplete = completed.Verdict == RealTestVerdict.CompletedNoAssertion &&
            capture.RecordingComplete && capture.DroppedEvents == 0 &&
            orphaned.Length == 0 && !corrupt;
        await AppendAuditAsync(Guid.NewGuid(),
            completed.Verdict is RealTestVerdict.Passed or RealTestVerdict.Failed
                ? RunFactKind.Judgement : RunFactKind.Uncertainty,
            "RUN_TERMINAL " + completed.Verdict,
            EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct).ConfigureAwait(false);

        var terminal = completed.Verdict == RealTestVerdict.Passed && !formalPassEligible
            ? RunState.Unknown : completed.Verdict switch
        {
            RealTestVerdict.Passed => RunState.Completed,
            RealTestVerdict.CompletedNoAssertion when actionOnlyComplete => RunState.Completed,
            RealTestVerdict.Failed => RunState.Failed,
            RealTestVerdict.Aborted => RunState.Aborted,
            _ => RunState.Unknown
        };
        var verdict = completed.Verdict == RealTestVerdict.Passed && !formalPassEligible
            ? Verdict.Inconclusive : completed.Verdict switch
        {
            RealTestVerdict.Passed => Verdict.Pass,
            RealTestVerdict.Failed => Verdict.Fail,
            RealTestVerdict.Error => Verdict.Error,
            _ => Verdict.Inconclusive
        };
        var reason = completed.Verdict == RealTestVerdict.Passed && !formalPassEligible
            ? "EVIDENCE_GATE_NOT_MET" : completed.Verdict switch
        {
            RealTestVerdict.Passed => null,
            RealTestVerdict.CompletedNoAssertion when actionOnlyComplete =>
                "TEST_ACTIONS_COMPLETED_WITHOUT_PRODUCT_ASSERTION",
            RealTestVerdict.CompletedNoAssertion => "TEST_ACTION_EVIDENCE_INCOMPLETE",
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
