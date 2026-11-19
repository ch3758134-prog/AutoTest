using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Tools;

public partial class WaveView : UserControl
{
    public WaveView() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;
    private void AddSignal_Click(object sender, RoutedEventArgs e)
    {
        var row = new WaveSignalDraft { Name = $"信号 {State.WaveSignals.Count + 1}" };
        State.WaveSignals.Add(row);
        State.SelectedWave = row;
        SignalsList.ScrollIntoView(row);
    }
    private void RemoveSignal_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedWave is not { } row) return;
        State.WaveSignals.Remove(row);
        State.SelectedWave = State.WaveSignals.FirstOrDefault();
    }
}
