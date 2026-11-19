using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

public partial class ProjectOverviewView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    public ProjectOverviewView(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new ProjectOverviewInspector(viewModel);
    }
}
