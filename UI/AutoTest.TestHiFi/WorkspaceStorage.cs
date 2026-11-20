using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace AutoTest.TestHiFi;

// UI-02 has its own explicitly named draft file format. It is NOT a replacement
// for the existing PROJECT/TEST formal Schema, nor a converter for real ECU data.
public sealed class UiWorkspaceManifest
{
    public string Format { get; set; } = "AutoTest.UI.Workspace/1";
    public string Name { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<UiAsset> Assets { get; set; } = [];
}

public sealed class UiAsset : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "说明";
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this,new(nameof(Name))); }
    }
    public string RelativePath { get; set; } = "";
    public string KindLabel => Kind switch
    {
        "TEST" => "测试程序", "FLOW" => "公共流程",
        "DBC" => "DBC", "WAVE" => "参考波形",
        "INPUT" => "输入配置", "UDS" => "诊断配置",
        "FLASH" => "烧录配置", _ => "文档"
    };
    [JsonIgnore] public string TreeLabel => KindLabel + "   ·   " + Name;
}

public sealed class UiFileDocument : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _content = "";
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private bool _loading;
    public UiAsset Asset { get; init; } = new();
    public string FilePath { get; init; } = "";
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            if (!_loading)
            {
                _undo.Push(_content);
                if (_undo.Count > 1000) TrimUndo();
                _redo.Clear();
            }
            _content = value;
            Changed = _content != LastSavedContent;
            PropertyChanged?.Invoke(this,new(nameof(Content)));
        }
    }
    public string LastSavedContent { get; private set; } = "";
    private bool _changed;
    public bool Changed
    {
        get => _changed;
        private set
        {
            _changed = value;
            PropertyChanged?.Invoke(this,new(nameof(Changed)));
            PropertyChanged?.Invoke(this,new(nameof(DisplayName)));
        }
    }
    public string DisplayName => Asset.Name + (Changed ? " ●" : "");
    public string KindLabel => Asset.KindLabel;
    private void TrimUndo()
    {
        var keep = _undo.ToArray().Take(500).Reverse().ToArray();
        _undo.Clear();
        foreach (var entry in keep) _undo.Push(entry);
    }
    public void LoadContent(string text)
    {
        _loading = true;
        _content = text; LastSavedContent = text;
        _undo.Clear(); _redo.Clear();
        Changed = false; _loading = false;
        PropertyChanged?.Invoke(this,new(nameof(Content)));
    }
    public void MarkSaved()
    {
        LastSavedContent = Content;
        Changed = false;
    }
    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_content);
        SetWithoutHistory(_undo.Pop());
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_content);
        SetWithoutHistory(_redo.Pop());
    }
    private void SetWithoutHistory(string value)
    {
        _content = value; Changed = _content != LastSavedContent;
        PropertyChanged?.Invoke(this,new(nameof(Content)));
    }
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
}

public sealed class WorkspacePage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; init; } = "";
    public string Kind { get; init; } = "TEST_PREVIEW"; // TEST_PREVIEW, FILE, TOOL
    public string Title { get; set; } = "";
    public UiFileDocument? Document { get; init; }
    public bool IsTestPreview => Kind == "TEST_PREVIEW";
    private bool _active;
    public bool Active
    {
        get => _active;
        set { _active = value; PropertyChanged?.Invoke(this,new(nameof(Active))); }
    }
    public string Label => (Document?.DisplayName ?? Title);
    public void NotifyLabel() => PropertyChanged?.Invoke(this,new(nameof(Label)));
}

public sealed class LocalSession
{
    public string? ProjectPath { get; set; }
    public List<string> OpenPageKeys { get; set; } = [];
    public string? ActivePageKey { get; set; }
    public double NavigatorWidth { get; set; } = 294;
    public double InspectorWidth { get; set; } = 400;
    public double IssuesHeight { get; set; } = 165;
    public bool IssuesExpanded { get; set; } = true;
    public List<string> RecentProjects { get; set; } = [];
}

