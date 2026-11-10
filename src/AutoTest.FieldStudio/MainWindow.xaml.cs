using System.IO;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Scan;
using Microsoft.Win32;

namespace AutoTest.FieldStudio;

public sealed record CanRow(string Time, string Port, string Id, string Length, string Raw, string Quality);
public sealed record LogRow(string Time, string Module, string Message);
public sealed record ScanRow(string Time, string Source, string Barcode, string Result);
public sealed record UdsRow(string Time, string Direction, string Detail);
public sealed record DbcMessageRow(string Id, string Name, int Dlc, string Transmitter, int Signals);
public sealed record FirmwareSegmentRow(string Address, long Bytes, string EndExclusive);
public sealed record SignalOption(DbcFileEntry File, DbcMessage Message, DbcSignal Signal)
{
    public string Label => $"{File.Name} / {Message.Name} / {Signal.Name}  [{Signal.Unit}]";
}
public sealed class DbcFileEntry(DbcWorkbenchDocument document, DbcFileReceipt receipt)
{
    public DbcWorkbenchDocument Document { get; } = document;
    public DbcFileReceipt Receipt { get; set; } = receipt;
    public string Name => System.IO.Path.GetFileName(Receipt.Path);
    public string Display => $"{Name} {(Document.CanEdit ? "✎" : "只读")}";
}
public sealed record WavePoint(DateTimeOffset HostUtc, double Value, long Lost, string Unit);

public partial class MainWindow : Window
{
    private readonly HardwareBenchController _device = new();
    private readonly ObservableCollection<CanRow> _canRows = [];
    private readonly ObservableCollection<LogRow> _logRows = [];
    private readonly ObservableCollection<ScanRow> _scanRows = [];
    private readonly ObservableCollection<UdsRow> _udsRows = [];
    private readonly ObservableCollection<DbcFileEntry> _dbcFiles = [];
    private readonly List<WavePoint> _waveSamples = [];
    private WavePoint[]? _waveReference;
    private readonly ConcurrentQueue<CanObservation> _pendingCan = new();
    private readonly DispatcherTimer _renderClock = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly BarcodeInputRouter _scannerRouter = new();
    private readonly KeyboardWedgeCapture _keyboardScanner;
    private readonly HashSet<string> _seenScans = new(StringComparer.Ordinal);
    private CancellationTokenSource? _rxStop;
    private Task? _rxPump;
    private CancellationTokenSource? _udsStop;
    private Guid _requestId;
    private string? _preparedHex;
    private bool _busy;
    private bool _closed;
    private bool _closing;
    private bool _waveRunning;
    private int _canUiLost;
    private int _waveCursorIndex = -1;
    private FirmwareImage? _image;
    private string? _imagePath;
    private SignalOption? _selectedWave;

    public MainWindow()
    {
        InitializeComponent();
        CanGrid.ItemsSource = _canRows;
        LogGrid.ItemsSource = _logRows;
        ScanGrid.ItemsSource = _scanRows;
        UdsTraceGrid.ItemsSource = _udsRows;
        DbcFiles.ItemsSource = _dbcFiles;
        _scannerRouter.AttachMode(ScannerCaptureMode.KeyboardWedge, new InputPolicy(true, 512, "keyboard"));
        _keyboardScanner = new KeyboardWedgeCapture(_scannerRouter);
        _device.ConnectionChanged += state => OnUi(() => SetConnectionState(state));
        _device.NativeOperation += fact =>
        {
            if (fact.Outcome is "FAILED" or "BLOCKED" or "CHECK")
                Log("USB NATIVE", $"{fact.Stage}: {fact.Outcome}, {fact.NativeErrorHex} {fact.Detail}");
        };
        _device.UsbTrace += fact =>
        {
            if (fact.Kind is "REQUEST_ERROR" or "REQUEST_TIMEOUT" or "READ_FAULT" or "CONNECTED" or "DISCONNECTED")
                Log("USB", $"Gen={fact.Generation} {fact.Kind} CMD={fact.Command:X2} {fact.Detail}");
        };
        _renderClock.Tick += (_, _) => DrainCanAndRender();
        _renderClock.Start();
        Log("APP", "新独立实测工作台启动；无自动连接、无 CAN 发送、无真实烧录。");
    }

