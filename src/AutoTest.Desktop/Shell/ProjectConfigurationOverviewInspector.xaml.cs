using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;
public partial class ProjectConfigurationOverviewInspector : UserControl
{
    public ProjectConfigurationOverviewInspector(MainWindowViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
