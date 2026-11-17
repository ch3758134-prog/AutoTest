using System.IO;
using System.Windows;
using AutoTest.UiPreview;

namespace AutoTest.G4Workbench;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--verify-workspace", StringComparer.OrdinalIgnoreCase))
        {
            var arg = Array.IndexOf(e.Args, "--verify-workspace");
            var report = arg >= 0 && arg + 1 < e.Args.Length && !e.Args[arg + 1].StartsWith("--")
                ? e.Args[arg + 1] : Path.Combine(AppContext.BaseDirectory, "G4_WORKSPACE_VERIFY.txt");
            try
            {
                var outcome = await G4WorkspaceSmoke.RunAsync();
                File.WriteAllText(report, outcome);
                Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "G4_WORKSPACE_VERIFY_FAIL" + Environment.NewLine + error);
                Shutdown(1);
            }
            return;
        }

        MainWindow = new MainWindow(new RealEditorWorkspaceGateway());
        MainWindow.Show();
    }
}
