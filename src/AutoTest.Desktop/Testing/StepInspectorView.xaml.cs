using System.Windows.Controls;
namespace AutoTest.Desktop.Testing;
public partial class StepInspectorView : UserControl { public StepInspectorView(AutoTest.Desktop.Shell.MainWindowViewModel vm) { InitializeComponent(); DataContext = vm; } }