    private void OnUi(Action callback)
    {
        if (_closing || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) callback();
        else _ = Dispatcher.BeginInvoke(callback, DispatcherPriority.Background);
    }
    private void Log(string module, string message)
    {
        OnUi(() =>
        {
            _logRows.Add(new LogRow(DateTime.Now.ToString("HH:mm:ss.fff"), module, message));
            while (_logRows.Count > 300) _logRows.RemoveAt(0);
            if (_logRows.Count > 0) LogGrid.ScrollIntoView(_logRows[^1]);
        });
    }
    private static string TracePath()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest", "FieldStudio", "Traces");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, $"field-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
    }
    private static uint ParseId(string value)
    {
        string v = value.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        return uint.Parse(v, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }
    private static byte[] ParsePdu(string text)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (compact.Length < 4 || compact.Length % 2 != 0 || compact.Length > 8190 ||
            compact.Any(c => !Uri.IsHexDigit(c)))
            throw new FormatException("请输入完整 UDS PDU 十六进制字节，不能包含非法字符。");
        return Convert.FromHexString(compact);
    }
    private static bool IsSafePhysicalRead(byte[] pdu) =>
        (pdu.Length >= 3 && pdu[0] == 0x22 && ((pdu.Length - 1) % 2 == 0)) ||
        (pdu.Length >= 2 && pdu[0] == 0x19 && (pdu[1] & 0x80) == 0);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConnectButton.IsEnabled = !busy && _device.State == UsbSessionState.Disconnected;
        DisconnectButton.IsEnabled = !busy && _device.State != UsbSessionState.Disconnected;
        UdsSendButton.IsEnabled = !busy && _device.State == UsbSessionState.Connected;
    }
    private void SetConnectionState(UsbSessionState state)
    {
        UsbStatus.Text = $"USB {state} · Gen {_device.Generation}";
        DeviceInfo.Text = $"设备: {_device.SelectedInstanceId ?? "未选择"}\nWinUSB: {_device.DevicePath ?? "—"}\n" +
            $"原始证据: {_device.TraceFile ?? "—"}";
        Footer.Text = $"USB={state}  ·  CAN1 DUT  ·  Gen={_device.Generation}  ·  原始 Trace={_device.TraceFile ?? "—"}";
        if (state != UsbSessionState.Connected) CanStatus.Text = "CAN1 DUT · 未连接";
        SetBusy(_busy);
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetConnectionState(_device.State);
        await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        try
        {
            var candidates = await Task.Run(_device.Enumerate);
            DeviceCombo.ItemsSource = candidates;
            if (_device.State == UsbSessionState.Disconnected && candidates.Count == 1)
                DeviceCombo.SelectedIndex = 0;
            Log("USB", $"发现 {candidates.Count} 个匹配现役主控板 VID/PID/GUID 的 WinUSB 接口。");
        }
        catch (Exception ex) { Log("USB", "枚举失败：" + ex); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _device.State != UsbSessionState.Disconnected) return;
        if (DeviceCombo.SelectedItem is not WinUsbCandidate selected)
        { MessageBox.Show(this, "请选择正确的主控板。", "连接", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        SetBusy(true);
        try
        {
            await _device.ConnectAsync(selected.DeviceInstanceId, TracePath());
            _rxStop = new CancellationTokenSource();
            var hub = _device.Hub ?? throw new IOException("CAN Hub not available.");
            _rxPump = Task.Run(() => ReceiveCanAsync(hub, _rxStop.Token));
            var version = await _device.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetVersion,
                TimeSpan.FromSeconds(2));
            DeviceVersion.Text = FirmwareReadOnlyProtocol.Explain(version.Command, version.Payload);
            Log("DEVICE", $"GET_VERSION 已核对：{DeviceVersion.Text}");
        }
        catch (Exception ex)
        {
            Log("USB", "连接或固件版本验证未完成：" + ex);
            MessageBox.Show(this, ex.Message, "USB / 版本验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); SetConnectionState(_device.State); }
    }
    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();
    private async Task DisconnectAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            _udsStop?.Cancel();
            _rxStop?.Cancel();
            await _device.DisconnectAsync();
            if (_rxPump != null)
            {
                try { await _rxPump; } catch (OperationCanceledException) { }
            }
            _rxPump = null;
            _rxStop?.Dispose(); _rxStop = null;
            DeviceVersion.Text = "已断开，版本证据失效";
            _requestId = Guid.Empty; _preparedHex = null; UdsExpected.Clear(); UdsConfirmation.Clear();
            Log("USB", "已断开；旧会话令牌失效。");
        }
        catch (Exception ex) { Log("USB", "断开失败：" + ex); }
        finally { SetBusy(false); SetConnectionState(_device.State); }
    }
    private async Task ReceiveCanAsync(CanFrameHub hub, CancellationToken ct)
    {
        var spec = new CanSubscriptionSpec(BusRole.DutCan,
            System.Collections.Immutable.ImmutableArray<uint>.Empty,
            CanBackpressurePolicy.DropWithLossFact, 4096);
        try
        {
            await foreach (var item in hub.SubscribeAsync(spec, ct).ConfigureAwait(false))
            {
                _pendingCan.Enqueue(item);
                if (_pendingCan.Count > 4000 && _pendingCan.TryDequeue(out _))
                    Interlocked.Increment(ref _canUiLost);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Log("CAN1 RX", "接收订阅中断：" + ex); }
    }
    private async void ReadVersion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _device.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetVersion, TimeSpan.FromSeconds(2));
            DeviceVersion.Text = FirmwareReadOnlyProtocol.Explain(response.Command, response.Payload);
            Log("DEVICE", DeviceVersion.Text);
        }
        catch (Exception ex) { Log("DEVICE", ex.Message); }
    }
    private async void ReadGpio_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _device.ReadOnlyCommandAsync(FirmwareReadOnlyProtocol.GetAllPower, TimeSpan.FromSeconds(2));
            Log("DEVICE", FirmwareReadOnlyProtocol.Explain(response.Command, response.Payload) +
                 "；仅 GPIO 读回，不是实际测得的电压。");
        }
        catch (Exception ex) { Log("DEVICE", "GPIO 只读查询失败：" + ex.Message); }
    }
    private void DrainCanAndRender()
    {
        int n = 0;
        while (n++ < 180 && _pendingCan.TryDequeue(out var obs))
        {
            var frame = obs.Frame;
            var raw = Convert.ToHexString(frame.Data.ToArray());
            _canRows.Add(new CanRow(obs.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
                "CAN1 DUT", $"0x{frame.Id:X}", frame.Dlc.ToString(CultureInfo.InvariantCulture),
                raw, $"{obs.Time.Quality} / lost={obs.LostFrames}"));
            while (_canRows.Count > 350) _canRows.RemoveAt(0);
            ProcessWaveFrame(obs);
        }
        CanStatus.Text = $"CAN1 RX: {_device.CanPublished:N0} · USB/Queue lost: {_device.CanDropped} · Hub lost: {_device.HubDropped}";
        CanQuality.Text = $"实际 CAN1 RX 计数 {_device.CanPublished}, 解码异常 {_device.CanMalformed}, " +
                          $"USB Decode QueueLost {_device.CanDropped}, HubLost {_device.HubDropped}, " +
                          $"UI 掉帧 {Volatile.Read(ref _canUiLost)}；时间戳为 PC 接收时间。";
        if (_waveRunning) DrawWave();
    }

    private async void ImportDbc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DBC files (*.dbc)|*.dbc|All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                if (_dbcFiles.Any(f => string.Equals(f.Receipt.Path, System.IO.Path.GetFullPath(file),
                    StringComparison.OrdinalIgnoreCase))) continue;
                var result = await DbcDocumentStore.OpenAsync(file);
                var entry = new DbcFileEntry(result.Document, result.Receipt);
                _dbcFiles.Add(entry);
                DbcFiles.SelectedItem = entry;
                Log("DBC", $"加载 {entry.Name}; 报文 {entry.Document.Model.Messages.Length}; " +
                    $"CanEdit={entry.Document.CanEdit}; SHA={entry.Receipt.Sha256}");
            }
            catch (Exception ex) { Log("DBC", $"{file}: {ex.Message}"); }
        }
        PopulateSignals();
    }
    private async void SaveDbc_Click(object sender, RoutedEventArgs e)
    {
        if (DbcFiles.SelectedItem is not DbcFileEntry file) return;
        try
        {
            if (!file.Document.CanEdit) throw new InvalidOperationException(
                "包含不支持的 DBC 扩展：只能只读导入，不允许有损导出。");
            file.Receipt = await DbcDocumentStore.SaveAsync(file.Document, file.Receipt.Path, file.Receipt.Sha256);
            DbcStatus.Text = $"已安全写入（冲突检查） {file.Name}";
            Log("DBC", $"已保存：{file.Receipt.Path} SHA={file.Receipt.Sha256}");
        }
        catch (Exception ex) { Log("DBC", "保存失败：" + ex.Message); }
    }
    private void RemoveDbc_Click(object sender, RoutedEventArgs e)
    {
        if (DbcFiles.SelectedItem is not DbcFileEntry entry) return;
        _dbcFiles.Remove(entry);
        DbcMessages.ItemsSource = null; DbcSignals.ItemsSource = null;
        DbcStatus.Text = "已从工作区移除，不删除磁盘文件";
        PopulateSignals();
    }
    private void DbcFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DbcFiles.SelectedItem is not DbcFileEntry entry)
        { DbcMessages.ItemsSource = null; DbcSignals.ItemsSource = null; return; }
        DbcMessages.ItemsSource = entry.Document.Model.Messages.Select(m =>
            new DbcMessageRow($"0x{m.Id:X}", m.Name, m.Dlc, m.Transmitter, m.Signals.Length)).ToList();
        DbcStatus.Text = $"{entry.Name} · Revision {entry.Document.Model.Revision} · " +
            (entry.Document.CanEdit ? "安全可写语法" : "扩展只读：" + string.Join(", ", entry.Document.UnsupportedDirectives.Take(3)));
    }
    private void DbcMessages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DbcFiles.SelectedItem is not DbcFileEntry entry ||
            DbcMessages.SelectedItem is not DbcMessageRow row) { DbcSignals.ItemsSource = null; return; }
        uint id = ParseId(row.Id);
        DbcSignals.ItemsSource = entry.Document.Model.FindMessage(id).Signals.Select(s => new
        {
            s.Name, s.StartBit, s.BitLength, ByteOrder = s.Intel ? "Intel" : "Motorola",
            s.Signed, s.Factor, s.Offset, s.Unit, s.Receiver, s.Multiplexer
        }).ToList();
    }
    private void PopulateSignals()
    {
        var previous = _selectedWave?.Label;
        var all = _dbcFiles.SelectMany(f => f.Document.Model.Messages.SelectMany(
            m => m.Signals.Select(s => new SignalOption(f, m, s)))).ToList();
        WaveSignals.ItemsSource = all;
        var selected = all.FirstOrDefault(s => s.Label == previous);
        if (selected is not null) WaveSignals.SelectedItem = selected;
        else if (all.Count > 0) WaveSignals.SelectedIndex = 0;
        else { _selectedWave = null; _waveRunning = false; _waveSamples.Clear(); DrawWave(); }
    }

    private void WaveSignals_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedWave = WaveSignals.SelectedItem as SignalOption;
        _waveSamples.Clear(); _waveReference = null; _waveCursorIndex = -1;
        WaveVerdict.Text = "未采集参考";
        WaveTitle.Text = _selectedWave is null ? "请选择信号" : _selectedWave.Label;
        WaveStatus.Text = _selectedWave is null ? "无 DBC 信号" : $"信号就绪 · {_selectedWave.Label}";
        DrawWave();
    }
    private void WaveRun_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWave is null) { Log("WAVE", "请先加载 DBC 并选中信号。"); return; }
        if (_device.State != UsbSessionState.Connected)
        { Log("WAVE", "设备尚未连接；不能将离线数据当作实时采样。"); return; }
        _waveRunning = true;
        WaveStatus.Text = "正在观察 CAN1 DUT 实际接收信号";
    }
    private void WaveStop_Click(object sender, RoutedEventArgs e)
    {
        _waveRunning = false; WaveStatus.Text = $"采集停止 · {_waveSamples.Count} 个样本";
    }
    private void ProcessWaveFrame(CanObservation obs)
    {
        if (!_waveRunning || _selectedWave is null) return;
        var selected = _selectedWave;
        if (obs.Frame.Id != selected.Message.Id || obs.Frame.Dlc != selected.Message.Dlc ||
            selected.Signal.Multiplexer is not null) return;
        try
        {
            double value = DbcSignalCodec.DecodePhysical(selected.Signal, obs.Frame.Data.AsSpan());
            _waveSamples.Add(new WavePoint(obs.Time.Utc, value, obs.LostFrames, selected.Signal.Unit));
            if (_waveSamples.Count > 1200)
            {
                _waveSamples.RemoveAt(0);
                _waveCursorIndex = -1;
            }
            WaveValue.Text = $"{value:0.###} {selected.Signal.Unit}";
            WaveQuality.Text = $"PC 接收时间 · 样本数 {_waveSamples.Count} · " +
                $"报告丢帧 {obs.LostFrames} · UI 丢帧 {Volatile.Read(ref _canUiLost)}";
        }
        catch (Exception ex)
        {
            Log("WAVE", "信号解码失败：" + ex.Message);
            _waveRunning = false;
        }
    }
    private void DrawWave()
    {
        WaveCanvas.Children.Clear();
        double width = WaveCanvas.ActualWidth, height = WaveCanvas.ActualHeight;
        if (width < 80 || height < 80) return;
        for (int i = 1; i < 5; i++)
        {
            double y = i * height / 5;
            WaveCanvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = Brushes.LightGray, StrokeThickness = .55 });
        }
        if (_waveSamples.Count < 2) return;
        double min = _waveSamples.Min(x => x.Value), max = _waveSamples.Max(x => x.Value);
        double span = Math.Max(1e-9, max - min);
        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(30, 115, 199)),
                                  StrokeThickness = 1.65 };
        int n = _waveSamples.Count;
        for (int i = 0; i < n; i++)
        {
            double x = 18 + (width - 36) * i / Math.Max(1, n - 1);
            double y = height - 20 - (height - 40) * (_waveSamples[i].Value - min) / span;
            line.Points.Add(new Point(x, y));
        }
        WaveCanvas.Children.Add(line);
        if (_waveCursorIndex >= 0 && _waveCursorIndex < n)
        {
            double x = 18 + (width - 36) * _waveCursorIndex / Math.Max(1, n - 1);
            WaveCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height,
                Stroke = Brushes.OrangeRed, StrokeThickness = 1 });
        }
    }
    private void WaveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawWave();
    private void WaveCanvas_Click(object sender, MouseButtonEventArgs e)
    {
        if (_waveSamples.Count < 2) return;
        double x = e.GetPosition(WaveCanvas).X;
        _waveCursorIndex = Math.Clamp((int)Math.Round((x - 18) /
            Math.Max(1, WaveCanvas.ActualWidth - 36) * (_waveSamples.Count - 1)), 0, _waveSamples.Count - 1);
        var point = _waveSamples[_waveCursorIndex];
        WaveCursor.Text = $"游标：PC 接收 {point.HostUtc:HH:mm:ss.fff} · {point.Value:0.####} {point.Unit} · loss={point.Lost}";
        DrawWave();
    }
    private void WaveReference_Click(object sender, RoutedEventArgs e)
    {
        if (_waveSamples.Count < 2) { Log("WAVE", "至少有两个真实样本才能采集参考。"); return; }
        _waveReference = _waveSamples.ToArray();
        WaveVerdict.Text = $"参考采集成功：{_waveReference.Length} 点（当前内存中，尚未正式建档）";
        Log("WAVE", WaveVerdict.Text);
    }
    private void WaveCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_waveReference is null || _waveSamples.Count < 2)
        { Log("WAVE", "请先采集参考，再继续接收真实样本。"); return; }
        if (_waveSamples.Any(x => x.Lost != 0) || Volatile.Read(ref _canUiLost) > 0)
        {
            WaveVerdict.Text = "UNCERTAIN：采集有损失；禁止判为 PASS。"; return;
        }
        int n = Math.Min(_waveReference.Length, _waveSamples.Count);
        if (n == 0) return;
        double max = Enumerable.Range(0, n).Max(i => Math.Abs(_waveReference[i].Value - _waveSamples[i].Value));
        WaveVerdict.Text = $"探索性比较：{n} 对样本，最大绝对差 {max:0.###}；" +
                           "未做时间对齐/容差配置，不提供正式 PASS/FAIL。";
        Log("WAVE", WaveVerdict.Text);
    }
    private async void WaveExport_Click(object sender, RoutedEventArgs e)
    {
        if (_waveSamples.Count == 0) { Log("WAVE", "没有采集样本。"); return; }
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"autotest-wave-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dlg.ShowDialog(this) != true) return;
        string body = "HostUtc,PhysicalValue,Unit,LostFrames\n" +
            string.Join("\n", _waveSamples.Select(x =>
                $"{x.HostUtc:O},{x.Value.ToString("R", CultureInfo.InvariantCulture)},\"{x.Unit.Replace("\"", "\"\"")}\",{x.Lost}"));
        try { await System.IO.File.WriteAllTextAsync(dlg.FileName, body); Log("WAVE", $"导出 {dlg.FileName}"); }
        catch (Exception ex) { Log("WAVE", "导出失败：" + ex.Message); }
    }

    private void UdsServices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UdsServices.SelectedItem is ListBoxItem item && item.Tag is string pdu)
        {
            UdsRequestText.Text = pdu;
            _requestId = Guid.Empty; _preparedHex = null;
            UdsExpected.Clear(); UdsConfirmation.Clear();
        }
    }
    private ReadOnlyUdsPhysicalEndpoint ReadEndpoint()
    {
        uint tx = ParseId(UdsTxId.Text), rx = ParseId(UdsRxId.Text);
        return new(UdsRevision.Text.Trim(), tx, rx, tx > 0x7FF);
    }
    private UdsEndpointProfile ReadProfile()
    {
        var endpoint = ReadEndpoint();
        endpoint.Validate();
        return new(BusRole.DutCan, endpoint.RequestCanId, endpoint.ResponseCanId,
            TimeSpan.FromMilliseconds(int.Parse(UdsP2.Text, CultureInfo.InvariantCulture)),
            TimeSpan.FromMilliseconds(int.Parse(UdsP2Star.Text, CultureInfo.InvariantCulture)), "Normal");
    }
    private void UdsPrepare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pdu = ParsePdu(UdsRequestText.Text);
            if (!IsSafePhysicalRead(pdu))
                throw new UnauthorizedAccessException("真实发送只允许 0x19 / 0x22 只读请求；其他服务仅展示，未获得物理授权。");
            var ep = ReadEndpoint();
            var profile = ReadProfile();
            AutoTest.Specialists.Uds.UdsEndpointProfileValidator.ValidateAddressing(profile);
            _requestId = Guid.NewGuid();
            _preparedHex = Convert.ToHexString(pdu);
            UdsExpected.Text = HardwareBenchController.PhysicalReadOnlyUdsConfirmation(_requestId, ep, pdu[0]);
            UdsConfirmation.Clear();
            UdsResponse.Text = "待操作者逐字确认授权内容，并检查 CAN ID 与目标 ECU。";
        }
        catch (Exception ex) { UdsExpected.Clear(); _requestId = Guid.Empty; UdsResponse.Text = ex.Message; }
    }
    private async void UdsSend_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        CancellationTokenSource? token = null;
        SetBusy(true);
        try
        {
            var pdu = ParsePdu(UdsRequestText.Text);
            if (_device.State != UsbSessionState.Connected || !_device.ProtocolVersionVerified)
                throw new InvalidOperationException("必须先连接并成功验证主控板版本。");
            if (!IsSafePhysicalRead(pdu) || _requestId == Guid.Empty ||
                _preparedHex != Convert.ToHexString(pdu))
                throw new UnauthorizedAccessException("请求已变化或未生成本次授权，拒绝发送。");
            if (!string.Equals(UdsExpected.Text, UdsConfirmation.Text.Trim(), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("必须手动粘贴当前授权串，拒绝自动授权。");
            var endpoint = ReadEndpoint();
            var profile = ReadProfile();
            AutoTest.Specialists.Uds.UdsEndpointProfileValidator.ValidateAddressing(profile);
            token = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            _udsStop = token;
            AppendUds("PREPARE", $"SID=0x{pdu[0]:X2} TX={endpoint.RequestCanId:X} RX={endpoint.ResponseCanId:X} PID={_requestId:N}");
            var response = await PhysicalReadOnlyUds.RunAsync(_device, endpoint, profile, pdu,
                _requestId, UdsConfirmation.Text.Trim(), token.Token);
            UdsResponse.Text = $"Outcome: {response.Outcome}\nRequest: {Convert.ToHexString(response.RequestPdu.ToArray())}\n" +
                $"Response: {Convert.ToHexString(response.ResponsePdu.ToArray())}\n" +
                $"NRC: {(response.NegativeResponseCode.HasValue ? $"0x{response.NegativeResponseCode:X2}" : "—")}\n" +
                $"Completed (Host): {response.CompletedAt.Utc:O}\n" +
                "注意：USB ACK 和 ECU 真实响应是两个不同的证据等级。";
            AppendUds("ECU RX", UdsResponse.Text.Replace("\n", " | "));
        }
        catch (OperationCanceledException) { UdsResponse.Text = "已取消；如果 CAN 帧已发出，ECU 状态需要单独观察。"; AppendUds("CANCEL", UdsResponse.Text); }
        catch (Exception ex) { UdsResponse.Text = "FAILED / UNCERTAIN: " + ex; AppendUds("ERROR", ex.Message); }
        finally
        {
            token?.Dispose(); _udsStop = null;
            _requestId = Guid.Empty; _preparedHex = null; UdsExpected.Clear(); UdsConfirmation.Clear();
            SetBusy(false);
        }
    }
    private void UdsCancel_Click(object sender, RoutedEventArgs e) => _udsStop?.Cancel();
    private void AppendUds(string direction, string details)
    {
        _udsRows.Add(new UdsRow(DateTime.Now.ToString("HH:mm:ss.fff"), direction, details));
        while (_udsRows.Count > 200) _udsRows.RemoveAt(0);
        Log("UDS", direction + ": " + details);
    }

    private async void FlashImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Firmware (*.hex;*.s19;*.s28;*.s37;*.bin)|*.hex;*.s19;*.s28;*.s37;*.bin|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            FirmwareImage image;
            if (ext == ".hex") image = FirmwareImageParser.ParseHex(await System.IO.File.ReadAllTextAsync(dlg.FileName));
            else if (ext is ".s19" or ".s28" or ".s37")
                image = FirmwareImageParser.ParseS19(await System.IO.File.ReadAllTextAsync(dlg.FileName));
            else if (ext == ".bin")
            {
                if (string.IsNullOrWhiteSpace(FlashBaseAddress.Text))
                    throw new InvalidOperationException("BIN 文件没有地址，必须由操作者显式输入起始地址。");
                image = FirmwareImageParser.ParseBin(await System.IO.File.ReadAllBytesAsync(dlg.FileName),
                    ParseId(FlashBaseAddress.Text));
            }
            else throw new FormatException("不支持的镜像格式。");
            _image = image;
            _imagePath = dlg.FileName;
            FlashSegments.ItemsSource = image.Segments.Select(s => new FirmwareSegmentRow(
                $"0x{s.Address:X8}", s.Data.Length, $"0x{s.EndExclusive:X}")).ToList();
            FlashInfo.Text = $"File: {dlg.FileName}\nFormat: {image.Format}\n有效字节: {image.ByteCount:N0}\n" +
                $"Segments: {image.Segments.Length}\nSHA256: {image.SourceFingerprint.Sha256}\n" +
                "未绑定目标 ECU/内存范围；不能开始烧录。";
            FlashStatus.Text = "镜像文件已解析。物理 FLASH 仍为 NOT_AUTHORIZED / NOT_RUN";
            Log("FLASH", $"导入 {dlg.FileName} bytes={image.ByteCount}；本操作不向 ECU 发送数据。");
        }
        catch (Exception ex) { _image = null; _imagePath = null; FlashSegments.ItemsSource = null; Log("FLASH", "导入失败：" + ex.Message); }
    }
    private void FlashInspect_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) { Log("FLASH", "请先选择固件文件。"); return; }
        try
        {
            FirmwareImageParser.ValidateSegments(_image.Segments);
            FlashStatus.Text = $"文件结构通过：{_image.Segments.Length} 个不重叠段、{_image.ByteCount:N0} 有效字节；" +
                "未做 ECU FlashMap / SeedKey / Bootloader 验证，禁止烧录。";
            Log("FLASH", FlashStatus.Text);
        }
        catch (Exception ex) { Log("FLASH", "结构检查失败：" + ex.Message); }
    }

    private void ScanBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char ch in e.Text)
        {
            var result = _keyboardScanner.OnKeyInput(ch, ScanBox.IsKeyboardFocused);
            if (result is not null) AdmitScan(result);
        }
    }
    private void ScanBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var result = _keyboardScanner.OnKeyInput('\r', ScanBox.IsKeyboardFocused);
            if (result is not null) AdmitScan(result);
            ScanBox.Clear(); e.Handled = true;
        }
    }
    private void AdmitScan(ScanIngressResult result)
    {
        if (!result.Accepted || result.Event is null)
        {
            Log("SCAN", "拒绝输入：" + result.Code);
            return;
        }
        string value = result.Event.Scan.Text;
        string status = _seenScans.Add(value) ? "已采集 / 待匹配" : "重复扫码 / 不新增出厂记录";
        CurrentDut.Text = $"DUT: {value}";
        _scanRows.Add(new ScanRow(DateTime.Now.ToString("HH:mm:ss.fff"),
            result.Event.Scan.Origin, value, status));
        while (_scanRows.Count > 400) _scanRows.RemoveAt(0);
        Log("SCAN", $"{value} · {status}（未启动任何测试或烧录）");
    }
    private void ClearLogs_Click(object sender, RoutedEventArgs e) => _logRows.Clear();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            _renderClock.Stop();
            _udsStop?.Cancel(); _rxStop?.Cancel();
            await _device.DisposeAsync();
            if (_rxPump is not null)
            {
                try { await _rxPump; } catch (OperationCanceledException) { }
            }
            _rxStop?.Dispose();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "关闭设备时出错"); }
        finally { _closed = true; Close(); }
    }
}
