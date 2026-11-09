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
        // --lab=<name> bypasses the R1 host entirely: fast functional testing
        // is independent of the final product layout and theme.
        var requestedLab = SpecialistLabLauncher.RequestedLab(Environment.GetCommandLineArgs());
        if (requestedLab is not null)
            return SpecialistLabLauncher.Open(requestedLab);

        PrismRegistration.AttachRegions(Container.Resolve<IRegionManager>());
        return Container.Resolve<MainWindow>();
    }
}
