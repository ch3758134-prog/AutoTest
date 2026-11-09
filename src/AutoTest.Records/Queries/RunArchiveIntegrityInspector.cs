using System.Text.Json;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;

namespace AutoTest.Records.Queries;

/// <summary>
/// Inspects persisted record and referenced evidence. This is NOT a physical release check.
/// No RunRecord/RunFact/evidence is modified by an inspection.
/// </summary>
public enum RunArchiveIntegrityStatus
{
    NotFound,
    RecordCorrupt,
    EvidenceCorrupt,
    EvidenceMissing,
    NotCommitted,
    MetadataOnly,
    EvidenceVerified
}

public sealed record RunArchiveIntegrityIssue(string Code, string Explanation, bool Blocking);

public sealed record RunArchiveIntegrityResult(
    RunId Run,
    RunArchiveIntegrityStatus Status,
    RecordRunMode? Mode,
    RecordPersistenceState? Persistence,
    string? Verdict,
    long FactCount,
    long StepFactCount,
    int VerifiedArtifacts,
    int MissingArtifacts,
    int CorruptArtifacts,
    DateTimeOffset CheckedUtc,
    IReadOnlyList<RunArchiveIntegrityIssue> Issues)
{
    /// <summary>Neither PASS nor a valid archive is evidence of a released physical ECU.</summary>
    public string PhysicalReleaseAssessment => "NOT_EVALUATED_NO_RELEASE_CONTRACT";

    /// <summary>Portable, raw-payload-free diagnostic summary (not the underlying evidence).</summary>
    public string ToManifestJson() => JsonSerializer.Serialize(new
    {
        schema = "AutoTest.RunArchiveIntegrity/1",
        runId = Run.ToString(),
        checkedUtc = CheckedUtc,
        integrity = Status.ToString(),
        mode = Mode?.ToString() ?? "Unknown",
        persistence = Persistence?.ToString() ?? "Unknown",
        verdict = Verdict ?? "Unknown",
        factCount = FactCount,
        stepFactCount = StepFactCount,
        verifiedArtifacts = VerifiedArtifacts,
        missingArtifacts = MissingArtifacts,
        corruptArtifacts = CorruptArtifacts,
        physicalRelease = PhysicalReleaseAssessment,
        issues = Issues.Select(i => new { code = i.Code, explanation = i.Explanation, blocking = i.Blocking })
    }, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// Verifies the same source of truth that the ordinary report uses:
/// the immutable Run header, hashed RunFacts/StepFacts, and artifact SHA-256.
/// This scanner deliberately does not create a second database or modify any verdict.
/// </summary>
public sealed class RunArchiveIntegrityInspector
{
    private readonly RunHistoryQuery _history;
    private readonly RunReportExporter _reports;

    public RunArchiveIntegrityInspector(RecordDatabase database, ArtifactRepository artifacts)
        : this(new RunHistoryQuery(database), new RunReportExporter(database, artifacts))
    {
    }

    public RunArchiveIntegrityInspector(RunHistoryQuery history, RunReportExporter reports)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    public async Task<RunArchiveIntegrityResult> InspectAsync(RunId run, CancellationToken ct = default)
    {
        if (run.Value == Guid.Empty) throw new ArgumentException("RunId is required.", nameof(run));
        ct.ThrowIfCancellationRequested();
        var checkedUtc = DateTimeOffset.UtcNow;
        try
        {
            // ExportSummaryAsync verifies the snapshot, all stored fact payload hashes,
            // fact counts, step fact counts and each linked artifact's contents.
            var report = await _reports.ExportSummaryAsync(run, ct).ConfigureAwait(false);
            var issues = new List<RunArchiveIntegrityIssue>();
            var missing = 0;
            var corrupt = 0;
            var verified = 0;
            foreach (var artifact in report.Artifacts)
            {
                switch (artifact.Integrity)
                {
                    case ArtifactIntegrity.Present:
                        verified++;
                        break;
                    case ArtifactIntegrity.Missing:
                        missing++;
                        issues.Add(new("EVIDENCE_MISSING",
                            $"Referenced artifact {artifact.ArtifactId:D} ({artifact.Kind}) is unavailable.", true));
                        break;
                    case ArtifactIntegrity.Corrupt:
                        corrupt++;
                        issues.Add(new("EVIDENCE_HASH_MISMATCH",
                            $"Referenced artifact {artifact.ArtifactId:D} ({artifact.Kind}) failed its hash/length check.", true));
                        break;
                    default:
                        throw new InvalidDataException("Unrecognized artifact integrity classification.");
                }
            }

            if (report.Header.Persistence != RecordPersistenceState.Committed)
                issues.Add(new("RUN_NOT_COMMITTED",
                    $"Run persistence is {report.Header.Persistence}; a partial or interrupted run is not archived as complete.", true));
            if (report.Artifacts.Count == 0)
                issues.Add(new("NO_LINKED_ARTIFACTS",
                    "No external evidence artifacts are linked. Database integrity alone does not prove hardware results.", false));
            if (report.StepFacts.Count == 0)
                issues.Add(new("NO_STEP_FACTS",
                    "No structured step Expected/Actual facts are stored for this run.", false));
            if (report.Header.Mode == RecordRunMode.Production)
                issues.Add(new("NOT_A_PHYSICAL_RELEASE",
                    "A production-mode PASS is historical test data, not a shipment or physical verification receipt.", false));

            var status = corrupt > 0 ? RunArchiveIntegrityStatus.EvidenceCorrupt :
                missing > 0 ? RunArchiveIntegrityStatus.EvidenceMissing :
                report.Header.Persistence != RecordPersistenceState.Committed ? RunArchiveIntegrityStatus.NotCommitted :
                report.Artifacts.Count == 0 ? RunArchiveIntegrityStatus.MetadataOnly :
                RunArchiveIntegrityStatus.EvidenceVerified;

            return new RunArchiveIntegrityResult(run, status, report.Header.Mode,
                report.Header.Persistence, report.Header.Verdict?.ToString(),
                report.Header.FactCount, report.Header.StepFactCount,
                verified, missing, corrupt, checkedUtc, issues);
        }
        catch (KeyNotFoundException)
        {
            return new RunArchiveIntegrityResult(run, RunArchiveIntegrityStatus.NotFound,
                null, null, null, 0, 0, 0, 0, 0, checkedUtc,
                new[] { new RunArchiveIntegrityIssue("RUN_NOT_FOUND", "No archived header exists for this RunId.", true) });
        }
        catch (InvalidDataException exception)
        {
            // Never turn a hash mismatch into an empty / successful query.
            // IO/SQLite availability exceptions are intentionally propagated to the caller.
            return new RunArchiveIntegrityResult(run, RunArchiveIntegrityStatus.RecordCorrupt,
                null, null, null, 0, 0, 0, 0, 0, checkedUtc,
                new[] { new RunArchiveIntegrityIssue("RECORD_DATA_INVALID", exception.Message, true) });
        }
        catch (JsonException exception)
        {
            return new RunArchiveIntegrityResult(run, RunArchiveIntegrityStatus.RecordCorrupt,
                null, null, null, 0, 0, 0, 0, 0, checkedUtc,
                new[] { new RunArchiveIntegrityIssue("RECORD_JSON_INVALID", exception.Message, true) });
        }
    }

    /// <summary>Bounded, sequential scan; no background operations or mutations.</summary>
    public async Task<IReadOnlyList<RunArchiveIntegrityResult>> InspectHistoryAsync(
        RunHistoryFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(filter), "Integrity scan accepts 1..500 records per call.");
        var history = await _history.FilterAsync(filter, ct).ConfigureAwait(false);
        var inspected = new List<RunArchiveIntegrityResult>(history.Length);
        foreach (var row in history)
        {
            ct.ThrowIfCancellationRequested();
            inspected.Add(await InspectAsync(row.Run, ct).ConfigureAwait(false));
        }
        return inspected;
    }
}
