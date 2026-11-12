using System.Windows.Controls;
using AutoTest.Desktop.Shell;
namespace AutoTest.Desktop.Hardware;
public partial class CanMonitorView : UserControl
{
    public CanMonitorView(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
