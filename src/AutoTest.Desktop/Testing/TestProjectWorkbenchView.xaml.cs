using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectWorkbenchView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    public TestProjectWorkbenchView(TestProjectWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new TestProjectSelectionInspector(viewModel);
    }
}
