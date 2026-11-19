using System.IO;
using System.Text;
using System.Text.Json;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Local, presentation-only last-document preference. Never changes a project
/// manifest, TEST/PROJECT asset, run selection or revision. A remembered key is
/// always revalidated against the currently opened ProjectWorkspaceService.
/// </summary>
public sealed class RecentEditorDocumentStore
{
    private sealed record Snapshot(int Version, Dictionary<string, string> RecentEditors);
    private readonly string _path;

    public RecentEditorDocumentStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "last-editor.json")) { }

    public RecentEditorDocumentStore(string settingsPath) =>
        _path = Path.GetFullPath(settingsPath);

    public string? Read(string manifest)
    {
        var key = Path.GetFullPath(manifest);
        if (!File.Exists(_path)) return null;
        var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path, Encoding.UTF8));
        if (saved?.Version != 1 || saved.RecentEditors is null) return null;
        return new Dictionary<string, string>(saved.RecentEditors, StringComparer.OrdinalIgnoreCase)
            .TryGetValue(key, out var editor) && IsResourceKey(editor)
            ? editor : null;
    }

    public void Remember(string manifest, string editorKey)
    {
        if (!IsResourceKey(editorKey)) return;
        var project = Path.GetFullPath(manifest);
        Dictionary<string, string> entries;
        try
        {
            var saved = File.Exists(_path)
                ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path, Encoding.UTF8))
                : null;
            entries = saved?.Version == 1 && saved.RecentEditors is not null
                ? new Dictionary<string, string>(saved.RecentEditors, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (entries.TryGetValue(project, out var old) && old == editorKey) return;
        entries[project] = editorKey;
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, ".last-editor-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp,
                JsonSerializer.Serialize(new Snapshot(1, entries), new JsonSerializerOptions
                { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool IsResourceKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var separator = key.LastIndexOf('|');
        if (separator <= 0 || !Guid.TryParse(key[(separator + 1)..], out _))
            return false;
        var prefix = key[..separator];
        return prefix is "test" or "project" or "dbc" or "config";
    }
}
