using System.Security.Cryptography;
using System.Text;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

public sealed record DbcFileReceipt(string Path, string Sha256, int Revision);
public static class DbcDocumentStore
{
    public static async Task<(DbcWorkbenchDocument Document, DbcFileReceipt Receipt)> OpenAsync(
        string file, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(file);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 4 * 1024 * 1024)
            throw new ProjectDataException("DBC_FILE", "File is missing or exceeds limit.");
        var data = await File.ReadAllBytesAsync(path, ct);
        var source = DbcSource.Load(data); // fail closed for non-UTF8 sources
        var document = DbcWorkbenchDocument.Import(Path.GetFileNameWithoutExtension(path), source.Text);
        return (document, new DbcFileReceipt(path, source.Sha256, document.Model.Revision));
    }
    public static async Task<DbcFileReceipt> SaveAsync(DbcWorkbenchDocument document, string file,
        string? expectedFileHash, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(file);
        var parent = Path.GetDirectoryName(path) ?? throw new ProjectDataException("DBC_FILE", "Invalid DBC output path.");
        Directory.CreateDirectory(parent);
        if (File.Exists(path))
        {
            if (string.IsNullOrWhiteSpace(expectedFileHash) ||
                !string.Equals(Hash(await File.ReadAllBytesAsync(path, ct)), expectedFileHash, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("DBC_FILE_CONFLICT", "DBC changed on disk or wasn't opened first.");
        }
        else if (!string.IsNullOrEmpty(expectedFileHash))
            throw new ProjectDataException("DBC_FILE_CONFLICT", "Expected DBC disappeared.");
        var content = document.ExportText();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        var temporary = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            return new DbcFileReceipt(path, Hash(bytes), document.Model.Revision);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
