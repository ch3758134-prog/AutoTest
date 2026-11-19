using System.IO;
using System.Text;
using System.Text.Json;

namespace AutoTest.Desktop.Projects;

/// <summary>Per-Windows-user recent project preference. Never stored in the source repository.</summary>
public sealed class RecentProjectStore
{
    private sealed record Settings(int Version, string? LastProjectManifest, string? LastBrowseDirectory = null);
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


    public string? GetLastBrowseDirectory()
    {
        var state = LoadSettings();
        var directory = state?.LastBrowseDirectory;
        if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(state?.LastProjectManifest))
            directory = Path.GetDirectoryName(state.LastProjectManifest);
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return null;
        var path = Path.GetFullPath(directory);
        return Directory.Exists(path) ? path : null;
    }

    public void RememberDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return;
        var old = LoadSettings();
        SaveSettings(new Settings(1, old?.LastProjectManifest, path));
    }

    public void Remember(string manifest)
    {
        var path = Path.GetFullPath(manifest);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("工程清单必须为绝对路径。");
        var old = LoadSettings();
        if (old?.LastProjectManifest == path && old.LastBrowseDirectory == Path.GetDirectoryName(path)) return;
        SaveSettings(new Settings(1, path, Path.GetDirectoryName(path)));
    }

    private Settings? LoadSettings()
    {
        try
        {
            return File.Exists(_settingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsFile, Encoding.UTF8))
                : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveSettings(Settings value)
    {
        var parent = Path.GetDirectoryName(_settingsFile)!;
        Directory.CreateDirectory(parent);
        var temp = Path.Combine(parent, ".recent-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, _settingsFile, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
