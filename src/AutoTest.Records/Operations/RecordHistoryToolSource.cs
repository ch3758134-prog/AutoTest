using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;

namespace AutoTest.Records.Operations;

public sealed record RecordHistoryToolRow(string RunId, string DutId, string Mode,
    DateTimeOffset StartedUtc, string PersistenceState, string Verdict,
    long FactCount, long StepFactCount, string? FailureReason, string SnapshotJson);
public sealed record RecordToolSummary(long RunCount, long DistinctDutCount, long CommittedRuns,
    long PassedRuns, long FailedRuns, long InterruptedRuns, long WritingRuns);
public interface IRecordHistoryToolSource
{
    Task<IReadOnlyList<RecordHistoryToolRow>> QueryAsync(string? dutId, string? mode, int limit,
        CancellationToken ct = default);
    Task<RecordToolSummary> SummaryAsync(string mode, CancellationToken ct = default);
    Task<IReadOnlyList<RecordHistoryToolRow>> QueryPeriodAsync(string? dutId, string? mode,
        int limit, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default);
    Task<ProductionPeriodSummary> ProductionPeriodSummaryAsync(DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc, CancellationToken ct = default);
    Task<RunArchiveIntegrityResult> InspectArchiveAsync(string runId, CancellationToken ct = default);
    Task<string> ExportFullReportJsonAsync(string runId, CancellationToken ct = default);
    Task<string> ExportFullReportHtmlAsync(string runId, CancellationToken ct = default);
    Task ExportReportAsync(string runId, string destination, bool html, CancellationToken ct = default);
}

/// <summary>Bridges the existing production history read model; no second RunRecord database.</summary>
public sealed class RecordHistoryToolSource : IRecordHistoryToolSource
{
    private readonly RunHistoryQuery _history;
    private readonly ProductionStatistics _statistics;
    private readonly RunReportExporter _reports;
    public RecordHistoryToolSource(RunHistoryQuery history, ProductionStatistics statistics, RunReportExporter reports)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    public Task<IReadOnlyList<RecordHistoryToolRow>> QueryAsync(string? dutId, string? mode, int limit,
        CancellationToken ct = default) =>
        QueryPeriodAsync(dutId, mode, limit, null, null, ct);

    public Task<ProductionPeriodSummary> ProductionPeriodSummaryAsync(DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc, CancellationToken ct = default) =>
        _statistics.GetPeriodSummaryAsync(RecordRunMode.Production, fromUtc, toUtc, ct);

    public async Task<IReadOnlyList<RecordHistoryToolRow>> QueryPeriodAsync(string? dutId, string? mode,
        int limit, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        RecordRunMode? runMode = string.IsNullOrWhiteSpace(mode) || mode == "All" ? null : Enum.Parse<RecordRunMode>(mode, true);
        var filtered = await _history.FilterAsync(new RunHistoryFilter(
            DutId: string.IsNullOrWhiteSpace(dutId) ? null : dutId.Trim(), Mode: runMode,
            FromUtc: fromUtc, ToUtc: toUtc,
            Limit: Math.Clamp(limit, 1, 500)), ct).ConfigureAwait(false);
        return filtered.Select(x => new RecordHistoryToolRow(x.Run.ToString(), x.DutId, x.Mode.ToString(),
            x.StartedUtc, x.Persistence.ToString(), x.Verdict?.ToString() ?? "Unknown", x.FactCount,
            x.StepFactCount, x.FailureReason, x.SnapshotJson)).ToArray();
    }

    public Task<RunArchiveIntegrityResult> InspectArchiveAsync(string runId, CancellationToken ct = default) =>
        new RunArchiveIntegrityInspector(_history, _reports).InspectAsync(ParseRun(runId), ct);

    public Task<string> ExportFullReportJsonAsync(string runId, CancellationToken ct = default) =>
        _reports.ExportJsonAsync(ParseRun(runId), ct);

    public async Task<string> ExportFullReportHtmlAsync(string runId, CancellationToken ct = default) =>
        (await _reports.ExportSummaryAsync(ParseRun(runId), ct).ConfigureAwait(false)).Html;

    private static RunId ParseRun(string runId) => RunId.TryParse(runId, out var parsed) ? parsed :
        throw new ArgumentException("Invalid RunId", nameof(runId));

    public async Task ExportReportAsync(string runId, string destination, bool html, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var content = html ? await ExportFullReportHtmlAsync(runId, ct).ConfigureAwait(false) :
            await ExportFullReportJsonAsync(runId, ct).ConfigureAwait(false);
        destination = Path.GetFullPath(destination);
        var dir = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, destination, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<RecordToolSummary> SummaryAsync(string mode, CancellationToken ct = default)
    {
        // Never sum Preview/Development/Diagnostic into a production counter.
        var parsed = Enum.Parse<RecordRunMode>(mode, true);
        var summary = await _statistics.GetSummaryAsync(parsed, ct: ct).ConfigureAwait(false);
        return new RecordToolSummary(summary.RunCount, summary.DistinctDutCount,
            summary.CommittedRuns, summary.PassedRuns, summary.FailedRuns,
            summary.InterruptedRuns, summary.WritingRuns);
    }
}

