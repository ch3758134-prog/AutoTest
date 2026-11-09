using System.Windows;
using AutoTest.Desktop.Bootstrap;
using AutoTest.Desktop.Shell;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Navigation.Regions;

namespace AutoTest.Desktop;

public partial class App : PrismApplication
{
    protected override void RegisterTypes(IContainerRegistry containerRegistry) =>
        PrismRegistration.RegisterTypes(containerRegistry);

    protected override Window CreateShell()
    {
        PrismRegistration.AttachRegions(Container.Resolve<IRegionManager>());
        return Container.Resolve<MainWindow>();
    }
}
