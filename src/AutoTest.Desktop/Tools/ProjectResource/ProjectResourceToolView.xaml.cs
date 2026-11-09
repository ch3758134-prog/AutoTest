using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Desktop.Tools.ProjectResource;

public partial class ProjectResourceToolView : UserControl
{

    private void BrowseProject_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json",
            Title = "打开本地 AutoTest 工程",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ProjectPathBox.Text = dialog.FileName;
        if (DataContext is ProjectResourceToolViewModel model)
        {
            model.ProjectFilePath = dialog.FileName;
            model.OpenProjectCommand.Execute(null);
        }
    }

    private void ChooseNewProject_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json",
            Title = "指定新工程文件位置",
            DefaultExt = ".json",
            FileName = "NewECU.autotest-project.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ProjectPathBox.Text = dialog.FileName;
        if (DataContext is ProjectResourceToolViewModel model)
            model.ProjectFilePath = dialog.FileName;
    }

    private void BrowseImport_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "资源文件 (*.dbc;*.json;*.hex;*.bin;*.s19)|*.dbc;*.json;*.hex;*.bin;*.s19|所有文件 (*.*)|*.*",
            Title = "选择实际资源文件",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ImportPathBox.Text = dialog.FileName;
        if (DataContext is ProjectResourceToolViewModel model)
        {
            model.ImportFilePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(model.ResourceName))
                model.ResourceName = Path.GetFileName(dialog.FileName);
        }
    }

    public ProjectResourceToolView()
    {
        InitializeComponent();
        DataContext = new ProjectResourceToolViewModel();
    }
}
