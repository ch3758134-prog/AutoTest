using System.Text;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Resources;

/// <summary>Local PROJECT tool evidence only; not a replacement for shared operation/trace contracts.</summary>
public sealed record R0ProjectDiagnosticEvent(DateTimeOffset TimestampUtc, Guid OperationId,
    string Tool, string Origin, string Stage, string Outcome, string ErrorCode,
    string Project, string AssetId, int Revision, string Sha256,
    string RawHex, string Expected, string Actual, string Detail);

public sealed class R0ProjectDiagnosticJournal(int limit = 4096)
{
    private readonly List<R0ProjectDiagnosticEvent> _events = [];
    private long _dropped;
    public IReadOnlyList<R0ProjectDiagnosticEvent> Events => _events;
    public long DroppedCount => _dropped;
    public R0ProjectDiagnosticEvent Record(string tool, string stage, string outcome, string code = "",
        string project = "", string assetId = "", int revision = 0, string sha256 = "",
        string rawHex = "", string expected = "", string actual = "", string detail = "",
        string origin = "SOFTWARE_ONLY", Guid? operationId = null)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        var item = new R0ProjectDiagnosticEvent(DateTimeOffset.UtcNow, operationId ?? Guid.NewGuid(), tool,
            origin, stage, outcome, code, project, assetId, revision, sha256,
            rawHex, expected, actual, detail);
        if (_events.Count == limit) { _events.RemoveAt(0); _dropped++; }
        _events.Add(item);
        return item;
    }
    public async Task ExportJsonLinesAsync(string target, bool redactLocalPaths = true,
        bool includeRawHex = true, CancellationToken ct = default, string softwareGitSha = "UNBOUND")
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ProjectDataException("TRACE_PATH", "Choose a trace file path before export.");
        var full = Path.GetFullPath(target);
        if (!string.Equals(Path.GetExtension(full), ".jsonl", StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("TRACE_EXTENSION", "Diagnostic export requires a .jsonl file path.");
        var directory = Path.GetDirectoryName(full) ?? throw new ProjectDataException("TRACE_PATH", "Invalid trace directory.");
        Directory.CreateDirectory(directory);
        var tmp = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    Type = "R0_PROJECT_TRACE_HEADER", SchemaVersion = 1, ExportedUtc = DateTimeOffset.UtcNow,
                    LostEvents = _dropped, Origin = "SOFTWARE_ONLY", SoftwareGitSha = softwareGitSha,
                    RedactedLocalPaths = redactLocalPaths,
                    RawHexIncluded = includeRawHex, Note = "Local PROJECT evidence, not proof of physical observation. Review payloads before sharing."
                }));
                foreach (var entry in _events)
                {
                    ct.ThrowIfCancellationRequested();
                    var output = entry with
                    {
                        Project = redactLocalPaths ? Path.GetFileName(entry.Project) : entry.Project,
                        Detail = redactLocalPaths ? RedactPathDetails(entry.Detail) : entry.Detail,
                        Expected = redactLocalPaths ? RedactPathDetails(entry.Expected) : entry.Expected,
                        Actual = redactLocalPaths ? RedactPathDetails(entry.Actual) : entry.Actual,
                        RawHex = includeRawHex ? entry.RawHex : "[OMITTED]"
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(output));
                }
                await writer.FlushAsync(ct);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(tmp, full, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    private static string RedactPathDetails(string details) => System.Text.RegularExpressions.Regex.Replace(
        details, @"(?i)(?:[a-z]:[\\/]|\\\\)[^\s,;]+", "[LOCAL_PATH]");
}
