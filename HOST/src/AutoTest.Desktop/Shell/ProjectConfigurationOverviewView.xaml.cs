using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Configuration;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// The four HTML configuration tabs host the canonical saved TCF/Controller/TBC
/// sessions; no alternate persisted config model or duplicate old editor window.
/// </summary>
public partial class ProjectConfigurationOverviewView : UserControl
{
    private readonly MainWindowViewModel _shell;
    private readonly ProjectWorkspaceService _workspace;
    private readonly ProjectFileDialogService _dialogs;
    private readonly List<ConfigurationWorkbenchViewModel> _opened = new();
    private InspectionStationProcedureEditorViewModel? _inspectionEditor;

    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public ProjectConfigurationOverviewView(MainWindowViewModel shell,
        ProjectWorkspaceService workspace, ProjectFileDialogService dialogs)
    {
        InitializeComponent();
        DataContext = shell;
        _shell = shell;
        ScannerSetupHost.Content = new SerialScannerSetupView
        {
            DataContext = shell.ScannerSetupModel
        };
        _workspace = workspace;
        _dialogs = dialogs;
        InspectorHost.Content = new ProjectConfigurationOverviewInspector(shell);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Stage navigation changes the visible part of this one engineer workbench;
    /// it does not create or select another project/recipe.
    /// </summary>
    private void NavigateToStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string stage }) return;
        ConfigTabs.SelectedIndex = stage switch
        {
            "dut" => 1,
            "scan" => 3,
            "flash" => 4,
            "station" => 5,
            "check" => 6,
            _ => 0
        };
        _ = _shell.RefreshEngineeringSetupAsync();
    }

    public void ShowInspectionSession(ProjectConfigurationSession session)
    {
        if (session.Kind != ProjectWorkspaceService.InspectionKind)
            throw new ArgumentException("当前资源不是工程工位动作配置。");
        if (_inspectionEditor is not null && _inspectionEditor.IsDirty)
        {
            System.Diagnostics.Trace.TraceWarning("工位动作草稿仍未保存，保留当前编辑页面。");
            return;
        }
        if (InspectionProcedureHost.Content is InspectionStationProcedureEditorView
            && _inspectionEditor is not null &&
            _workspace.Current?.ManifestPath == session.ManifestPath)
        {
            ConfigTabs.SelectedIndex = 5;
            return;
        }
        _inspectionEditor?.Dispose();
        _inspectionEditor = new InspectionStationProcedureEditorViewModel(_workspace, session);
        InspectionProcedureHost.Content = new InspectionStationProcedureEditorView(_inspectionEditor);
        ConfigTabs.SelectedIndex = 5;
    }

    private async Task LoadInspectionAsync()
    {
        var project = _workspace.Current;
        if (project is null) return;
        var candidates = project.Definition.Assets
            .Where(x => x.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
        if (candidates.Length == 0) return;
        if (candidates.Length != 1)
        {
            InspectionProcedureHost.Content = new TextBlock
            {
                Text = "同一工程存在多个工位动作配置，必须先处理冲突。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12)
            };
            return;
        }
        try
        {
            var session = await _workspace.OpenConfigurationAsync(candidates[0].Id);
            if (!string.Equals(project.ManifestPath, _workspace.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase)) return;
            var currentTab = ConfigTabs.SelectedIndex;
            ShowInspectionSession(session);
            ConfigTabs.SelectedIndex = currentTab;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            ArgumentException or ProjectDataException or UnauthorizedAccessException)
        {
            UiExceptionReporter.Record("Station procedure setup", error);
            InspectionProcedureHost.Content = new TextBlock
            {
                Text = "工位动作配置不可读取：" + error.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12)
            };
        }
    }

    private static bool IsInlineKind(string kind) =>
        kind is ProjectWorkspaceService.TestConfigurationKind or
            ProjectWorkspaceService.BenchConfigurationKind;

    private ContentControl HostFor(string kind) => kind switch
    {
        ProjectWorkspaceService.TestConfigurationKind => DutFormHost,
        ProjectWorkspaceService.BenchConfigurationKind => BenchFormHost,
        _ => throw new ArgumentException("该资源属于独立专业工具，不是自动测试工程配置。")
    };

    public void SelectKind(string kind)
    {
        ConfigTabs.SelectedIndex = kind switch
        {
            ProjectWorkspaceService.TestConfigurationKind => 1,
            ProjectWorkspaceService.BenchConfigurationKind => 2,
            _ => ConfigTabs.SelectedIndex
        };
    }

    public void ShowSession(string kind, ProjectConfigurationSession session)
    {
        if (!IsInlineKind(kind)) throw new ArgumentException("不支持的自动测试配置类型：" + kind);
        var host = HostFor(kind);
        if (host.Content is ConfigurationInlineEditorView existing &&
            existing.DataContext is ConfigurationWorkbenchViewModel previous)
        {
            if (previous.AssetGuid == session.AssetId.Value)
            {
                SelectKind(kind);
                return; // keep edits and focus when opening the same canonical asset
            }
            if (!previous.ConfirmClose()) return;
            _opened.Remove(previous);
        }
        var model = new ConfigurationWorkbenchViewModel(_workspace, _dialogs, session);
        _opened.Add(model);
        host.Content = new ConfigurationInlineEditorView(model);
        SelectKind(kind);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _shell.RefreshEngineeringSetupAsync();
        await LoadKindAsync(ProjectWorkspaceService.TestConfigurationKind);
        await LoadKindAsync(ProjectWorkspaceService.BenchConfigurationKind);
        await LoadInspectionAsync();
    }

    private async Task LoadKindAsync(string kind)
    {
        var current = _workspace.Current;
        if (current is null) return;
        var host = HostFor(kind);
        if (host.Content is ConfigurationInlineEditorView) return;
        var asset = current.Definition.Assets.FirstOrDefault(x => x.Kind == kind);
        if (asset is null)
        {
            host.Content = new TextBlock
            {
                Text = "当前工程尚未创建此配置资源，可使用上方按钮创建正式资产。",
                Foreground = System.Windows.Media.Brushes.SlateGray,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
            return;
        }
        try
        {
            var session = await _workspace.OpenConfigurationAsync(asset.Id);
            if (!ReferenceEquals(current, _workspace.Current)) return;
            // The initial background loading of all three configuration assets
            // must never force navigation away from the user's active subtab.
            var previousTabIndex = ConfigTabs.SelectedIndex;
            ShowSession(kind, session);
            ConfigTabs.SelectedIndex = previousTabIndex;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            UiExceptionReporter.Record("Project configuration / " + kind, error);
            host.Content = new TextBlock
            {
                Text = "BLOCKED_RESOURCE: " + error.Message,
                Foreground = System.Windows.Media.Brushes.Firebrick,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    public bool HasUnsavedDrafts => _opened.Any(x => x.IsDirty) || _inspectionEditor?.IsDirty == true;
    public bool IsBusy => _opened.Any(x => x.IsBusy) || _inspectionEditor?.IsBusy == true;

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy) return false;
        foreach (var editor in _opened)
            if (!await editor.SaveForCloseAsync()) return false;
        if (_inspectionEditor is not null && !await _inspectionEditor.SaveForCloseAsync())
            return false;
        return !HasUnsavedDrafts;
    }

    public bool ConfirmClose() => _opened.All(x => x.ConfirmClose()) &&
        (_inspectionEditor?.ConfirmClose() ?? true);
}
