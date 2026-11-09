using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Desktop.Tools.Production;

// Local production-tool UI evidence only. This is NOT the global OperationAudit,
// a physical USB/CAN trace, a TEST StepFact store or a factory release ledger.
public sealed record ProductionDiagnosticEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Layer,
    string Stage,
    string Severity,
    string Code,
    string Source,
    string Summary,
    string CatalogRevision,
    string RecipeRevision,
    Guid? RunId,
    string? RawBarcode,
    Guid? CaptureAttemptId);

public sealed class ProductionDiagnosticJournal
{
    private readonly object _gate = new();
    private readonly Queue<ProductionDiagnosticEntry> _events = new();
    private readonly TimeProvider _clock;
    private readonly int _capacity;
    private long _sequence;
    private long _dropped;

    public ProductionDiagnosticJournal(int capacity = 4000, TimeProvider? clock = null)
    {
        if (capacity is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _clock = clock ?? TimeProvider.System;
    }

    public long DroppedCount { get { lock (_gate) return _dropped; } }

    public ProductionDiagnosticEntry Append(string stage, string severity, string code,
        string summary, string source = "Mock", string catalogRevision = "",
        string recipeRevision = "", Guid? runId = null, string? rawBarcode = null,
        Guid? captureId = null)
    {
        // Never silently claim this UI journal represents physical transport activity.
        var entry = new ProductionDiagnosticEntry(0, _clock.GetUtcNow(),
            "ProductionUI", stage, severity, code, source, summary,
            catalogRevision, recipeRevision, runId, rawBarcode, captureId);
        lock (_gate)
        {
            entry = entry with { Sequence = ++_sequence };
            if (_events.Count == _capacity) { _events.Dequeue(); _dropped++; }
            _events.Enqueue(entry);
            return entry;
        }
    }

    public IReadOnlyList<ProductionDiagnosticEntry> Snapshot(bool errorsOnly = false, string? stage = null)
    {
        lock (_gate)
        {
            return _events.Where(x => (!errorsOnly || x.Severity is "Error" or "Warning") &&
                (stage is null || string.Equals(x.Stage, stage, StringComparison.Ordinal))).ToArray();
        }
    }

    // A copyable, redacted view of the same bounded events. No raw barcode by default.
    public string CopyText(bool errorsOnly = false, string? stage = null)
    {
        var lines = Snapshot(errorsOnly, stage).Select(entry =>
        {
            var x = Redact(entry);
            return $"{x.TimestampUtc:O} | {x.Sequence} | {x.Stage} | {x.Severity} | " +
                   $"{x.Code} | {x.Source} | {x.RunId?.ToString("N") ?? "-"} | " +
                   $"{x.CaptureAttemptId?.ToString("N") ?? "-"} | {x.Summary}";
        });
        return string.Join(Environment.NewLine, lines);
    }

    // Redact the raw value even when it appears in a free-text summary.
    private static ProductionDiagnosticEntry Redact(ProductionDiagnosticEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RawBarcode)) return entry with { RawBarcode = null };
        return entry with
        {
            RawBarcode = "[REDACTED]",
            Summary = entry.Summary.Replace(entry.RawBarcode, "[REDACTED]", StringComparison.Ordinal),
            Code = entry.Code.Replace(entry.RawBarcode, "[REDACTED]", StringComparison.Ordinal)
        };
    }

    public async Task ExportJsonLinesAsync(string filePath, bool includeRawBarcode = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Export path required", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        var folder = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(folder);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        ProductionDiagnosticEntry[] captured;
        long dropped;
        lock (_gate) { captured = _events.ToArray(); dropped = _dropped; }
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    Type = "AutoTest.R0.ProductionUiDiagnostic",
                    SchemaVersion = 1,
                    ExportedAtUtc = _clock.GetUtcNow(),
                    EvidenceAuthority = "UI_ONLY_NOT_PHYSICAL_NOT_FACTORY_RELEASE",
                    SoftwareGitSha = "UNKNOWN_RUNTIME_REVISION_NOT_INJECTED",
                    ProjectRevision = "UNCONNECTED_TO_FORMAL_PROJECT_CONTEXT",
                    SessionGeneration = (long?)null,
                    StreamCompleteness = dropped > 0 ? "IN_MEMORY_OVERFLOW" : "IN_MEMORY_UI_EVENTS_ONLY",
                    EventCount = captured.Length,
                    DroppedEvents = dropped,
                    RawBarcodeIncluded = includeRawBarcode
                }));
                foreach (var entry in captured)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var safeEntry = includeRawBarcode ? entry : Redact(entry);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(safeEntry));
                }
                await writer.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
