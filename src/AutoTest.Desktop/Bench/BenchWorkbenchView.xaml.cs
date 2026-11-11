using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Bench;

/// <summary>
/// Real hardware bench view. The shared Station handles the only WinUSB
/// connection; opening this view never silently creates another session.
/// </summary>
public partial class BenchWorkbenchView : UserControl
{
    public event EventHandler? OpenStationRequested;
    public event EventHandler? OpenCanRequested;
    public BenchWorkbenchView(BenchWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        vm.CanMonitor.Start();
        await vm.InitializeAsync();
        await vm.RefreshConnectionAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BenchWorkbenchViewModel vm) vm.CanMonitor.Stop();
    }

    private void OpenCan_Click(object sender, RoutedEventArgs e) =>
        OpenCanRequested?.Invoke(this, EventArgs.Empty);

    private void OpenStation_Click(object sender, RoutedEventArgs e) =>
        OpenStationRequested?.Invoke(this, EventArgs.Empty);
}
