using System.ComponentModel;
using System.Windows;

namespace AutoTest.DeviceLab;

public partial class DeviceLabWindow : Window
{
    private readonly DeviceLabViewModel _vm;
    private bool _shutdownStarted;
    private bool _shutdownDone;
    public DeviceLabWindow()
    {
        InitializeComponent();
        _vm = new DeviceLabViewModel();
        DataContext = _vm;
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        IsEnabled = false;
        try { await _vm.DisposeAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "关闭硬件会话失败；请断开 USB 并保留日志。\n" + ex,
                "DEVICE 关闭故障", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _shutdownDone = true;
            Close();
        }
    }
}
