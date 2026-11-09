using System.Windows.Controls;
namespace AutoTest.Desktop.Project;
public partial class ProjectExplorerView : UserControl { public ProjectExplorerView(AutoTest.Desktop.Shell.MainWindowViewModel vm) { InitializeComponent(); DataContext = vm; } }
