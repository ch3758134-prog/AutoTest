using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Desktop.Shell;
using Prism.Ioc;
using Prism.Navigation.Regions;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bootstrap;

public static class PrismRegistration
{
    // The one and only composition created on application startup is mock-only.
    public static void RegisterTypes(IContainerRegistry registry)
    {
        var mock = ApplicationComposition.RegisterMock();
        registry.RegisterInstance<IApplicationCommandGateway>(mock.Gateway);
        registry.Register<MainWindowViewModel>();
        registry.Register<MainWindow>();
        registry.Register<V0RegionPlaceholder>();
        // Shell namespace is not the default Views/ViewModels convention.
        ViewModelLocationProvider.Register<MainWindow, MainWindowViewModel>();
    }

    public static void AttachRegions(IRegionManager regionManager) =>
        regionManager.RegisterViewWithRegion("V0SidebarRegion", typeof(V0RegionPlaceholder));
}
