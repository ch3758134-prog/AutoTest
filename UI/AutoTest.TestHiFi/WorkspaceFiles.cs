using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTest.TestHiFi;

/// <summary>
/// Real local file operations for a UI-only workspace. The manifest is deliberately
/// named *.ui-workspace.json: it is NOT the formal AutoTest PROJECT/TEST schema.
/// Nothing in this class talks to the device or manufactures ECU run results.
/// </summary>
public sealed class UiWorkspace
{
    public int Version { get; set; } = 1;
    public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<UiAsset> Assets { get; set; } = [];
}
public sealed class UiAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "测试程序";
    public string RelativePath { get; set; } = "";
    public int Revision { get; set; } = 1;
}
public sealed class UiDocument
{
    public UiAsset Asset { get; init; } = null!;
    public string Content { get; set; } = "";
    public string SavedText { get; set; } = "";
    public bool Dirty => !string.Equals(Content, SavedText, StringComparison.Ordinal);
    public string Caption => Asset.Name + (Dirty ? " *" : "");
}
public sealed class UiSession
{
    public string? LastProjectManifest { get; set; }
    public string? LastOpenAssetId { get; set; }
    public List<string> OpenAssetIds { get; set; } = [];
}

public sealed class UiWorkspaceFiles
{
    public const string ManifestName = ".autotest-ui-workspace.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public UiWorkspace? Workspace { get; private set; }
    public string? Root { get; private set; }
    public string? Manifest => Root is null ? null : Path.Combine(Root, ManifestName);
    public bool HasProject => Workspace is not null && Root is not null;
    public ObservableCollection<UiDocument> OpenDocuments { get; } = [];
    public static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "TestHiFi", "ui-session.json");

    public void Create(string parentDirectory, string projectName)
    {
        var name = CleanName(projectName);
        var root = Path.GetFullPath(Path.Combine(parentDirectory, name));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("目标工程目录已经存在且不为空，请换一个名称。");
        Directory.CreateDirectory(root);
        foreach (var segment in new[] { "tests", "flows", "dbc", "waves", "config", "flash" })
            Directory.CreateDirectory(Path.Combine(root, segment));
        Root = root;
        Workspace = new UiWorkspace { Name = name };
        OpenDocuments.Clear();
        SaveManifest();
    }

    public void Open(string projectManifestPath)
    {
        var path = Path.GetFullPath(projectManifestPath);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到工程清单。", path);
        if (!string.Equals(Path.GetFileName(path), ManifestName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("只能打开本 UI 版本创建的 .autotest-ui-workspace.json，不能冒充正式 AutoTest 工程。");
        var data = JsonSerializer.Deserialize<UiWorkspace>(File.ReadAllText(path, Encoding.UTF8));
        if (data is null || data.Version != 1 || string.IsNullOrWhiteSpace(data.Name) || data.Assets is null)
            throw new InvalidDataException("UI 工作区清单结构无效或版本不受支持。");
        if (data.Assets.Count > 5000 ||
            data.Assets.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != data.Assets.Count ||
            data.Assets.Any(x => string.IsNullOrWhiteSpace(x.Id) ||
                                 string.IsNullOrWhiteSpace(x.Name) ||
                                 string.IsNullOrWhiteSpace(x.RelativePath)))
            throw new InvalidDataException("工程资源清单含有无效或重复对象。");
        var root = Path.GetDirectoryName(path)!;
        // Validate every path before accepting untrusted local workspace manifests.
        foreach (var asset in data.Assets) PathFor(root, asset.RelativePath);
        Workspace = data;
        Root = root;
        OpenDocuments.Clear();
    }

    public UiAsset NewAsset(string kind, string displayName)
    {
        Require();
        var name = CleanName(displayName);
        var folder = kind switch
        {
            "测试程序" => "tests",
            "公共流程" => "flows",
            "DBC" => "dbc",
            "参考波形" => "waves",
            "烧录配置" => "flash",
            _ => "config"
        };
        var ext = kind == "DBC" ? ".dbc" : kind == "参考波形" ? ".csv" : ".json";
        var relative = Path.Combine(folder, name + ext).Replace('\\', '/');
        if (Workspace!.Assets.Any(a => string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(FullPath(relative))) throw new IOException("该名称已存在，请更换资源名称。");
        var asset = new UiAsset { Name = name, Kind = kind, RelativePath = relative };
        string initial = kind == "DBC" ? "VERSION \"UI WORKSPACE - NOT A REAL ECU DBC\"\n" :
            kind == "参考波形" ? "time_ms,value\n" :
            "{\n  \"uiOnly\": true,\n  \"name\": " + JsonSerializer.Serialize(name) + ",\n  \"notes\": \"\"\n}\n";
        SaveAtomic(FullPath(relative), initial);
        Workspace.Assets.Add(asset);
        try { SaveManifest(); }
        catch { Workspace.Assets.Remove(asset); File.Delete(FullPath(relative)); throw; }
        return asset;
    }

    public UiAsset Import(string source, string kind, string? displayName = null)
    {
        Require();
        var file = Path.GetFullPath(source);
        if (!File.Exists(file)) throw new FileNotFoundException("导入文件不存在。", file);
        var name = CleanName(displayName ?? Path.GetFileNameWithoutExtension(file));
        var folder = kind switch
        {
            "DBC" => "dbc", "参考波形" => "waves", "测试程序" => "tests",
            "公共流程" => "flows", "烧录配置" => "flash", _ => "config"
        };
        // File import is an explicit COPY into the workspace, never an outside link.
        var relative = Path.Combine(folder, name + Path.GetExtension(file)).Replace('\\','/');
        var target = FullPath(relative);
        if (File.Exists(target) || Workspace!.Assets.Any(a =>
             string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("目标已存在，未覆盖任何文件。请先重命名或另选名称。");
        File.Copy(file, target, overwrite:false);
        var asset = new UiAsset { Name=name, Kind=kind, RelativePath=relative };
        Workspace.Assets.Add(asset);
        try { SaveManifest(); }
        catch { Workspace.Assets.Remove(asset); File.Delete(target); throw; }
        return asset;
    }

    public UiDocument OpenAsset(UiAsset asset)
    {
        RequireMember(asset);
        var old = OpenDocuments.FirstOrDefault(d => d.Asset.Id == asset.Id);
        if (old is not null) return old;
        string text = File.ReadAllText(FullPath(asset.RelativePath), Encoding.UTF8);
        var d = new UiDocument { Asset=asset, Content=text, SavedText=text };
        OpenDocuments.Add(d);
        return d;
    }

    public void Save(UiDocument doc)
    {
        RequireMember(doc.Asset);
        var path = FullPath(doc.Asset.RelativePath);
        SaveAtomic(path, doc.Content);
        doc.SavedText = doc.Content;
        doc.Asset.Revision++;
        SaveManifest();
    }

    public void SaveAll()
    {
        foreach (var doc in OpenDocuments.Where(d => d.Dirty).ToArray()) Save(doc);
    }

    public UiAsset Rename(UiAsset asset, string newName)
    {
        RequireMember(asset);
        var name = CleanName(newName);
        if (name == asset.Name) return asset;
        var relative = Path.Combine(Path.GetDirectoryName(asset.RelativePath) ?? "",
                        name + Path.GetExtension(asset.RelativePath)).Replace('\\','/');
        var oldPath = FullPath(asset.RelativePath);
        var newPath = FullPath(relative);
        if (File.Exists(newPath) || Workspace!.Assets.Any(a => a.Id != asset.Id &&
              string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("同名资源已经存在，重命名已取消。");
        File.Move(oldPath, newPath);
        var oldRelative = asset.RelativePath;
        var oldName = asset.Name;
        asset.Name = name; asset.RelativePath = relative;
        try { SaveManifest(); }
        catch { asset.Name = oldName; asset.RelativePath = oldRelative; File.Move(newPath, oldPath); throw; }
        return asset;
    }

    public UiAsset Duplicate(UiAsset asset)
    {
        RequireMember(asset);
        string candidate = asset.Name + " 副本";
        int i = 2;
        while (Workspace!.Assets.Any(a => a.Kind == asset.Kind && a.Name == candidate))
            candidate = asset.Name + " 副本 " + i++;
        var target = NewAsset(asset.Kind, candidate);
        File.Copy(FullPath(asset.RelativePath), FullPath(target.RelativePath), overwrite:true);
        return target;
    }

    public void Delete(UiAsset asset)
    {
        RequireMember(asset);
        if (OpenDocuments.Any(d => d.Asset.Id == asset.Id && d.Dirty))
            throw new InvalidOperationException("资源存在未保存更改，请先保存或放弃编辑。");
        var source = FullPath(asset.RelativePath);
        var trash = Path.Combine(Root!, ".ui-trash");
        Directory.CreateDirectory(trash);
        var dest = Path.Combine(trash, asset.Id + "_" + Path.GetFileName(source));
        if (File.Exists(source)) File.Move(source, dest);
        Workspace!.Assets.Remove(asset);
        foreach (var doc in OpenDocuments.Where(d => d.Asset.Id == asset.Id).ToArray()) OpenDocuments.Remove(doc);
        try { SaveManifest(); }
        catch
        {
            Workspace.Assets.Add(asset);
            if (File.Exists(dest)) File.Move(dest, source);
            throw;
        }
    }

    public void SaveManifest()
    {
        Require();
        SaveAtomic(Manifest!, JsonSerializer.Serialize(Workspace, JsonOptions));
    }
    public string FullPath(string relative) { Require(); return PathFor(Root!, relative); }

    private static string PathFor(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("不允许工程外的绝对资源路径：" + relative);
        var canonical = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        if (!canonical.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源路径超出工程目录。");
        return canonical;
    }
    private static string CleanName(string input)
    {
        var name = input.Trim();
        if (name.Length == 0 || name.Length > 70 || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("名称不能为空、过长或含有不允许的文件字符。");
        return name;
    }
    private void Require() { if (!HasProject) throw new InvalidOperationException("请先新建或打开 UI 工作区。"); }
    private void RequireMember(UiAsset asset)
    {
        Require();
        if (!Workspace!.Assets.Any(a => a.Id == asset.Id))
            throw new InvalidOperationException("当前资源不属于打开的工程。");
    }
    private static void SaveAtomic(string path, string text)
    {
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        var tmp = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite:true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public static UiSession ReadSession()
    {
        try
        {
            if (!File.Exists(SessionPath)) return new UiSession();
            return JsonSerializer.Deserialize<UiSession>(File.ReadAllText(SessionPath)) ?? new UiSession();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UiSession();
        }
    }
    public static void SaveSession(UiSession session)
    {
        SaveAtomic(SessionPath, JsonSerializer.Serialize(session, JsonOptions));
    }
}
