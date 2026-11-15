using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectWorkbenchView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private void PackagesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (DataContext is TestProjectWorkbenchViewModel vm)
            vm.OpenSelectedPackage();
    }

    public TestProjectWorkbenchView(TestProjectWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new TestProjectSelectionInspector(viewModel);
    }
}
