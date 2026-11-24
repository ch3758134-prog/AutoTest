using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// The engineering setup hosts the real scanner, native FLASH entry and
/// project-owned fixture procedure. No extra ECU-system or bench config.
/// </summary>
public partial class ProjectConfigurationOverviewView : UserControl
{
    private readonly MainWindowViewModel _shell;
    private readonly ProjectWorkspaceService _workspace;
    private InspectionStationProcedureEditorViewModel? _inspectionEditor;
    private readonly ProjectBarcodeExpectationView _barcodeEditor;

    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public ProjectConfigurationOverviewView(MainWindowViewModel shell,
        ProjectWorkspaceService workspace)
    {
        InitializeComponent();
        DataContext = shell;
        _shell = shell;
        _barcodeEditor = new ProjectBarcodeExpectationView(shell, workspace);
        ScannerSetupHost.Content = _barcodeEditor;
        _workspace = workspace;
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
            "scan" => 1,
            "flash" => 2,
            "station" => 3,
            "check" => 4,
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
            ConfigTabs.SelectedIndex = 3;
            return;
        }
        _inspectionEditor?.Close();
        _inspectionEditor = new InspectionStationProcedureEditorViewModel(_workspace, session);
        InspectionProcedureHost.Content = new InspectionStationProcedureEditorView(_inspectionEditor);
        ConfigTabs.SelectedIndex = 3;
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _shell.RefreshEngineeringSetupAsync();
        await LoadInspectionAsync();
    }

    public bool HasUnsavedDrafts => _barcodeEditor.IsDirty || _inspectionEditor?.IsDirty == true;
    public bool IsBusy => _barcodeEditor.IsBusy || _inspectionEditor?.IsBusy == true;

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy || !await _barcodeEditor.SaveForCloseAsync()) return false;
        if (_inspectionEditor is not null && !await _inspectionEditor.SaveForCloseAsync())
            return false;
        return !HasUnsavedDrafts;
    }

    public bool ConfirmClose() => _inspectionEditor?.ConfirmClose() ?? true;
}
