using System.Windows;
using AutoTest.Desktop.Core;
using Prism.DryIoc;
using Prism.Ioc;

namespace AutoTest.Desktop;

public partial class App : PrismApplication
{
    protected override void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<UiStore>();
        registry.Register<MainWindow>();
    }

    protected override Window CreateShell() => Container.Resolve<MainWindow>();
}