// Persist user-created artifacts, not copied copies of formal domain models.
// One manifest + independent files; files are UTF-8 text and can be opened externally.
public static class WorkspaceStorage
{
    public const string Extension = ".atui-project.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "UiWorkbench");
    public static string SessionFile => Path.Combine(UserFolder, "session.json");
    public static string RecoveryFile => Path.Combine(UserFolder, "recovery.json");
    public static readonly string[] AssetKinds =
    [
        "TEST", "FLOW", "DBC", "WAVE", "INPUT", "UDS", "FLASH", "NOTE"
    ];
    public static UiWorkspaceManifest New(string name)
        => new() { Name = name.Trim() };
    public static string DefaultText(UiAsset asset) => asset.Kind switch
    {
        "TEST" => "# " + asset.Name + "\n# TEST 文档 UI 草稿。正式步骤执行模型将在 UI-03 接入。\n准备:\n测试:\n清理:\n",
        "FLOW" => "# " + asset.Name + "\n# 公共流程 UI 草稿，当前不做正式 StepId 与 Revision 解析。\n",
        "DBC" => "VERSION \"\"\\nNS_ :\\nBS_: \\nBU_: DUT\\n\\nCM_ \"UI design example, NOT production data\";\\n",
        "WAVE" => "time_ms,value\n0,0\n10,0\n",
        "INPUT" => "# 输入配置 UI 草稿\n# 20 Key 是组合电阻网络；五路模拟输出另行正式映射。\n",
        "UDS" => "# UDS 配置 UI 草稿\n",
        "FLASH" => "# 烧录配置 UI 草稿（不包含实际文件或密钥）\n",
        _ => "# 文档\n"
    };
    public static string ExtensionFor(string kind)
        => kind == "DBC" ? ".dbc" : kind == "WAVE" ? ".csv" : ".txt";
    public static string UniqueFilePath(string projectPath, string kind)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var directory = Path.Combine(root, "ui-assets");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, kind.ToLowerInvariant() + "-" +
            Guid.NewGuid().ToString("N")[..12] + ExtensionFor(kind));
    }
    public static string Resolve(string projectPath, string relative)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relative) ||
            !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            relative.Split('/', '\\').Any(x => x == ".."))
            throw new InvalidDataException("文件路径不属于当前工程目录：" + relative);
        return full;
    }
    public static void WriteAtomic(string file, string text)
    {
        var full = Path.GetFullPath(file);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var tmp = full + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            // A copy of the old file remains available if a replacement fails later.
            if (File.Exists(full)) File.Copy(full, full + ".bak", true);
            File.Move(tmp, full, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static void SaveManifest(string projectPath, UiWorkspaceManifest manifest)
        => WriteAtomic(projectPath, JsonSerializer.Serialize(manifest, Json));
    public static UiWorkspaceManifest OpenManifest(string projectPath)
    {
        if (!projectPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("此预览版只打开 *.atui-project.json。正式工程请用 AutoTest 正式版本。");
        if (new FileInfo(projectPath).Length > 2_000_000)
            throw new InvalidDataException("工程清单超过 UI 工作台的 2 MB 上限。");
        var manifest = JsonSerializer.Deserialize<UiWorkspaceManifest>(File.ReadAllText(projectPath))
            ?? throw new InvalidDataException("工程清单无效。");
        if (manifest.Format != "AutoTest.UI.Workspace/1" || manifest.Assets.Count > 500)
            throw new InvalidDataException("不支持的 UI 工程格式或资源过多。");
        if (string.IsNullOrWhiteSpace(manifest.Name) ||
            manifest.Assets.Any(a => string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.RelativePath)) ||
            manifest.Assets.Select(a => a.Id).Distinct().Count() != manifest.Assets.Count)
            throw new InvalidDataException("工程清单缺少必要字段或 ID 重复。");
        foreach (var asset in manifest.Assets) _ = Resolve(projectPath, asset.RelativePath);
        return manifest;
    }
    public static string ReadText(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException("资源文件不存在", file);
        if (info.Length > 4_000_000)
            throw new InvalidDataException("UI 文本编辑器暂时不支持大于 4 MB 的文件。");
        return File.ReadAllText(file);
    }
    public static void SaveSession(LocalSession state)
        => WriteAtomic(SessionFile, JsonSerializer.Serialize(state, Json));
    public static LocalSession ReadSession()
    {
        try { return JsonSerializer.Deserialize<LocalSession>(File.ReadAllText(SessionFile)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void SaveRecovery(Dictionary<string,string> drafts)
    {
        if (drafts.Count == 0) { if(File.Exists(RecoveryFile)) File.Delete(RecoveryFile); return; }
        WriteAtomic(RecoveryFile, JsonSerializer.Serialize(drafts, Json));
    }
    public static Dictionary<string,string> ReadRecovery()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(RecoveryFile)) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
