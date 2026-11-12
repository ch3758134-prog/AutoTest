using System.Windows.Controls;

namespace AutoTest.Desktop.Tools;

/// <summary>Self-contained Wave tool surface; The application integration layer owns Shell registration.</summary>
public partial class WaveToolView : UserControl
{
    public WaveToolView()
    {
        InitializeComponent();
        DataContext = new WaveToolViewModel();
    }

    public WaveToolViewModel Model => (WaveToolViewModel)DataContext;
}
