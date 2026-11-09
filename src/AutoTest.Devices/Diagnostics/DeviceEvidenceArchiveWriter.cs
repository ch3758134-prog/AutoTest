using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTest.Devices.Diagnostics;

/// <summary>R0 local-only export of the original DEVICE JSONL evidence. This is never a physical PASS.</summary>
public sealed record DeviceEvidenceContext(
    string GitBaseSha,
    string? DeviceInstanceId,
    string? DriverService,
    long SessionGeneration,
    long TransportDropped,
    long CanIngressDropped,
    long CanMalformed,
    long HubDropped,
    long UiHistoryTrimmed,
    string? TransportError,
    string? OperationLogError,
    bool IsDisconnected);

public sealed record DeviceEvidenceFile(string Path, long Bytes, string Sha256);
public sealed record DeviceEvidenceArchiveResult(string ArchivePath, string Sha256,
    IReadOnlyList<DeviceEvidenceFile> Files);

/// <summary>
/// Archives the transport and operation logs without reserializing them, preserving every raw byte.
/// The caller must disconnect USB first. Does not scan, send USB/CAN, or control any station output.
/// </summary>
public static class DeviceEvidenceArchiveWriter
{
    public static async Task<DeviceEvidenceArchiveResult> ExportAsync(
        string transportJsonl, string operationJsonl, string destinationZip,
        DeviceEvidenceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsDisconnected)
            throw new InvalidOperationException("Disconnect the physical DEVICE session before exporting evidence.");
        if (string.IsNullOrWhiteSpace(transportJsonl) || string.IsNullOrWhiteSpace(operationJsonl) ||
            string.IsNullOrWhiteSpace(destinationZip))
            throw new ArgumentException("Both JSONL source files and destination ZIP are required.");

        string transport = Path.GetFullPath(transportJsonl);
        string operations = Path.GetFullPath(operationJsonl);
        string destination = Path.GetFullPath(destinationZip);
        if (string.Equals(transport, operations, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination, transport, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination, operations, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Transport, operations, and export destinations must be distinct files.");
        if (!File.Exists(transport) || !File.Exists(operations))
            throw new FileNotFoundException("DEVICE raw trace or operation audit is missing. Nothing was exported.");
        if (File.Exists(destination))
            throw new IOException("Export archive already exists. Refusing to overwrite evidence.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = Path.Combine(Path.GetDirectoryName(destination)!, ".device-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        var files = new List<DeviceEvidenceFile>(2);
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    files.Add(await AddSourceAsync(zip, transport, "raw/transport.jsonl", cancellationToken)
                        .ConfigureAwait(false));
                    files.Add(await AddSourceAsync(zip, operations, "raw/operations.jsonl", cancellationToken)
                        .ConfigureAwait(false));

                    bool rawLoss = context.TransportDropped > 0 || context.CanIngressDropped > 0 ||
                                   context.HubDropped > 0 || context.TransportError is not null ||
                                   context.OperationLogError is not null;
                    var manifest = new
                    {
                        Schema = "AutoTest.DeviceEvidence/1",
                        CandidateId = "R0-DEVICE-OBS-01",
                        ExportedUtc = DateTimeOffset.UtcNow,
                        context.GitBaseSha,
                        ProjectProfileRevision = "NOT_BOUND_STANDALONE_DEVICE_DIAGNOSTICS",
                        Source = "PHYSICAL_READONLY_OBSERVATION_UNVERIFIED",
                        PhysicalTestPassed = false,
                        TimestampQuality = "HostArrivalOnly; no controller CAN hardware timestamps",
                        BusMapping = "CAN0=KeypadCan; CAN1=DutCan",
                        context.DeviceInstanceId,
                        context.DriverService,
                        context.SessionGeneration,
                        context.TransportDropped,
                        context.CanIngressDropped,
                        context.CanMalformed,
                        context.HubDropped,
                        context.UiHistoryTrimmed,
                        context.TransportError,
                        context.OperationLogError,
                        EvidenceCompleteness = rawLoss ? "DEGRADED_OR_UNKNOWN" : "NO_REPORTED_LOSS_NOT_PHYSICALLY_VERIFIED",
                        OperationScope = "Application-lifetime audit; may include multiple connections",
                        TransportScope = "Last selected connection trace",
                        Confidentiality = "LOCAL_ONLY: raw CAN/USB may contain sensitive ECU data. Review before sharing.",
                        Note = "File hashes cover bytes in ZIP, not the physical truth of board signals.",
                        Files = files
                    };
                    var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await JsonSerializer.SerializeAsync(entryStream, manifest,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            // No replaced/partial files at the declared destination.
            File.Move(partial, destination);
            await using var verify = new FileStream(destination, FileMode.Open, FileAccess.Read,
                FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            string sha = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)
                .ConfigureAwait(false));
            return new DeviceEvidenceArchiveResult(destination, sha, files);
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    private static async Task<DeviceEvidenceFile> AddSourceAsync(ZipArchive archive, string source,
        string archiveName, CancellationToken cancellationToken)
    {
        // Capture exact byte count and detect changes during a supposed disconnected snapshot.
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
        long initialLength = input.Length;
        if (initialLength == 0) throw new IOException($"Empty evidence file: {Path.GetFileName(source)}");
        var entry = archive.CreateEntry(archiveName, CompressionLevel.Optimal);
        await using var write = entry.Open();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long remaining = initialLength;
        while (remaining > 0)
        {
            int take = (int)Math.Min(buffer.Length, remaining);
            int read = await input.ReadAsync(buffer.AsMemory(0, take), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0) throw new EndOfStreamException("Evidence changed while exporting.");
            await write.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            digest.AppendData(buffer, 0, read);
            remaining -= read;
        }
        if (input.Length != initialLength)
            throw new IOException("Evidence source grew during export. Disconnect and retry.");
        return new DeviceEvidenceFile(archiveName, initialLength, Convert.ToHexString(digest.GetHashAndReset()));
    }
}

/// <summary>Single semantics for live WPF list filtering; never filters raw disk evidence.</summary>
public static class DeviceEvidenceTextFilter
{
    public static bool Matches(string? source, string? details, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = query.Trim();
        return (source?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (details?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
