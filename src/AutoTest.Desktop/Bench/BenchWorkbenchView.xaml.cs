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
    public event EventHandler? OpenDbcRequested;
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

    private async void ReloadKeyActions_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        if (vm.KeyActions.HasUnsavedChanges)
        {
            if (MessageBox.Show(
                "当前按键动作存在未保存修改。放弃这些修改并重新加载当前工程的按键动作？",
                "确认放弃按键草稿", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await vm.KeyActions.DiscardDraftAndReloadAsync();
        }
        else await vm.KeyActions.LoadAsync();
    }

    private async void ReloadCanMessages_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        if (vm.CanMessages.Dirty)
        {
            if (MessageBox.Show(
                "CAN 发送表存在未保存修改。放弃这些修改并重新加载当前工程的 CAN 报文？",
                "确认放弃 CAN 草稿", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await vm.CanMessages.DiscardDraftAndReloadAsync();
        }
        else await vm.CanMessages.LoadAsync();
    }

    private void OpenDbc_Click(object sender, RoutedEventArgs e) =>
        OpenDbcRequested?.Invoke(this, EventArgs.Empty);

    private void OpenCan_Click(object sender, RoutedEventArgs e) =>
        OpenCanRequested?.Invoke(this, EventArgs.Empty);

    private void OpenStation_Click(object sender, RoutedEventArgs e) =>
        OpenStationRequested?.Invoke(this, EventArgs.Empty);
}
