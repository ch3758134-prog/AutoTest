using System.IO;
using System.Text.Json;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Project-local manual ECU CAN send-table draft. This is not a TEST plan,
/// production recipe, USB authorization or an automatic-transmit instruction.
/// </summary>
public static class ManualBenchCanDraftStore
{
    public sealed record DbcPin(Guid AssetId, int Revision, string Sha256, uint MessageId);
    public sealed record Row(string Name, string IdHex, string DataHex,
        bool Extended, bool IsPeriodic, string PeriodText, bool Included, DbcPin? Dbc);
    public sealed record Document(int Schema, List<Row> Rows);

    private const int MaximumBytes = 262144;
    public static string DocumentPath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot), ".autotest", "manual-can-rows.v1.json");

    public static async Task<Document?> LoadAsync(string projectRoot, CancellationToken ct = default)
    {
        var path = DocumentPath(projectRoot);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > MaximumBytes)
            throw new InvalidDataException("CAN workbench draft is too large.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("CAN workbench draft is too large.");
        var document = JsonSerializer.Deserialize<Document>(bytes) ??
            throw new InvalidDataException("CAN workbench draft is empty.");
        Validate(document);
        return document;
    }

    public static async Task SaveAsync(string projectRoot, Document document,
        CancellationToken ct = default)
    {
        Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document,
            new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("CAN workbench draft is too large.");
        var path = DocumentPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            // Preserve the preceding valid file for explicit recovery. Windows
            // File.Replace atomically swaps the two files on the same volume.
            // A corrupt current file is never silently accepted as new state.
            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Offer a separately verified last-known-good version when an
    /// interrupted edit damages the current JSON. Calling this does not
    /// overwrite or silently replace the current project draft.</summary>
    public static async Task<Document?> LoadBackupAsync(string projectRoot,
        CancellationToken ct = default)
    {
        var path = DocumentPath(projectRoot) + ".bak";
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumBytes)
            throw new InvalidDataException("CAN backup draft is too large.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("CAN backup draft is too large.");
        var document = JsonSerializer.Deserialize<Document>(bytes) ??
            throw new InvalidDataException("CAN backup draft is empty.");
        Validate(document);
        return document;
    }

    public static void Validate(Document document)
    {
        if (document.Schema != 1 || document.Rows is null ||
            document.Rows.Count > 64)
            throw new InvalidDataException("Unsupported CAN send table schema or row count.");
        foreach (var row in document.Rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Name) ||
                row.Name.Length > 120 || row.IdHex is null || row.IdHex.Length > 32 ||
                row.DataHex is null || row.DataHex.Length > 256 ||
                row.PeriodText is null || row.PeriodText.Length > 16 ||
                (row.Dbc is { } pin && (pin.AssetId == Guid.Empty ||
                 pin.Revision < 1 || pin.Sha256?.Length != 64)))
                throw new InvalidDataException("Invalid CAN send-table draft row.");
        }
    }
}
