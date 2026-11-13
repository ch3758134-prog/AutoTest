using System.Windows.Controls;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectWorkbenchView : UserControl
{
    public TestProjectWorkbenchView(TestProjectWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
