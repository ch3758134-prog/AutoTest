using System.Windows;

namespace AutoTest.Desktop.Shell;

public partial class SerialScannerWindow : Window
{
    public SerialScannerWindow(SerialScannerWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        DataContext = viewModel;
    }
}
