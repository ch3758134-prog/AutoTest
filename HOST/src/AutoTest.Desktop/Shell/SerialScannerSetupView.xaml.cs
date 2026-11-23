using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

/// <summary>One scanner configuration and real COM capture surface, hosted by
/// the ECU workbench or its dedicated tool window with the same ViewModel.</summary>
public partial class SerialScannerSetupView : UserControl
{
    public SerialScannerSetupView()
    {
        InitializeComponent();
    }
}
