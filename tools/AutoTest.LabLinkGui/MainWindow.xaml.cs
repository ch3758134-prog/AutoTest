using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using AutoTest.LabLinkGui.LabLink;

namespace AutoTest.LabLinkGui;

public partial class MainWindow : Window
{
    private readonly HardwareBenchController _bench = new();
    private readonly DeviceLabLinkPort _port;
    private readonly LabLinkViewModel _viewModel;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        _port = new DeviceLabLinkPort(_bench);
        _viewModel = new LabLinkViewModel(_port);
        LabLink.DataContext = _viewModel;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        try { await _bench.DisposeAsync(); _viewModel.Dispose(); Close(); }
        catch (Exception ex) { _closing = false; MessageBox.Show(this, ex.Message, "设备资源释放失败"); }
    }
}

internal sealed class DeviceLabLinkPort : ILabLinkUiPort
{
    private readonly HardwareBenchController _bench;
    private LabBoardIdentity? _connected;
    public DeviceLabLinkPort(HardwareBenchController bench)
    {
        _bench = bench;
        _bench.ConnectionChanged += OnConnectionChanged;
        _bench.UsbTrace += OnUsbTrace;
        _bench.NativeOperation += OnNative;
        _bench.CanTrace += OnCanTrace;
    }
    public event EventHandler<LabLinkUiEvent>? Activity;
    public bool IsConnected => _bench.State == UsbSessionState.Connected;
    public LabBoardIdentity? ConnectedBoard => _connected;
    public Task<IReadOnlyList<LabBoardIdentity>> DiscoverMatchingBoardsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boards = _bench.Enumerate().Select(c => new LabBoardIdentity(c.DisplayIdentity, c.DevicePath, "VID_1183&PID_5850", ExtractSerial(c.DeviceInstanceId))).ToArray();
        Publish(LabLinkActivityKind.Discovery, $"发现 {boards.Length} 台符合主控 VID/PID 与接口 GUID 的设备");
        return Task.FromResult<IReadOnlyList<LabBoardIdentity>>(boards);
    }
    public async Task ConnectAsync(LabBoardIdentity board, CancellationToken cancellationToken)
    {
        var candidate = _bench.Enumerate().SingleOrDefault(c => string.Equals(c.DevicePath, board.DevicePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("所选设备已不在严格匹配的枚举列表中。");
        _connected = board;
        await _bench.ConnectAsync(candidate.DeviceInstanceId, TracePath(), cancellationToken);
        Publish(LabLinkActivityKind.Status, "设备会话已连接", board.DevicePath);
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _bench.DisconnectAsync();
        _connected = null;
        Publish(LabLinkActivityKind.Status, "设备会话已断开");
    }
    private static string TracePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AutoTest", "HardwareTrace", $"lablink-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
    private static string? ExtractSerial(string id) { var p=id.Split('\\'); return p.Length > 1 ? p[^1] : null; }
    private void OnConnectionChanged(UsbSessionState s) { if (s == UsbSessionState.Disconnected) _connected=null; Publish(LabLinkActivityKind.Status, s.ToString()); }
    private void OnNative(WinUsbOperationEvent e) => Publish(e.Outcome == "SUCCESS" ? LabLinkActivityKind.Success : LabLinkActivityKind.Failure, e.Detail ?? e.Outcome, e.DeviceInstanceId, e.Stage, e.NativeErrorCode, e.Kind, null, e.DevicePath);
    private void OnUsbTrace(UsbTraceRecord e) => Publish(e.Kind.Contains("ERROR") || e.Kind.Contains("FAULT") ? LabLinkActivityKind.Error : e.Kind.StartsWith("TX") ? LabLinkActivityKind.Request : e.Kind.StartsWith("RX") ? LabLinkActivityKind.Response : LabLinkActivityKind.Trace, e.Detail ?? e.Kind, _connected?.DevicePath, e.Kind, null, $"UTC={e.Utc:O}; ticks={e.MonotonicTicks}; generation={e.Generation}", e.Sequence?.ToString(), e.RawHex);
    private void OnCanTrace(HardwareCanTrace e) => Publish(LabLinkActivityKind.Trace, e.Detail, _connected?.DevicePath, "CAN_RX", null, e.RawUsbPayloadHex);
    private void Publish(LabLinkActivityKind kind, string msg, string? id=null,string? stage=null,int? win32=null,string? trace=null,string? seq=null,string? raw=null) => Activity?.Invoke(this,new LabLinkUiEvent(DateTimeOffset.Now,kind,msg,id,stage,win32,trace,seq,raw));
}


