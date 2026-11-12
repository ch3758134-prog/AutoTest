using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Dbc;
using AutoTest.Desktop.Testing;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Shell;
using Prism.Ioc;

namespace AutoTest.Desktop.Bootstrap;

public static class PrismRegistration
{
    public static void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<HardwareConnectionService>();
        registry.RegisterSingleton<RecentProjectStore>();
        registry.RegisterSingleton<ProjectWorkspaceService>();
        registry.RegisterSingleton<NewProjectDialogService>();
        registry.RegisterSingleton<ProjectFileDialogService>();
        registry.RegisterSingleton<DbcEditorDialogs>();
        registry.RegisterSingleton<TestEditorDialogs>();
        registry.Register<MainWindowViewModel>();
        registry.Register<MainWindow>();
    }
}
