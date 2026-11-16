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
    private readonly ProjectWorkspaceService _workspace;
    private readonly ProjectFileDialogService _dialogs;
    private readonly List<ConfigurationWorkbenchViewModel> _opened = new();

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
        _workspace = workspace;
        _dialogs = dialogs;
        InspectorHost.Content = new ProjectConfigurationOverviewInspector(shell);
        Loaded += OnLoaded;
    }

    private static bool IsInlineKind(string kind) =>
        kind is ProjectWorkspaceService.TestConfigurationKind or
            ProjectWorkspaceService.ControllerKind or
            ProjectWorkspaceService.BenchConfigurationKind;

    private ContentControl HostFor(string kind) => kind switch
    {
        ProjectWorkspaceService.TestConfigurationKind => DutFormHost,
        ProjectWorkspaceService.ControllerKind => InputFormHost,
        ProjectWorkspaceService.BenchConfigurationKind => BenchFormHost,
        _ => throw new ArgumentException("该资源属于独立专业工具，不是自动测试工程配置。")
    };

    public void SelectKind(string kind)
    {
        ConfigTabs.SelectedIndex = kind switch
        {
            ProjectWorkspaceService.TestConfigurationKind => 0,
            ProjectWorkspaceService.ControllerKind => 1,
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
        await LoadKindAsync(ProjectWorkspaceService.TestConfigurationKind);
        await LoadKindAsync(ProjectWorkspaceService.ControllerKind);
        await LoadKindAsync(ProjectWorkspaceService.BenchConfigurationKind);
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

    public bool ConfirmClose() => _opened.All(x => x.ConfirmClose());
}
