using System.Windows.Controls;
using AutoTest.Contracts;
using AutoTest.Devices.Usb;
using AutoTest.Devices.Diagnostics;

namespace AutoTest.DeviceLab;

/// <summary>
/// In-process product Station/CAN workbench. A single DeviceLabViewModel owns
/// the session for its station and CAN tabs; parent Shell disposes on close,
/// not when user changes product documents.
/// </summary>
public partial class DeviceLabEmbeddedView : UserControl, IAsyncDisposable
{
    private readonly DeviceLabViewModel _model;
    private readonly HardwareBenchController _controller = new();
    private readonly CanIdMonitor _canIds;
    public HardwareBenchController Controller => _controller;
    public DeviceLabViewModel ViewModel => _model;
    public DeviceLabEmbeddedView()
    {
        InitializeComponent();
        _model=new DeviceLabViewModel(new HardwareDeviceLabBackend(_controller));
        _canIds = new CanIdMonitor(Dispatcher);
        R1CanIdsGrid.ItemsSource = _canIds.Rows;
        _controller.CanReceived += OnCanReceived;
        _controller.ConnectionChanged += OnConnectionChanged;
        DataContext=_model;
    }
    public void ShowStation() => DevicePages.SelectedIndex=0;
    public void ShowCan() => DevicePages.SelectedIndex=1;

    private void OnCanReceived(CanObservation observation) => _canIds.Enqueue(observation);

    private void OnConnectionChanged(UsbSessionState state)
    {
        if (state == UsbSessionState.Connected) return;
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(_canIds.Clear));
    }

    public async ValueTask DisposeAsync()
    {
        _controller.CanReceived -= OnCanReceived;
        _controller.ConnectionChanged -= OnConnectionChanged;
        _canIds.Dispose();
        await _model.DisposeAsync();
    }
}
