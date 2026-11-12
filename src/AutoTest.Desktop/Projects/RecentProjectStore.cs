using System.IO;
using System.Text;
using System.Text.Json;

namespace AutoTest.Desktop.Projects;

/// <summary>Per-Windows-user recent project preference. Never stored in the source repository.</summary>
public sealed class RecentProjectStore
{
    private sealed record Settings(int Version, string? LastProjectManifest);
    private readonly string _settingsFile;

    public RecentProjectStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "desktop-settings.json")) { }

    public RecentProjectStore(string settingsFile) =>
        _settingsFile = Path.GetFullPath(settingsFile);

    public string? GetLastProjectManifest()
    {
        if (!File.Exists(_settingsFile)) return null;
        var data = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsFile, Encoding.UTF8));
        if (data?.Version != 1 || string.IsNullOrWhiteSpace(data.LastProjectManifest)) return null;
        if (!Path.IsPathFullyQualified(data.LastProjectManifest)) return null;
        return Path.GetFullPath(data.LastProjectManifest);
    }

    public void Remember(string manifest)
    {
        var path = Path.GetFullPath(manifest);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("工程清单必须为绝对路径。");
        if (string.Equals(GetLastProjectManifest(), path, StringComparison.OrdinalIgnoreCase))
            return;
        var parent = Path.GetDirectoryName(_settingsFile)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".recent-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var json = JsonSerializer.Serialize(new Settings(1, path),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, _settingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
