using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Application.Composition;
using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Desktop.G4;
using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests;

public sealed class ModuleToolsWindowCompositionTests
{
    [Fact]
    public void ModuleWindowLoadsEveryToolTabOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var workspace = new V1Workspace();
                using var host = new G4IntegrationHost(workspace,
                    Path.Combine(Path.GetTempPath(), "AutoTest-ModuleTools-Smoke", Guid.NewGuid().ToString("N")));
                var gateway = ApplicationComposition.RegisterMock().Gateway;
                var viewModel = new MainWindowViewModel(gateway, workspace, host);
                var window = new ModuleToolsWindow(viewModel);

                window.Show();
                window.UpdateLayout();
                var tabs = Assert.IsType<TabControl>(((DockPanel)window.Content).Children[1]);
                Assert.Equal(10, tabs.Items.Count);
                Assert.NotNull(window.FindName("ControllerConfigHost"));
                Assert.NotNull(window.FindName("DbcHost"));
                Assert.NotNull(window.FindName("TestHost"));
                Assert.NotNull(window.FindName("ProductionHost"));
                Assert.NotNull(window.FindName("RecordsHost"));
                Assert.NotNull(window.FindName("OperationsHost"));
                Assert.NotNull(window.FindName("UdsHost"));
                Assert.NotNull(window.FindName("FlashHost"));
                Assert.NotNull(window.FindName("WaveHost"));

                window.Close();
                var g4Window = new G4ToolsWindow(viewModel);
                g4Window.Show();
                g4Window.UpdateLayout();
                Assert.Equal("AutoTest G4 · 四专项 Mock 工作台", g4Window.Title);
                g4Window.Close();
                app.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
