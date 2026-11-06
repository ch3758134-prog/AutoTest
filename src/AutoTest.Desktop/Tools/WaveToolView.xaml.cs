using System.Windows.Controls;

namespace AutoTest.Desktop.Tools;

/// <summary>Self-contained Wave tool surface; Shell insertion is G4-INTEGRATION owned.</summary>
public partial class WaveToolView : UserControl
{
    public WaveToolView()
    {
        InitializeComponent();
        DataContext = new WaveToolViewModel();
    }

    public WaveToolViewModel Model => (WaveToolViewModel)DataContext;
}
