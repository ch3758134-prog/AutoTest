using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using Microsoft.Win32;

namespace AutoTest.FlashDiag;

/// <summary>
/// Standalone physical diagnostic window. Does not reuse the formal R1 Shell,
/// and does not open a second WinUSB session. Physical ECU writes are not
/// implemented; this tool provides a reproducible USB/CAN1/UDS diagnosis path.
/// </summary>
public partial class MainWindow : Window
{
    private readonly HardwareBenchController _device = new();
    private readonly ObservableCollection<string> _events = new();
    private bool _closing;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Logs.ItemsSource = _events;
        FlashPanel.AttachPhysicalLink(() => _device);
        UdsPanel.DataContext = new AutoTest.Desktop.Tools.Uds.UdsToolViewModel();
        if (UdsPanel.DataContext is AutoTest.Desktop.Tools.Uds.UdsToolViewModel uds)
        {
            uds.AttachPhysicalLink(() => _device,
                details => MessageBox.Show(this, details + "\n\n确认本次实际只读 UDS 请求？",
                    "真实 CAN1 / UDS 请求确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes);
        }
        _device.ConnectionChanged += OnConnection;
        _device.UsbTrace += OnUsb;
        _device.NativeOperation += OnNative;
        _device.CanTrace += OnCan;
        RefreshDevices();
    }

    private void Append(string type, string detail)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Append(type, detail));
            return;
        }
        if (_closing) return;
        _events.Add($"{DateTime.Now:HH:mm:ss.fff} [{type}] {detail}");
        if (_events.Count > 1000) _events.RemoveAt(0);
        Logs.ScrollIntoView(_events[^1]);
    }

    private void RefreshDevices()
    {
        try
        {
            var previous = (Devices.SelectedItem as WinUsbCandidate)?.DeviceInstanceId;
            var items = _device.Enumerate();
            Devices.ItemsSource = items;
            Devices.SelectedItem = items.FirstOrDefault(x => x.DeviceInstanceId == previous) ??
                (items.Count == 1 ? items[0] : null);
            Append("USB", $"发现 {items.Count} 台匹配主控，请选择目标设备。");
        }
        catch (Exception ex) { Append("ERROR", "设备查找失败：" + ex.Message); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Devices.SelectedItem is not WinUsbCandidate candidate)
        {
            StatusLine.Text = "未选择主控设备；无法连接。";
            return;
        }
        _busy = true;
        ConnectButton.IsEnabled = false;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "AutoTest",
                "FlashDiag", "Sessions");
            Directory.CreateDirectory(root);
            var traceFile = Path.Combine(root, "usb-can-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" +
                Guid.NewGuid().ToString("N") + ".jsonl");
            Append("CONNECT", candidate.DisplayIdentity);
            await _device.ConnectAsync(candidate.DeviceInstanceId, traceFile);
            TracePathLine.Text = "USB/CAN JSONL：" + (_device.TraceFile ?? traceFile);
            StatusLine.Text = $"已连接 · USB Generation {_device.Generation} · 固件协议验证：{_device.ProtocolVersionVerified}";
            DisconnectButton.IsEnabled = true;
            Append("CONNECTED", StatusLine.Text);
        }
        catch (Exception ex)
        {
            StatusLine.Text = "连接失败：" + ex.Message;
            Append("CONNECT_FAIL", ex.ToString());
            ConnectButton.IsEnabled = true;
        }
        finally { _busy = false; }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        DisconnectButton.IsEnabled = false;
        try
        {
            await _device.DisconnectAsync();
            StatusLine.Text = "主控已断开。";
            TracePathLine.Text = "最近会话 JSONL：" + (_device.TraceFile ?? "无");
            Append("DISCONNECT", "设备资源已释放。");
            ConnectButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusLine.Text = "断开失败：" + ex.Message;
            Append("DISCONNECT_FAIL", ex.ToString());
        }
        finally { _busy = false; }
    }

    private void OnConnection(UsbSessionState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            Append("SESSION", state + " / generation=" + _device.Generation);
            if (state == UsbSessionState.Disconnected)
            {
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                StatusLine.Text = "设备连接已结束，请检查 USB 和现场接线。";
            }
        });
    }

    private void OnUsb(UsbTraceRecord item) =>
        Append("USB " + item.Kind,
            $"gen={item.Generation} seq={item.Sequence} {item.Detail} RAW={item.RawHex}");
    private void OnNative(WinUsbOperationEvent item) =>
        Append("NATIVE " + item.Outcome, item.Stage + " | " + item.Detail +
            " | Win32=" + item.NativeErrorCode);
    private void OnCan(HardwareCanTrace item) =>
        Append("CAN", item.Detail + " | USB=" + item.RawUsbPayloadHex);

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, _events)); }
        catch (Exception ex) { Append("ERROR", "无法复制日志：" + ex.Message); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Flash 诊断包文本 (*.txt)|*.txt",
                FileName = "AutoTest-FlashDiag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"
            };
            if (dialog.ShowDialog(this) != true) return;
            var source = _device.TraceFile;
            var header = "AutoTest FlashDiag (real physical evidence only)" +
                Environment.NewLine + "UTC: " + DateTimeOffset.UtcNow.ToString("O") +
                Environment.NewLine + "USB Generation: " + _device.Generation +
                Environment.NewLine + "USB State: " + _device.State +
                Environment.NewLine + "Firmware protocol verified: " + _device.ProtocolVersionVerified +
                Environment.NewLine + "USB/CAN JSONL: " + (source ?? "NO TRACE") +
                Environment.NewLine + "CAN Published: " + _device.CanPublished +
                Environment.NewLine + "CAN Dropped: " + _device.CanDropped +
                Environment.NewLine + "CAN Malformed: " + _device.CanMalformed +
                Environment.NewLine + "Hub Dropped: " + _device.HubDropped +
                Environment.NewLine + "Trace Dropped: " + _device.TraceDropped +
                Environment.NewLine + "Trace Failure: " + (_device.TraceError ?? "none") +
                Environment.NewLine + Environment.NewLine;
            File.WriteAllText(dialog.FileName, header +
                string.Join(Environment.NewLine, _events), new UTF8Encoding(false));
            Append("EXPORT", dialog.FileName);
        }
        catch (Exception ex) { Append("ERROR", "日志导出失败：" + ex.Message); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try
        {
            await _device.DisposeAsync();
            _device.ConnectionChanged -= OnConnection;
            _device.UsbTrace -= OnUsb;
            _device.NativeOperation -= OnNative;
            _device.CanTrace -= OnCan;
            Close();
        }
        catch (Exception ex)
        {
            _closing = false;
            IsEnabled = true;
            MessageBox.Show(this, ex.ToString(), "无法安全关闭设备会话");
        }
    }
}
