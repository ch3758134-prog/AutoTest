using System.Windows.Controls;
using System.Windows;
using Microsoft.Win32;

namespace AutoTest.Desktop.Tools.Dbc;

public partial class DbcToolView : UserControl
{
    public const string NavigationKey = "DbcTool";

    private void BrowseDbc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 DBC 文件",
            Filter = "DBC 数据库 (*.dbc)|*.dbc|全部文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            DbcFilePathBox.Text = dialog.FileName;
    }
    public DbcToolView(DbcToolViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
