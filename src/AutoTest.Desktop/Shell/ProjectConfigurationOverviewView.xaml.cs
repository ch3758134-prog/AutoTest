using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Configuration;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One actual project configuration surface. Each populated tab opens a canonical
/// ConfigurationWorkbenchViewModel (same save/validation/Revision), never HTML JSON.
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var snapshot = _workspace.Current;
        if (snapshot is null) return;
        async Task Load(string kind, ContentControl host)
        {
            var asset = snapshot.Definition.Assets.FirstOrDefault(x => x.Kind == kind);
            if (asset is null)
            {
                host.Content = new TextBlock
                {
                    Text = "当前工程尚未建立此配置资源。可使用上方按钮创建正式资产。",
                    Foreground = System.Windows.Media.Brushes.SlateGray,
                    Margin = new Thickness(16)
                };
                return;
            }
            try
            {
                var session = await _workspace.OpenConfigurationAsync(asset.Id);
                if (!ReferenceEquals(snapshot, _workspace.Current)) return;
                var editor = new ConfigurationWorkbenchViewModel(_workspace, _dialogs, session);
                _opened.Add(editor);
                host.Content = new ConfigurationInlineEditorView(editor);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or InvalidOperationException)
            {
                UiExceptionReporter.Record("Project config / " + kind, error);
                host.Content = new TextBlock
                {
                    Text = "BLOCKED_RESOURCE: " + error.Message,
                    Foreground = System.Windows.Media.Brushes.Firebrick,
                    Margin = new Thickness(16),
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }
        await Load(ProjectWorkspaceService.TestConfigurationKind, DutFormHost);
        await Load(ProjectWorkspaceService.ControllerKind, InputFormHost);
        await Load(ProjectWorkspaceService.BenchConfigurationKind, BenchFormHost);
    }
    public bool ConfirmClose() => _opened.All(x => x.ConfirmClose());
}
