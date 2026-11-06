using System.Windows;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.G4;

public partial class G4ToolsWindow : Window
{
    public G4ToolsWindow(MainWindowViewModel main)
    {
        InitializeComponent();
        if (main.G4 is null) throw new InvalidOperationException("G4 services are not registered");
        DataContext = new G4ToolsViewModel(main.G4);
        WavePanel.Model.BindActions(new G4WaveActions(main.G4, WavePanel.Model));
    }
}
