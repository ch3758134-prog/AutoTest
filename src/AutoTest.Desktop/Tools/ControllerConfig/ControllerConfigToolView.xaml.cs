using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.ControllerConfig;

public partial class ControllerConfigToolView : UserControl
{
    public const string NavigationKey = "ControllerConfigTool";
    public ControllerConfigToolView(ControllerConfigToolViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
