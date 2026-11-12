using System.Windows;

namespace AutoTest.Desktop.Projects;

/// <summary>WPF modal dialog boundary, isolated from filesystem operations.</summary>
public sealed class NewProjectDialogService(ProjectWorkspaceService workspace)
{
    public bool ShowDialog()
    {
        var dialog = new NewProjectDialog(workspace);
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
