using AutoTest.Application.Composition;
using AutoTest.Application.V1;
using AutoTest.Application.G4;
using AutoTest.Contracts;
using AutoTest.Desktop.Shell;
using AutoTest.Desktop.Shell.R1;
using AutoTest.Desktop.Project;
using AutoTest.Desktop.Testing;
using Prism.Ioc;
using Prism.Navigation.Regions;
using Prism.Mvvm;
namespace AutoTest.Desktop.Bootstrap;
public static class PrismRegistration
{
 public static void RegisterTypes(IContainerRegistry registry)
 {
  var mock = ApplicationComposition.RegisterMock();
  registry.RegisterInstance<IApplicationCommandGateway>(mock.Gateway);
  registry.RegisterSingleton<V1Workspace>();
  registry.RegisterSingleton<G4IntegrationHost>();
  registry.RegisterSingleton<R1SharedCanSource>();
  registry.RegisterSingleton<MainWindowViewModel>();
  registry.Register<MainWindow>();
  registry.Register<ProjectExplorerView>();
  registry.Register<TestEditorView>();
  registry.Register<StepInspectorView>();
  registry.Register<RunMonitorView>();
  ViewModelLocationProvider.Register<MainWindow, MainWindowViewModel>();
 }
 public static void AttachRegions(IRegionManager manager)
 {
  manager.RegisterViewWithRegion("ProjectExplorerRegion", typeof(ProjectExplorerView));
  manager.RegisterViewWithRegion("TestEditorRegion", typeof(TestEditorView));
  manager.RegisterViewWithRegion("StepInspectorRegion", typeof(StepInspectorView));
  manager.RegisterViewWithRegion("RunMonitorRegion", typeof(RunMonitorView));
 }
}
