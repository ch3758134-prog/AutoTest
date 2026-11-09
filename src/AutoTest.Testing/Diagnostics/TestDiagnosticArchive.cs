using System.Text;
using System.Text.Json;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Testing.Diagnostics;

/// <summary>
/// TEST-owned diagnostic projection. Not a substitute for the shared Records/OperationAudit model.
/// Every raw payload here is a Mock engine fact, never a physical CAN/USB frame.
/// </summary>
public sealed record TestDiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string EventKind,
    string Source,
    string RunId,
    string StepId,
    string OperationId,
    string Phase,
    string Type,
    string Expected,
    string? Actual,
    string Quality,
    string Verdict,
    string Diagnostic,
    string RawMockFactJson)
{
    public long? MonotonicOffsetMs { get; init; }
    public string Layer { get; init; } = "TEST.RUNTIME";
    public string ErrorCode { get; init; } = "";
}

public sealed record TestDiagnosticBundle(
    string SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string SourceMode,
    string PhysicalPermission,
    string DocumentId,
    string DocumentName,
    int FormatVersion,
    string PlanFingerprint,
    string RunId,
    string RunVerdict,
    bool Stopped,
    IReadOnlyList<TestDiagnosticEvent> Timeline,
    string Limitations)
{
    public string HostBuildGitSha { get; init; } = "NOT_SUPPLIED";
    public string ProjectRevision { get; init; } = "UNBOUND";
    public string ProjectId { get; init; } = "UNBOUND";
    public string DutProfile { get; init; } = "UNBOUND";
    public string StationId { get; init; } = "NOT_CONNECTED";
    public string SessionGeneration { get; init; } = "NOT_CONNECTED";
    public string ResourceRevision { get; init; } = "UNBOUND";
    public string EvidenceCompleteness { get; init; } = "IN_MEMORY_ONLY_NOT_DURABLE";
    public bool ValuesRedacted { get; init; } = true;
    public string AttemptStatus { get; init; } = "NOT_RUN";
    public IReadOnlyList<string> PreflightDiagnostics { get; init; } = Array.Empty<string>();
}

