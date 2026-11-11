using AutoTest.Application.Composition;
using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Desktop.Shell;
using Prism.Ioc;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bootstrap;

/// <summary>
/// The formal product has ONE Shell and no registered legacy P01-P13 regions.
/// Tools and document editors are created only when opened by the IDE host.
/// The existing project command gateway remains until the project persistence
/// service is migrated; it does NOT create a second operator UI.
/// </summary>
public static class PrismRegistration
{
    public static void RegisterTypes(IContainerRegistry registry)
    {
        var legacyProjectCommands = ApplicationComposition.RegisterMock();
        registry.RegisterInstance<IApplicationCommandGateway>(legacyProjectCommands.Gateway);
        registry.RegisterSingleton<V1Workspace>();
        registry.RegisterSingleton<G4IntegrationHost>();
        registry.RegisterSingleton<MainWindowViewModel>();
        registry.Register<MainWindow>();
        ViewModelLocationProvider.Register<MainWindow, MainWindowViewModel>();
    }
}
