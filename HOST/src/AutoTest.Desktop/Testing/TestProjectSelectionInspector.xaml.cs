using System.Windows.Controls;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectSelectionInspector : UserControl
{
    public TestProjectSelectionInspector(TestProjectWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
