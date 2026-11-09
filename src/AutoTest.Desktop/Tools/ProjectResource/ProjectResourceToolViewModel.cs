using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Tools.ProjectResource;

public sealed class ProjectResourceCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}

public sealed class ProjectResourceToolViewModel : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly R0ProjectDiagnosticJournal _journal = new();
    private R0ProjectAssetWorkspace? _workspace;
    private R0AssetCheck? _selectedAsset;
    private string _traceFilter = "";
    private string _status = "未打开工程 / SOFTWARE_ONLY";
    private string _error = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));

    public string ProjectFilePath { get; set; } = "";
    public string ProjectName { get; set; } = "New ECU Project";
    public string ImportFilePath { get; set; } = "";
    public string ResourceKind { get; set; } = "DBC";
    public string ResourceName { get; set; } = "";
    public string NewResourceName { get; set; } = "";
    public string TraceFilePath { get; set; } = "";
    public bool RedactTracePaths { get; set; } = true;
    public bool IncludeRawTraceHex { get; set; } = true;
    public IReadOnlyList<string> ResourceKinds { get; } = ["DBC", "Controller", "Profile"];
    public ObservableCollection<R0AssetCheck> Assets { get; } = [];
    public ObservableCollection<R0ProjectDiagnosticEvent> TraceEvents { get; } = [];
    public IEnumerable<R0ProjectDiagnosticEvent> VisibleTraceEvents => TraceEvents.Where(x =>
        string.IsNullOrWhiteSpace(TraceFilter) ||
        (x.Stage + " " + x.ErrorCode + " " + x.Detail + " " + x.AssetId).Contains(TraceFilter, StringComparison.OrdinalIgnoreCase));
    public string TraceFilter
    {
        get => _traceFilter;
        set { _traceFilter = value ?? ""; Notify(); Notify(nameof(VisibleTraceEvents)); }
    }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string Error { get => _error; private set { _error = value; Notify(); } }
    public string ProjectSummary => _workspace is null ? "工程未绑定 / Revision UNKNOWN" :
        $"ProjectId {_workspace.Definition.Id.Value:D} / Revision {_workspace.Definition.Revision} / " +
        $"资产 {_workspace.Assets.Count} / 过期引用 {_workspace.StaleDependencies.Count} / SOFTWARE_ONLY";
    public string TraceQuality => $"日志 {TraceEvents.Count} / Dropped {_journal.DroppedCount} / SOFTWARE_ONLY";
    public R0AssetCheck? SelectedAsset
    {
        get => _selectedAsset;
        set { _selectedAsset = value; Notify(); Notify(nameof(SelectedAssetDetails)); }
    }
    public string SelectedAssetDetails => SelectedAsset is null ? "未选择资源" :
        $"AssetId {SelectedAsset.Asset.Id.Value:D} / Rev {SelectedAsset.Asset.Revision} / " +
        $"Expected SHA256 {SelectedAsset.Asset.Sha256} / Actual {SelectedAsset.ActualSha256} / {SelectedAsset.Diagnostic}";

    public ICommand CreateProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand VerifyAssetsCommand { get; }
    public ICommand ImportResourceCommand { get; }
    public ICommand RenameResourceCommand { get; }
    public ICommand UnlinkResourceCommand { get; }
    public ICommand CopyAssetPathCommand { get; }
    public ICommand CopyVisibleTraceCommand { get; }
    public ICommand ExportTraceCommand { get; }

    public ProjectResourceToolViewModel()
    {
        CreateProjectCommand = new ProjectResourceCommand(() => _ = RunAsync("PROJECT_CREATE", async () =>
        {
            _workspace = await R0ProjectAssetWorkspace.CreateAsync(ProjectFilePath, ProjectName);
            Refresh();
        }));
        OpenProjectCommand = new ProjectResourceCommand(() => _ = RunAsync("PROJECT_OPEN", async () =>
        {
            _workspace = await R0ProjectAssetWorkspace.OpenAsync(ProjectFilePath);
            Refresh();
        }));
        VerifyAssetsCommand = new ProjectResourceCommand(() => _ = RunAsync("ASSET_VERIFY", async () =>
        {
            await Workspace.RefreshChecksAsync();
            Refresh();
        }));
        ImportResourceCommand = new ProjectResourceCommand(() => _ = RunAsync("ASSET_IMPORT", async () =>
        {
            var result = await Workspace.ImportAsync(ImportFilePath, ResourceKind, ResourceName);
            Refresh(); SelectedAsset = Assets.Single(a => a.Asset.Id == result.Asset.Id);
        }));
        RenameResourceCommand = new ProjectResourceCommand(() => _ = RunAsync("ASSET_RENAME", async () =>
        {
            await Workspace.RenameAsync(NeedAsset().Asset.Id, NewResourceName);
            Refresh();
        }));
        UnlinkResourceCommand = new ProjectResourceCommand(() => _ = RunAsync("ASSET_UNLINK", async () =>
        {
            var asset = NeedAsset().Asset;
            await Workspace.UnlinkAsync(asset.Id);
            Refresh();
        }));
        CopyAssetPathCommand = new ProjectResourceCommand(() =>
        {
            try
            {
                Clipboard.SetText(NeedAsset().FullPath);
                Status = "资源绝对路径已复制；在 DBC/Controller 专业页中选择相同工程并绑定。";
            }
            catch (Exception e) { Fail("COPY_PATH", e); }
        });
        CopyVisibleTraceCommand = new ProjectResourceCommand(() =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, VisibleTraceEvents.Select(e =>
                    $"{e.TimestampUtc:O} | {e.Stage} | {e.Outcome} | {e.ErrorCode} | {e.Detail}")));
                Status = "已复制当前过滤结果";
            }
            catch (Exception e) { Fail("COPY_TRACE", e); }
        });
        ExportTraceCommand = new ProjectResourceCommand(() => _ = RunAsync("TRACE_EXPORT", async () =>
        {
            if (string.IsNullOrWhiteSpace(TraceFilePath))
                throw new ProjectDataException("TRACE_PATH", "请输入诊断 JSONL 导出路径。");
            await _journal.ExportJsonLinesAsync(TraceFilePath, RedactTracePaths, IncludeRawTraceHex);
        }));
    }

    private R0ProjectAssetWorkspace Workspace => _workspace ?? throw new ProjectDataException("PROJECT_UNBOUND", "请先打开或创建工程。");
    private R0AssetCheck NeedAsset() => SelectedAsset ?? throw new ProjectDataException("ASSET_UNSELECTED", "请先选择资源。");
    private void Refresh()
    {
        var selected = SelectedAsset?.Asset.Id;
        Assets.Clear();
        foreach (var asset in Workspace.Assets) Assets.Add(asset);
        SelectedAsset = Assets.FirstOrDefault(x => x.Asset.Id == selected) ?? Assets.FirstOrDefault();
        Notify(nameof(ProjectSummary));
        foreach (var a in Workspace.Assets)
            Log("ASSET_CHECK", a.IsValid ? "ACCEPTED" : "REJECTED", a.IsValid ? "" : "ASSET_" + a.Status,
                asset: a, expected: a.Asset.Sha256, actual: a.ActualSha256,
                detail: $"{a.Asset.Kind} / {a.Asset.Name} / {a.Asset.RelativePath} / {a.Diagnostic}");
        foreach (var dep in Workspace.StaleDependencies)
            Log("REFERENCE_CHECK", "DIAGNOSTIC", "REFERENCE_STALE", expected: $"Rev {dep.RequiredRevision}",
                actual: "Revision mismatch", detail: $"From={dep.From.Value:D} / To={dep.To.Value:D} / {dep.Purpose}");
    }
    private async Task RunAsync(string stage, Func<Task> action)
    {
        if (!await _operations.WaitAsync(0))
        {
            Fail(stage, new ProjectDataException("PROJECT_BUSY", "前一个工程操作尚未结束。"));
            return;
        }
        var operationId = Guid.NewGuid();
        try
        {
            Error = "";
            Log(stage, "REQUESTED", expected: "Validate and perform local project operation", operationId: operationId);
            await action();
            Status = $"{stage} 完成 / {ProjectSummary}";
            Log(stage, "ACCEPTED", actual: ProjectSummary, detail: Status, operationId: operationId);
        }
        catch (Exception e) { Fail(stage, e, operationId); }
        finally { _operations.Release(); }
    }
    private void Fail(string stage, Exception exception, Guid? operationId = null)
    {
        var code = exception is ProjectDataException p ? p.Code : exception.GetType().Name;
        Error = $"{code}: {exception.Message}";
        Status = $"{stage} 已拒绝 / 当前工程不代表真实硬件状态";
        Log(stage, "REJECTED", code, detail: exception.Message, operationId: operationId);
    }
    private void Log(string stage, string outcome, string code = "", R0AssetCheck? asset = null,
        string expected = "", string actual = "", string detail = "", Guid? operationId = null)
    {
        var item = _journal.Record("PROJECT/RESOURCES", stage, outcome, code, project: _workspace?.ProjectPath ?? "",
            assetId: asset?.Asset.Id.Value.ToString("D") ?? "", revision: asset?.Asset.Revision ?? 0,
            sha256: asset?.Asset.Sha256 ?? "", expected: expected, actual: actual, detail: detail,
            operationId: operationId);
        TraceEvents.Add(item);
        if (TraceEvents.Count > 4096) TraceEvents.RemoveAt(0);
        Notify(nameof(VisibleTraceEvents)); Notify(nameof(TraceQuality));
    }
}
