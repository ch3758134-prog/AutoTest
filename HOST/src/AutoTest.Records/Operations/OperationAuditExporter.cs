using System.Text;
using System.Text.Json;

namespace AutoTest.Records.Operations;

public interface IOperationAuditExporter
{
    Task<int> ExportCsvAsync(OperationAuditQuery query, string destination, CancellationToken ct = default);
    Task<int> ExportJsonlAsync(OperationAuditQuery query, string destination, CancellationToken ct = default);
}

/// <summary>Cursor streaming export. A failed export leaves no completed output file.</summary>
public sealed class OperationAuditExporter : IOperationAuditExporter
{
    private readonly IOperationAuditStore _store;
    public OperationAuditExporter(IOperationAuditStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<int> ExportCsvAsync(OperationAuditQuery query, string destination, CancellationToken ct = default) =>
        ExportAsync(query, destination, isCsv: true, ct);
    public Task<int> ExportJsonlAsync(OperationAuditQuery query, string destination, CancellationToken ct = default) =>
        ExportAsync(query, destination, isCsv: false, ct);

    private async Task<int> ExportAsync(OperationAuditQuery query, string destination, bool isCsv, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        query.Validate();
        destination = Path.GetFullPath(destination);
        var dir = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        int count = 0;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 32768, options: FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                if (isCsv) await writer.WriteLineAsync("EventId,OccurredUtc,OperationId,CorrelationId,Kind,Outcome,Origin,Level,DeviceId,SessionGeneration,RunId,NativeCode,FailureStage,Summary,TraceFile,TraceOffset,TraceSha256").ConfigureAwait(false);
                AuditCursor? cursor = null;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    var page = await _store.QueryAsync(query with { PageSize = 500, Before = cursor }, ct).ConfigureAwait(false);
                    foreach (var evt in page.Events)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (isCsv)
                        {
                            var cells = new object?[] { evt.EventId,evt.OccurredUtc.ToString("O"),evt.OperationId,evt.CorrelationId,
                                evt.Kind,evt.Outcome,evt.Origin,evt.Level,evt.DeviceId,evt.SessionGeneration,evt.RunId,
                                evt.NativeErrorCode,evt.FailureStage,evt.Summary,evt.Trace?.RelativeFile,
                                evt.Trace?.Offset,evt.Trace?.Sha256 };
                            await writer.WriteLineAsync(string.Join(",", cells.Select(Csv))).ConfigureAwait(false);
                        }
                        else await writer.WriteLineAsync(JsonSerializer.Serialize(evt)).ConfigureAwait(false);
                        count++;
                    }
                    cursor = page.NextCursor;
                } while (cursor != null);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            // Refuse to overwrite a historical export; destination is claimed only when fully written.
            File.Move(temp, destination, overwrite: false);
            return count;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Csv(object? value)
    {
        var s = value?.ToString() ?? "";
        // Excel/LibreOffice CSV formula injection: make untrusted text explicitly literal.
        if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
