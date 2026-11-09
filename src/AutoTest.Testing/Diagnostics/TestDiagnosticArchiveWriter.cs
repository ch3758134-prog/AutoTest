using System.Text;

namespace AutoTest.Testing.Diagnostics;

/// <summary>
/// Private, best-effort TEST diagnostic snapshot writer. It does not implement a production
/// RunRecord, does not acquire shared Records locks, and never authorizes physical execution.
/// The caller is responsible for redaction of bundle content.
/// </summary>
public static class TestDiagnosticArchiveWriter
{
    public static string Save(string folder, TestDiagnosticBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("A fully qualified TEST diagnostics folder is required", nameof(folder));
        ArgumentNullException.ThrowIfNull(bundle);
        Directory.CreateDirectory(folder);
        var fileName = $"TEST_{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}_{Guid.NewGuid():N}.testdiag.json";
        var destination = Path.Combine(folder, fileName);
        var temporary = destination + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(TestDiagnosticArchive.ToJson(bundle));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
