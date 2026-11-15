using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

public partial class ProjectOverviewInspector : UserControl
{
    public ProjectOverviewInspector(MainWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
