using System.Windows.Controls;

namespace AutoTest.Desktop.Shell.R1;

public partial class R1WorkspaceView : UserControl
{
    public R1WorkspaceView(R1WorkspaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