public static class TestDiagnosticArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string ScrubContext(string value) => value is "UNBOUND" or "NOT_CONNECTED" or "NOT_SUPPLIED"
        ? value : "[REDACTED]";

    // ErrorCode survives redacted exports, while the original input remains private.
    // Only allow known TEST-owned diagnostic prefixes; never copy arbitrary log text
    // into ErrorCode (which is retained when values are scrubbed).
    private static string? ConditionErrorCode(string? detail)
    {
        if (detail is null) return null;
        foreach (var code in new[]
        {
            "MOCK_CONDITION_INVALID_NUMBER", "MOCK_CONDITION_INVALID_BOOLEAN",
            "MOCK_CONDITION_NO_VALUE", "MOCK_CONDITION_EXPECTED_MISSING",
            "MOCK_ASSERT_NO_VALID_OBSERVATION"
        })
            if (detail.Equals(code, StringComparison.Ordinal) ||
                detail.StartsWith(code + ":", StringComparison.Ordinal)) return code;
        return null;
    }

    public static TestDiagnosticEvent FromFact(string runId, string phase, StepFact fact,
        DateTimeOffset? timestamp = null, long? monotonicOffsetMs = null) => new(
            timestamp ?? DateTimeOffset.UtcNow,
            "STEP_FACT", "Mock", runId, fact.StepId.ToString(), fact.OperationId.ToString("D"),
            phase, fact.Type, fact.Expected, fact.Actual, fact.Quality.ToString(),
            fact.Verdict.ToString(), fact.Detail ?? "", JsonSerializer.Serialize(fact))
            {
                MonotonicOffsetMs = monotonicOffsetMs,
                Layer = fact.Type is "RuntimeError" or "CleanupError" ? "TEST.EXECUTION" : "TEST.JUDGEMENT",
                ErrorCode = ConditionErrorCode(fact.Detail) ?? (fact.Verdict switch
                {
                    MockStepVerdict.Fail when fact.Type == "Assert" => "ASSERT_MISMATCH",
                    MockStepVerdict.Fail when fact.Type == "When" => "MOCK_WHEN_TIMEOUT",
                    MockStepVerdict.Fail when fact.Type == "Fail" => "MOCK_EXPLICIT_FAILURE",
                    MockStepVerdict.Fail => "MOCK_STEP_FAILED",
                    MockStepVerdict.Inconclusive when fact.Type == "When" => "MOCK_WHEN_NO_OBSERVATION",
                    MockStepVerdict.Inconclusive when fact.Type == "Switch" => "MOCK_SWITCH_UNRESOLVED",
                    MockStepVerdict.Inconclusive => "INSUFFICIENT_MOCK_EVIDENCE",
                    MockStepVerdict.Skipped => "STEP_SKIPPED",
                    MockStepVerdict.Error => "MOCK_RUNTIME_ERROR",
                    _ => ""
                })
            };

    public static TestDiagnosticEvent Operation(string runId, string eventKind,
        string diagnostic, DateTimeOffset? timestamp = null, long? monotonicOffsetMs = null) => new(
            timestamp ?? DateTimeOffset.UtcNow, eventKind, "Mock", runId, "", "", "", "", "", null,
            "Unknown", "NotEvaluated", diagnostic, "")
            {
                MonotonicOffsetMs = monotonicOffsetMs,
                Layer = eventKind.StartsWith("PREFLIGHT", StringComparison.Ordinal) ? "TEST.PREPARATION" : "TEST.CONTROL",
                ErrorCode = eventKind.EndsWith("REJECTED", StringComparison.Ordinal) ||
                    eventKind.EndsWith("BLOCKED", StringComparison.Ordinal) ||
                    eventKind.EndsWith("ERROR", StringComparison.Ordinal) ? eventKind : ""
            };

    public static TestDiagnosticBundle Build(TestDocument document,
        string planFingerprint, MockRunReport? report, IEnumerable<TestDiagnosticEvent> events,
        string hostBuildGitSha = "NOT_SUPPLIED", string projectRevision = "UNBOUND",
        bool redactValues = true, string projectId = "UNBOUND", string dutProfile = "UNBOUND",
        string stationId = "NOT_CONNECTED", string sessionGeneration = "NOT_CONNECTED",
        string resourceRevision = "UNBOUND", IEnumerable<string>? preflightDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(events);
        var timeline = events.ToArray();
        if (redactValues)
            timeline = timeline.Select(e => e with
            {
                Expected = string.IsNullOrEmpty(e.Expected) ? "" : "[REDACTED]",
                Actual = e.Actual is null ? null : "[REDACTED]",
                Diagnostic = string.IsNullOrEmpty(e.Diagnostic) ? "" : "[REDACTED]",
                RawMockFactJson = string.IsNullOrEmpty(e.RawMockFactJson) ? "" : "[REDACTED]"
            }).ToArray();
        return new TestDiagnosticBundle("AutoTest.TEST.R0.DIAG/1", DateTimeOffset.UtcNow,
            "Mock", "DISABLED", document.Id.ToString(), redactValues ? "[REDACTED]" : document.Name,
            document.FormatVersion, planFingerprint, report?.Run.ToString() ?? "",
            report?.Verdict.ToString() ?? "NOT_RUN", report?.Stopped ?? false, timeline,
            "Software-only TEST diagnostics; no physical ECU/CAN/USB observation or production-release evidence. " +
            "RawMockFactJson is serialized Mock engine evidence, not a physical frame. " +
            (redactValues ? "Payloads and document name were redacted for sharing." :
                "Contains unredacted values: retain locally and inspect before sharing."))
        {
            HostBuildGitSha = hostBuildGitSha,
            ProjectRevision = projectRevision,
            ProjectId = redactValues ? ScrubContext(projectId) : projectId,
            DutProfile = redactValues ? ScrubContext(dutProfile) : dutProfile,
            StationId = redactValues ? ScrubContext(stationId) : stationId,
            SessionGeneration = redactValues ? ScrubContext(sessionGeneration) : sessionGeneration,
            ResourceRevision = resourceRevision,
            ValuesRedacted = redactValues,
            AttemptStatus = report is not null ? (report.Stopped ? "STOPPED" : "COMPLETED") :
                timeline.Any(x => x.EventKind == "PREFLIGHT_BLOCKED") ? "PREFLIGHT_BLOCKED" :
                timeline.Any(x => x.EventKind is "PREFLIGHT_ERROR" or "RUN_ERROR") ? "ERROR" :
                timeline.Any(x => x.EventKind == "PREFLIGHT_READY") ? "PREFLIGHT_READY" : "NOT_RUN",
            PreflightDiagnostics = redactValues
                ? (preflightDiagnostics ?? []).Select(_ => "[REDACTED]").ToArray()
                : (preflightDiagnostics ?? []).ToArray()
        };
    }

    public static string ToJson(TestDiagnosticBundle bundle) =>
        JsonSerializer.Serialize(bundle, JsonOptions);

    public static string ToCsv(TestDiagnosticBundle bundle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,MonotonicOffsetMs,EventKind,Source,RunId,StepId,OperationId,Layer,ErrorCode,Phase,Type,Expected,Actual,Quality,Verdict,Diagnostic,RawMockFactJson");
        foreach (var e in bundle.Timeline)
        {
            var values = new[] { e.TimestampUtc.ToString("O"), e.MonotonicOffsetMs?.ToString() ?? "", e.EventKind, e.Source, e.RunId,
                e.StepId, e.OperationId, e.Layer, e.ErrorCode, e.Phase, e.Type, e.Expected, e.Actual ?? "",
                e.Quality, e.Verdict, e.Diagnostic, e.RawMockFactJson };
            sb.AppendLine(string.Join(",", values.Select(CsvCell)));
        }
        return sb.ToString();
    }
    private static string CsvCell(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
