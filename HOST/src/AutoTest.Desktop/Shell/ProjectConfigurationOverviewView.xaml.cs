using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only engineering resource review. It has no scanner, fixture or
/// production-recipe editor; actions navigate to the authoritative editors.
/// </summary>
public partial class ProjectConfigurationOverviewView : UserControl
{
    private readonly MainWindowViewModel _shell;
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public ProjectConfigurationOverviewView(MainWindowViewModel shell,
        ProjectWorkspaceService workspace)
    {
        InitializeComponent();
        _shell = shell;
        DataContext = shell;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _shell.RefreshEngineeringSetupAsync();
    }

    private async void LocateDependency_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: ResourceDependencyGroup group })
            await _shell.OpenDependencyGroupAsync(group);
    }

    public bool HasUnsavedDrafts => false;
    public bool IsBusy => false;
    public Task<bool> SaveForCloseAsync() => Task.FromResult(true);
    public bool ConfirmClose() => true;
}
