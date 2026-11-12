using System.Windows;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Dbc;

public sealed class DbcWorkbenchWindowService(
    ProjectWorkspaceService workspace, DbcEditorDialogs dialogs)
{
    public void Show(DbcEditorSession opened)
    {
        var viewModel = new DbcWorkbenchViewModel(workspace, dialogs, opened);
        var window = new DbcWorkbenchWindow(viewModel);
        if (Application.Current.MainWindow is { IsLoaded: true } main) window.Owner = main;
        window.Show();
        window.Activate();
    }
}
