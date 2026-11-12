using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.ControllerConfig;

public sealed record ControllerFileReceipt(string Path, string Sha256, int Revision);

public static class ControllerRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false
    };
    private sealed record FileEnvelope(int SchemaVersion, EcuControlProfile Profile);
    private const int Schema = 1;
    private const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Canonical schema envelope for embedding a controller profile in an AutoTest project asset.</summary>
    public static byte[] Encode(EcuControlProfile profile)
    {
        profile.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new FileEnvelope(Schema, profile), Json);
        if (bytes.Length > MaxBytes) throw new ProjectDataException("CTRL_FILE_SIZE", "Configuration exceeds the limit.");
        return bytes;
    }

    public static EcuControlProfile Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > MaxBytes)
            throw new ProjectDataException("CTRL_FILE_SIZE", "Controller configuration size invalid.");
        try
        {
            var data = JsonSerializer.Deserialize<FileEnvelope>(bytes, Json);
            if (data is null || data.SchemaVersion != Schema || data.Profile is null)
                throw new ProjectDataException("CTRL_SCHEMA", "Unsupported or malformed configuration format.");
            return data.Profile.Validate();
        }
        catch (JsonException e) { throw new ProjectDataException("CTRL_JSON", e.Message); }
    }

    public static async Task<(EcuControlProfile Profile, ControllerFileReceipt Receipt)> OpenAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists || info.Length > MaxBytes)
            throw new ProjectDataException("CTRL_FILE_SIZE", "Configuration does not exist or exceeds the limit.");
        var bytes = await File.ReadAllBytesAsync(full, cancellationToken);
        try
        {
            var data = JsonSerializer.Deserialize<FileEnvelope>(bytes, Json);
            if (data is null || data.SchemaVersion != Schema || data.Profile is null)
                throw new ProjectDataException("CTRL_SCHEMA", "Unsupported or malformed configuration format.");
            data.Profile.Validate();
            return (data.Profile, new ControllerFileReceipt(full, Hash(bytes), data.Profile.Revision));
        }
        catch (JsonException e) { throw new ProjectDataException("CTRL_JSON", e.Message); }
    }

    public static async Task<ControllerFileReceipt> SaveAsync(EcuControlProfile profile, string path,
        string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ProjectDataException("CTRL_PATH", "Missing directory.");
        Directory.CreateDirectory(dir);
        // Explicit optimistic-concurrency policy: caller must acknowledge the prior file hash.
        if (File.Exists(full))
        {
            if (string.IsNullOrWhiteSpace(expectedSha256) ||
                !string.Equals(Hash(await File.ReadAllBytesAsync(full, cancellationToken)), expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("CTRL_CONFLICT", "File changed or was not opened for editing.");
        }
        else if (!string.IsNullOrEmpty(expectedSha256))
            throw new ProjectDataException("CTRL_CONFLICT", "Expected file disappeared.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new FileEnvelope(Schema, profile), Json);
        if (bytes.Length > MaxBytes) throw new ProjectDataException("CTRL_FILE_SIZE", "Configuration exceeds the limit.");
        var temporary = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(bytes, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Leave a recoverable copy when overwriting an existing file.
            if (File.Exists(full))
            {
                File.Replace(temporary, full, full + ".bak");
            }
            else File.Move(temporary, full);
            return new ControllerFileReceipt(full, Hash(bytes), profile.Revision);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<ControllerFileReceipt> ExportAsync(EcuControlProfile profile, string path,
        CancellationToken ct = default) => SaveAsync(profile, path, null, ct);

    public static async Task<(EcuControlProfile Profile, ControllerFileReceipt Receipt)> RollbackAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var file = Path.GetFullPath(path);
        var backup = file + ".bak";
        if (!File.Exists(backup)) throw new ProjectDataException("CTRL_BACKUP", "No rollback revision available.");
        // Require a valid backup before replacing the current file.
        var loaded = await OpenAsync(backup, cancellationToken);
        var opened = await OpenAsync(file, cancellationToken);
        await SaveAsync(loaded.Profile, file, opened.Receipt.Sha256, cancellationToken);
        return await OpenAsync(file, cancellationToken);
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
