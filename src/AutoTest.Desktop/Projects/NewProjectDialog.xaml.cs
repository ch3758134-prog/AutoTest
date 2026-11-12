using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AutoTest.Desktop.Projects;

public partial class NewProjectDialog : Window
{
    private readonly NewProjectDialogViewModel _viewModel;

    public NewProjectDialog(ProjectWorkspaceService workspace)
    {
        InitializeComponent();
        _viewModel = new NewProjectDialogViewModel(workspace, PickParentDirectory);
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
        return picker.ShowDialog(this) == true ? picker.FolderName : null;
    }
}
