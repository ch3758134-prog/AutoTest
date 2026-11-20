using System.Windows;
namespace AutoTest.UI;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var shell = new MainWindow();
        MainWindow = shell;
        shell.Show();
    }
}
