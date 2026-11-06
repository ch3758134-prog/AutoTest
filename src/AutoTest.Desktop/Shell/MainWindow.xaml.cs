using System.Windows;
using AutoTest.Desktop.G4;
namespace AutoTest.Desktop.Shell;
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private G4ToolsWindow? _tools;
    private void G4Tools_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null)
        {
            MessageBox.Show("G4 Application 协调服务未注册，禁止执行", "MOCK ONLY"); return;
        }
        if (_tools is { IsVisible: true }) { _tools.Activate(); return; }
        _tools = new G4ToolsWindow(vm) { Owner = this };
        _tools.Show();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
