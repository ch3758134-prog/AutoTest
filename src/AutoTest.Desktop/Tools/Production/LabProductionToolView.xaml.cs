using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Tools.Production;

public partial class LabProductionToolView : UserControl
{
    public LabProductionToolView()
    {
        InitializeComponent();
        DataContext = new LabProductionToolViewModel();
    }

    // Integration can inject the shared TEST/RECORD bridge-backed VM instead of the demo VM.
    public LabProductionToolView(LabProductionToolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void BarcodeInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is LabProductionToolViewModel vm) vm.PreviewScan();
    }
}
