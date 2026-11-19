using System.IO;
using System.Windows;
using AutoTest.Desktop.Bootstrap;
using AutoTest.Desktop.Shell;
using Prism.DryIoc;
using Prism.Ioc;

namespace AutoTest.G4Workbench;

/// <summary>
/// G4 starts the ONE production WPF IDE, not a second window that translates
/// DemoStep/DemoPhase back into TEST. Prism resolves the existing real services.
/// </summary>
public partial class App : PrismApplication
{
    protected override void RegisterTypes(IContainerRegistry registry) =>
        PrismRegistration.RegisterTypes(registry);

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        var arg = Array.FindIndex(e.Args, x =>
            string.Equals(x, "--verify-workspace", StringComparison.OrdinalIgnoreCase));
        if (arg >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var report = arg + 1 < e.Args.Length && !e.Args[arg + 1].StartsWith("--", StringComparison.Ordinal)
                ? e.Args[arg + 1]
                : Path.Combine(AppContext.BaseDirectory, "G4_WORKSPACE_VERIFY.txt");
            try
            {
                var result = await G4WorkspaceSmoke.RunAsync();
                File.WriteAllText(report, result);
                Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "G4_WORKSPACE_VERIFY_FAIL" + Environment.NewLine + error);
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
    }
}
