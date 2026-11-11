using System.Windows;
using AutoTest.Desktop.Bootstrap;
using AutoTest.Desktop.Shell;
using Prism.DryIoc;
using Prism.Ioc;

namespace AutoTest.Desktop;

public partial class App : PrismApplication
{
    protected override void RegisterTypes(IContainerRegistry registry) =>
        PrismRegistration.RegisterTypes(registry);

    protected override Window CreateShell() => Container.Resolve<MainWindow>();
}
