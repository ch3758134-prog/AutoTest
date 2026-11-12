using AutoTest.Desktop.Shell;
using Prism.Ioc;

namespace AutoTest.Desktop.Bootstrap;

public static class PrismRegistration
{
    public static void RegisterTypes(IContainerRegistry registry)
    {
        registry.Register<MainWindow>();
    }
}
