using System.Windows;

namespace AutoTest.Desktop.Shell;

public partial class SerialScannerWindow : Window
{
    public SerialScannerWindow(SerialScannerWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
