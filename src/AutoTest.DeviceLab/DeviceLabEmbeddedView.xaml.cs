using System.Windows.Controls;
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
    public HardwareBenchController Controller => _controller;
    public DeviceLabViewModel ViewModel => _model;
    public DeviceLabEmbeddedView()
    {
        InitializeComponent();
        _model=new DeviceLabViewModel(new HardwareDeviceLabBackend(_controller));
        DataContext=_model;
    }
    public void ShowStation() => DevicePages.SelectedIndex=0;
    public void ShowCan() => DevicePages.SelectedIndex=1;
    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
