using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Dbc;
using AutoTest.Desktop.Testing;
using AutoTest.Desktop.Shell;
using Prism.Ioc;

namespace AutoTest.Desktop.Bootstrap;

public static class PrismRegistration
{
    public static void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<RecentProjectStore>();
        registry.RegisterSingleton<ProjectWorkspaceService>();
        registry.RegisterSingleton<NewProjectDialogService>();
        registry.RegisterSingleton<ProjectFileDialogService>();
        registry.RegisterSingleton<DbcEditorDialogs>();
        registry.RegisterSingleton<DbcWorkbenchWindowService>();
        registry.RegisterSingleton<TestEditorDialogs>();
        registry.RegisterSingleton<TestEditorWindowService>();
        registry.Register<MainWindowViewModel>();
        registry.Register<MainWindow>();
    }
}
