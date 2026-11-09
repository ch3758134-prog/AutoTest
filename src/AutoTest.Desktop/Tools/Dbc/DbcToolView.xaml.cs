using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Desktop.Tools.Dbc;

public partial class DbcToolView : UserControl
{
    public const string NavigationKey = "DbcTool";
    private void BrowseDbc_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开本地 DBC 文件",
            Filter = "CAN 数据库 (*.dbc)|*.dbc",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        DbcFilePathBox.Text = dialog.FileName;
        if (DataContext is DbcToolViewModel model)
        {
            model.FilePath = dialog.FileName;
            model.OpenCommand.Execute(null);
        }
    }

    private void BrowseProject_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择实际 AutoTest 工程",
            Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        DbcProjectPathBox.Text = dialog.FileName;
        if (DataContext is DbcToolViewModel model)
            model.ProjectFilePath = dialog.FileName;
    }

    public DbcToolView(DbcToolViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
