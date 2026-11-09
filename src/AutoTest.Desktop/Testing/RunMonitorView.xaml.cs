using System.Windows.Controls;
namespace AutoTest.Desktop.Testing;
public partial class RunMonitorView : UserControl { public RunMonitorView(AutoTest.Desktop.Shell.MainWindowViewModel vm) { InitializeComponent(); DataContext = vm; } }
