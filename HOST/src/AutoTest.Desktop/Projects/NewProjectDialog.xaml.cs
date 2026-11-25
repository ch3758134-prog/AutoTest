using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AutoTest.Desktop.Projects;

public partial class NewProjectDialog : Window
{
    private readonly NewProjectDialogViewModel _viewModel;

    public NewProjectDialog(ProjectWorkspaceService workspace, RecentProjectStore recent)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        _viewModel = new NewProjectDialogViewModel(workspace, PickParentDirectory, recent);
        DataContext = _viewModel;
        _viewModel.CloseRequested += accepted => DialogResult = accepted;
        Loaded += (_, _) => ProjectNameInput.Focus();
        Closing += (_, e) =>
        {
            if (_viewModel.IsCreating) e.Cancel = true;
        };
    }

    private string? PickParentDirectory()
    {
        var picker = new OpenFolderDialog
        {
            Title = "选择 AutoTest 工程保存位置",
            Multiselect = false
        };
        if (Directory.Exists(_viewModel.ParentDirectory))
            picker.InitialDirectory = _viewModel.ParentDirectory;
        if (picker.ShowDialog(this) != true) return null;
        return picker.FolderName;
    }
}
