using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTest.Records.Operations;

public interface IOperationTraceWriter
{
    Task<AuditTraceReference> AppendAsync(OperationRawTrace entry, CancellationToken ct = default);
}

public interface IOperationTraceReader
{
    Task<OperationRawTrace> ReadAsync(AuditTraceReference reference, CancellationToken ct = default);
}

/// <summary>A separate append-only JSONL artifact archive. Only flushed bytes can be referenced by SQLite.</summary>
public sealed class OperationTraceArchive : IOperationTraceReader, IOperationTraceWriter
{
    private readonly string _root;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public OperationTraceArchive(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public async Task<AuditTraceReference> AppendAsync(OperationRawTrace entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();
        var name = "trace-" + entry.OccurredUtc.ToString("yyyyMMdd") + ".jsonl";
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            // Cross-process writer lock; readers may still inspect fully flushed lines.
            using var writerLock = new FileStream(Path.Combine(_root, ".trace-writer.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var path = Path.Combine(_root, name);
            await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.ReadWrite, bufferSize: 4096, options: FileOptions.Asynchronous);
            stream.Seek(0, SeekOrigin.End);
            var offset = stream.Position;
            await stream.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            return new AuditTraceReference(name, offset, payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<OperationRawTrace> ReadAsync(AuditTraceReference reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        var path = Path.Combine(_root, reference.RelativeFile);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize: 4096, options: FileOptions.Asynchronous);
        if (stream.Length - reference.Offset < reference.ByteLength)
            throw new InvalidDataException("Referenced trace line is incomplete or missing.");
        stream.Seek(reference.Offset, SeekOrigin.Begin);
        var bytes = new byte[reference.ByteLength];
        await stream.ReadExactlyAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        if (!string.Equals(reference.Sha256,
            Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace SHA-256 mismatch. Do not display as verified evidence.");
        if (bytes[^1] != (byte)'\n') throw new InvalidDataException("Incomplete JSONL trace line.");
        var trace = JsonSerializer.Deserialize<OperationRawTrace>(bytes)
            ?? throw new InvalidDataException("Invalid trace JSONL payload.");
        trace.Validate();
        return trace;
    }
}
