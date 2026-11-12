using Microsoft.Win32;
using System.Windows;

namespace AutoTest.Desktop.Projects;

/// <summary>Native Windows dialogs only; no project persistence here.</summary>
public sealed class ProjectFileDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? PickProjectManifest()
    {
        var picker = new OpenFileDialog
        {
            Title = "打开 AutoTest 工程",
            Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json|JSON 文件 (*.json)|*.json",
            InitialDirectory = AppContext.BaseDirectory,
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(Owner) == true ? picker.FileName : null;
    }

    public string? PickDbcFile(string? projectDirectory)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入 DBC 文件到当前工程",
            Filter = "DBC 数据库 (*.dbc)|*.dbc",
            InitialDirectory = Directory.Exists(projectDirectory) ? projectDirectory! : AppContext.BaseDirectory,
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(Owner) == true ? picker.FileName : null;
    }

    public string? AskResourceName(string title, string initialName)
    {
        var dialog = new ResourceNameDialog(title, initialName);
        if (Owner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.ResourceName : null;
    }

    public bool ConfirmRemoveReference(string name)
    {
        var result = MessageBox.Show(Owner,
            "要从工程中移除资源“" + name + "”的引用吗？\n\n" +
            "此操作只修改工程清单，资源文件保留在磁盘上，不会被删除。",
            "移除资源引用", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
