using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Resources;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Only presentation state and UX commands. Project data remains in ProjectWorkspaceService.</summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly NewProjectDialogService _newDialog;
    private readonly ProjectFileDialogService _fileDialogs;
    private string _statusMessage = "尚未打开工程";
    private bool _isBusy;
    private ResourceTreeNode? _selectedResource;

    public MainWindowViewModel(
        ProjectWorkspaceService workspace, NewProjectDialogService newDialog,
        ProjectFileDialogService fileDialogs)
    {
        _workspace = workspace;
        _newDialog = newDialog;
        _fileDialogs = fileDialogs;
        NewProjectCommand = new DelegateCommand(OpenNewProject, () => !IsBusy);
        OpenProjectCommand = new DelegateCommand(async () => await OpenProjectAsync(), () => !IsBusy);
        NewTestProgramCommand = new DelegateCommand(async () => await CreateTestProgramAsync(),
            () => HasProject && !IsBusy);
        ImportDbcCommand = new DelegateCommand(async () => await ImportDbcAsync(),
            () => HasProject && !IsBusy);
        RenameResourceCommand = new DelegateCommand(async () => await RenameResourceAsync(),
            () => _selectedResource?.Asset is not null && !IsBusy);
        RemoveReferenceCommand = new DelegateCommand(async () => await RemoveReferenceAsync(),
            () => _selectedResource?.Asset is not null && !IsBusy);
        _workspace.CurrentChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(CurrentProjectName));
            RaisePropertyChanged(nameof(CurrentProjectPath));
            RaisePropertyChanged(nameof(AssetCount));
            RebuildResourceTree();
            StatusMessage = _workspace.Current is { } current
                ? "工程已载入：" + current.ManifestPath
                : "尚未打开工程";
            RefreshCommands();
        };
        RebuildResourceTree();
    }

    public ObservableCollection<ResourceTreeNode> ResourceNodes { get; } = new();
    public bool HasProject => _workspace.Current is not null;
    public string CurrentProjectName => _workspace.Current?.Definition.Name ?? "未打开工程";
    public string CurrentProjectPath => _workspace.Current?.ManifestPath ?? "";
    public int AssetCount => _workspace.Current?.Definition.Assets.Length ?? 0;
    public bool HasSelectedResource => _selectedResource is not null && HasProject;
    public bool SelectedResourceIsAsset => _selectedResource?.Asset is not null;
    public string SelectedResourceTitle => _selectedResource?.Label ?? "";
    public string SelectedResourceDescription => _selectedResource?.Description ?? "";
    public string SelectedResourcePath => _selectedResource?.Asset?.RelativePath ?? "";
    public string SelectedResourceRevision => _selectedResource?.Asset?.Revision.ToString() ?? "";
    public string SelectedResourceKind => _selectedResource?.Asset?.Kind ?? "";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RefreshCommands();
        }
    }

    public DelegateCommand NewProjectCommand { get; }
    public DelegateCommand OpenProjectCommand { get; }
    public DelegateCommand NewTestProgramCommand { get; }
    public DelegateCommand ImportDbcCommand { get; }
    public DelegateCommand RenameResourceCommand { get; }
    public DelegateCommand RemoveReferenceCommand { get; }

    public void SelectResource(ResourceTreeNode? item)
    {
        if (ReferenceEquals(_selectedResource, item)) return;
        if (_selectedResource is not null) _selectedResource.IsSelected = false;
        _selectedResource = item;
        if (item is not null) item.IsSelected = true;
        RaiseSelectedResourceChanges();
    }

    private void RaiseSelectedResourceChanges()
    {
        RaisePropertyChanged(nameof(HasSelectedResource));
        RaisePropertyChanged(nameof(SelectedResourceIsAsset));
        RaisePropertyChanged(nameof(SelectedResourceTitle));
        RaisePropertyChanged(nameof(SelectedResourceDescription));
        RaisePropertyChanged(nameof(SelectedResourcePath));
        RaisePropertyChanged(nameof(SelectedResourceRevision));
        RaisePropertyChanged(nameof(SelectedResourceKind));
        RefreshCommands();
    }

    private void RebuildResourceTree()
    {
        var selectedId = _selectedResource?.Asset?.Id;
        ResourceNodes.Clear();
        _selectedResource = null;
        if (_workspace.Current is not { } current)
        {
            RaiseSelectedResourceChanges();
            return;
        }

        var root = new ResourceTreeNode(current.Definition.Name, "▣", "当前工程的实际资源清单");
        var groups = new[]
        {
            (Key: ProjectWorkspaceService.TestProgramKind, Title: "测试程序与用例",
             Icon: "▤", Empty: "尚未添加测试程序"),
            (Key: ProjectWorkspaceService.DbcKind, Title: "DBC 数据库",
             Icon: "▥", Empty: "尚未导入 DBC"),
            (Key: "controller", Title: "控制器与按键配置",
             Icon: "⚙", Empty: "尚无控制器配置"),
            (Key: "wave", Title: "波形参考",
             Icon: "⌁", Empty: "尚无参考波形"),
            (Key: "firmware", Title: "固件与烧录资源",
             Icon: "◫", Empty: "尚无固件资源")
        };
        var assets = current.Definition.Assets;
        foreach (var category in groups)
        {
            var entries = assets.Where(x => x.Kind.Equals(category.Key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var group = new ResourceTreeNode(category.Title + " (" + entries.Length + ")",
                category.Icon,
                category.Key == ProjectWorkspaceService.TestProgramKind
                    ? "测试用例属于测试程序。编辑器将在后续阶段接入。"
                    : "工程资源分类；空分类不产生磁盘资源。", category.Key);
            if (entries.Length == 0)
                group.Children.Add(new ResourceTreeNode(category.Empty, "·",
                    "这是空分类提示，不是已保存的资源。", category.Key, isPlaceholder: true));
            else foreach (var entry in entries)
                group.Children.Add(ToAssetNode(entry));
            root.Children.Add(group);
        }
        var defined = groups.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = assets.Where(x => !defined.Contains(x.Kind)).ToArray();
        if (extras.Length > 0)
        {
            var other = new ResourceTreeNode("其他资源 (" + extras.Length + ")", "▦",
                "来自工程清单的其他资源");
            foreach (var entry in extras) other.Children.Add(ToAssetNode(entry));
            root.Children.Add(other);
        }
        ResourceNodes.Add(root);
        if (selectedId is { } id)
        {
            _selectedResource = root.Children.SelectMany(x => x.Children)
                .FirstOrDefault(x => x.Asset?.Id == id);
            if (_selectedResource is not null) _selectedResource.IsSelected = true;
        }
        RaiseSelectedResourceChanges();
    }

    private static ResourceTreeNode ToAssetNode(AssetEntry entry) => new(
        entry.Name, "◦",
        "真实工程资源 · " + entry.RelativePath + " · Revision " + entry.Revision,
        entry.Kind, entry);

    private void OpenNewProject()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { _newDialog.ShowDialog(); }
        catch (Exception e) { StatusMessage = "新建工程窗口打开失败：" + e.Message; }
        finally { IsBusy = false; }
    }

    private async Task OpenProjectAsync()
    {
        var path = _fileDialogs.PickProjectManifest();
        if (path is null) return;
        await RunOperationAsync(async () =>
        {
            await _workspace.OpenAsync(path);
            StatusMessage = "工程已打开：" + path;
        });
    }

    private async Task CreateTestProgramAsync()
    {
        if (!HasProject) return;
        var name = _fileDialogs.AskResourceName("新建测试程序", "新建测试程序");
        if (name is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.CreateTestProgramAsync(name);
            SelectAsset(entry.Id);
            StatusMessage = "测试程序已创建：" + entry.Name;
        });
    }

    private async Task ImportDbcAsync()
    {
        if (!HasProject) return;
        var path = _fileDialogs.PickDbcFile(_workspace.Current?.RootDirectory);
        if (path is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.ImportDbcAsync(path);
            SelectAsset(entry.Id);
            StatusMessage = "DBC 已导入工程：" + entry.Name;
        });
    }

    private async Task RenameResourceAsync()
    {
        var asset = _selectedResource?.Asset;
        if (asset is null) return;
        var name = _fileDialogs.AskResourceName("重命名工程资源", asset.Name);
        if (name is null || name == asset.Name) return;
        await RunOperationAsync(async () =>
        {
            await _workspace.RenameAssetAsync(asset.Id, name);
            SelectAsset(asset.Id);
            StatusMessage = "已更新资源显示名称：" + name;
        });
    }

    private async Task RemoveReferenceAsync()
    {
        var asset = _selectedResource?.Asset;
        if (asset is null || !_fileDialogs.ConfirmRemoveReference(asset.Name)) return;
        await RunOperationAsync(async () =>
        {
            await _workspace.RemoveAssetReferenceAsync(asset.Id);
            SelectResource(null);
            StatusMessage = "已移除工程资源引用，磁盘原文件保留：" + asset.Name;
        });
    }

    private void SelectAsset(AssetId id)
    {
        var root = ResourceNodes.FirstOrDefault();
        SelectResource(root?.Children.SelectMany(x => x.Children).FirstOrDefault(x => x.Asset?.Id == id));
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
                                     InvalidDataException or FormatException or NotSupportedException or
                                     AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "操作失败：" + e.Message;
            _fileDialogs.ShowError(e.Message);
        }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        NewProjectCommand?.RaiseCanExecuteChanged();
        OpenProjectCommand?.RaiseCanExecuteChanged();
        NewTestProgramCommand?.RaiseCanExecuteChanged();
        ImportDbcCommand?.RaiseCanExecuteChanged();
        RenameResourceCommand?.RaiseCanExecuteChanged();
        RemoveReferenceCommand?.RaiseCanExecuteChanged();
    }
}
