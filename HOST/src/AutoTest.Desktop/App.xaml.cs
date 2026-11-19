using System.Windows;
using AutoTest.Desktop.Shell;
using Prism.DryIoc;
using Prism.Ioc;

namespace AutoTest.Desktop;

/// <summary>Fresh Prism WPF app; intentionally does not register hardware or execution services.</summary>
public partial class App : PrismApplication
{
    protected override void RegisterTypes(IContainerRegistry registry)
    {
        registry.Register<MainWindow>();
    }

    protected override Window CreateShell() => Container.Resolve<MainWindow>();
}
