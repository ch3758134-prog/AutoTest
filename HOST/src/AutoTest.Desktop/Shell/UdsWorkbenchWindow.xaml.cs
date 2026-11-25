using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AutoTest.Specialists.Uds.Workbench;
using AutoTest.Specialists.Uds;
using AutoTest.Desktop.Hardware;
using AutoTest.Devices.Usb;
using AutoTest.Contracts;
using AutoTest.Core;
using Microsoft.Win32;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>Editable view of the existing, validated UDS sequence document.</summary>
public sealed class UdsServiceEditRow : BindableBase
{
    private string _name;
    private string _request;
    private string _expected;
    public UdsServiceEditRow(string name, string request, string expected)
    {
        _name = name; _request = request; _expected = expected;
    }
    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }
    public string RequestHex { get => _request; set => SetProperty(ref _request, value ?? ""); }
    public string ExpectedHex { get => _expected; set => SetProperty(ref _expected, value ?? ""); }
}

/// <summary>
/// Independent UDS sequence editor with offline replay and one separately
/// authorized, physical 0x22 DID diagnostic over the main station USB/CAN.
/// File replay cannot be represented as a live ECU response; neither path
/// publishes a TEST/production verdict.
/// </summary>
public partial class UdsWorkbenchWindow : Window
{
    public ObservableCollection<UdsServiceEditRow> ServiceRows { get; } = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly HardwareConnectionService _hardware;
    private CancellationTokenSource? _didCancel;
    private StationDidRead? _lastDidEvidence;
    private DateTimeOffset _lastDidObservedUtc;
    private ushort _lastDidIdentifier;
    private uint _lastDidTxId, _lastDidRxId;
    private UdsReplayWorkspace? _replay;
    private string? _documentPath;
    private string? _loadedSha;
    private DateTime _lastEditUtc = DateTime.UtcNow;
    private bool _loading;
    private bool _busy;
    private bool _dirty;
    private bool _closing;
    private bool _closeApproved;
    private bool? _compactLayout;
    private bool _showReplayInCompact;
    private long _documentRevision;
    private long _replayRevision;

    public UdsWorkbenchWindow(HardwareConnectionService hardware)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _hardware.ConnectionChanged += OnDidStationChanged;
        _loading = true;
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        DataContext = this;
        P2Input.Text = "50";
        P2StarInput.Text = "5000";
        _loading = false;
        _autosave.Tick += async (_, _) =>
        {
            if (!_dirty || _documentPath is null || _busy || _closing || !IsActive ||
                DateTime.UtcNow - _lastEditUtc < TimeSpan.FromSeconds(2)) return;
            try { await SaveCurrentAsync(_documentPath, silent: true); }
            catch (Exception error) { StatusText.Text = "自动保存未完成：" + error.Message; }
        };
        Loaded += (_, _) => { _autosave.Start(); ApplyResponsiveLayout(); };
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        Closing += ClosingGuard;
        Closed += (_, _) =>
        {
            _hardware.ConnectionChanged -= OnDidStationChanged;
            _didCancel?.Cancel();
            _autosave.Stop();
        };
        ServiceRows.CollectionChanged += (_, _) =>
        {
            MarkDirty();
            UpdateReplayAvailability();
            UpdateDidScope();
        };
        UpdateDidScope();
    }

    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        var compact = ActualWidth < 1120 || ActualHeight < 690;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        // Keep the operator's endpoint-panel choice when resizing the window.
        // A wide layout must not unexpectedly reopen low-frequency settings
        // and steal height from the editable UDS service sequence.
        if (compact)
        {
            UdsEditorColumn.Width = new GridLength(1, GridUnitType.Star);
            UdsGapColumn.Width = new GridLength(0);
            UdsReplayColumn.Width = new GridLength(0);
            UdsPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            UdsReplayRow.Height = new GridLength(0);
            UdsColumnSplitter.Visibility = Visibility.Collapsed;
            UdsRowSplitter.Visibility = Visibility.Collapsed;
            UdsCompactNavigation.Visibility = Visibility.Visible;
            Grid.SetRow(UdsReplayPanel, 0);
            Grid.SetColumn(UdsReplayPanel, 0);
            UdsReplayPanel.Margin = new Thickness(0);
            ApplyCompactTab();
        }
        else
        {
            UdsEditorColumn.Width = new GridLength(1.15, GridUnitType.Star);
            UdsGapColumn.Width = new GridLength(7);
            UdsReplayColumn.Width = new GridLength(1, GridUnitType.Star);
            UdsPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            UdsReplayRow.Height = new GridLength(0);
            UdsColumnSplitter.Visibility = Visibility.Visible;
            UdsRowSplitter.Visibility = Visibility.Collapsed;
            UdsCompactNavigation.Visibility = Visibility.Collapsed;
            Grid.SetRow(UdsReplayPanel, 0);
            Grid.SetColumn(UdsReplayPanel, 2);
            UdsReplayPanel.Margin = new Thickness(0);
            UdsEditorPanel.Visibility = Visibility.Visible;
            UdsReplayPanel.Visibility = Visibility.Visible;
        }
    }

    private void UdsServicesTab_Click(object sender, RoutedEventArgs e)
    {
        _showReplayInCompact = false;
        ApplyCompactTab();
    }

    private void UdsReplayTab_Click(object sender, RoutedEventArgs e)
    {
        _showReplayInCompact = true;
        ApplyCompactTab();
    }

    private void ApplyCompactTab()
    {
        if (_compactLayout != true) return;
        UdsEditorPanel.Visibility = _showReplayInCompact ? Visibility.Collapsed : Visibility.Visible;
        UdsReplayPanel.Visibility = _showReplayInCompact ? Visibility.Visible : Visibility.Collapsed;
        UdsServicesTab.FontWeight = _showReplayInCompact ? FontWeights.Normal : FontWeights.SemiBold;
        UdsReplayTab.FontWeight = _showReplayInCompact ? FontWeights.SemiBold : FontWeights.Normal;
        UdsServicesTab.Background = _showReplayInCompact
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(230, 241, 255));
        UdsReplayTab.Background = _showReplayInCompact
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(230, 241, 255))
            : System.Windows.Media.Brushes.White;
    }

    private void SetEditorInputsEnabled(bool enabled)
    {
        SequenceName.IsEnabled = enabled;
        EcuIdentity.IsEnabled = enabled;
        RequestCanId.IsEnabled = enabled;
        ResponseCanId.IsEnabled = enabled;
        P2Input.IsEnabled = enabled;
        P2StarInput.IsEnabled = enabled;
        ServiceGrid.IsEnabled = enabled;
    }

    private void Workbench_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S:
                    SaveDocument_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.O:
                    OpenDocument_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.N:
                    NewDocument_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.D when ServiceGrid.IsKeyboardFocusWithin:
                    CopyService_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
            }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
            e.Key == Key.S)
        {
            SaveAsDocument_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (!ServiceGrid.IsKeyboardFocusWithin || e.OriginalSource is TextBoxBase)
            return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Insert)
        {
            AddService_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete &&
            SelectedRow is not null)
        {
            DeleteService_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _documentRevision++;
        if (_replay is not null && _replayRevision != _documentRevision)
        {
            ReplayOutcome.Text = "序列已编辑；旧回放匹配不再有效";
            ReplayExplanation.Text = "请重新载入对应的真实回放记录后再次逐项核对。";
            UpdateReplayAvailability();
        }
        _lastEditUtc = DateTime.UtcNow;
        SourceLabel.Text = (_documentPath ?? "未保存的独立 UDS 序列") + " · 已修改";
    }
    private void Profile_TextChanged(object sender, TextChangedEventArgs e)
    {
        MarkDirty();
        UpdateDidScope();
    }

    private void AddRow(UdsServiceEditRow row)
    {
        row.PropertyChanged += (_, _) =>
        {
            MarkDirty();
            UpdateDidScope();
        };
        ServiceRows.Add(row);
    }
    private UdsServiceEditRow? SelectedRow => ServiceGrid.SelectedItem as UdsServiceEditRow;
    private bool FinishGrid() =>
        ServiceGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
        ServiceGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private bool ConfirmReplaceDraft() =>
        !_dirty || MessageBox.Show(this,
            "当前诊断序列存在尚未保存的修改。确认放弃这些修改并继续？",
            "未保存的 UDS 草稿", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void NewDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !FinishGrid() || !ConfirmReplaceDraft()) return;
        _loading = true;
        try
        {
            ServiceRows.Clear();
            SequenceName.Text = "";
            EcuIdentity.Text = "";
            RequestCanId.Text = "";
            ResponseCanId.Text = "";
            P2Input.Text = "50";
            P2StarInput.Text = "5000";
            _documentPath = null; _loadedSha = null; _dirty = false;
            SourceLabel.Text = "新建独立 UDS 诊断序列 · 未保存";
            StatusText.Text = "请填写 ECU 标识、请求/响应 CAN ID 和实际服务请求，再保存序列。";
            ClearReplay();
        }
        finally { _loading = false; }
    }

    private async void OpenDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !FinishGrid() || !ConfirmReplaceDraft()) return;
        var picker = new OpenFileDialog
        {
            Title = "打开真实 UDS 诊断序列文档",
            Filter = "UDS 序列 (*.json)|*.json|全部文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        _busy = true;
        SetEditorInputsEnabled(false);
        try
        {
            var source = Path.GetFullPath(picker.FileName);
            var snapshot = await UdsToolDocumentStore.LoadSnapshotAsync(source);
            var loaded = snapshot.Document;
            _loading = true;
            try
            {
                ServiceRows.Clear();
                foreach (var item in loaded.Services)
                    AddRow(new UdsServiceEditRow(item.Name, item.RequestHex, item.ExpectedHex));
                SequenceName.Text = loaded.Profile.Name;
                EcuIdentity.Text = loaded.Profile.EcuIdentity;
                RequestCanId.Text = "0x" + loaded.Profile.TxId.ToString("X");
                ResponseCanId.Text = "0x" + loaded.Profile.RxId.ToString("X");
                P2Input.Text = loaded.Profile.P2Ms.ToString(CultureInfo.InvariantCulture);
                P2StarInput.Text = loaded.Profile.P2StarMs.ToString(CultureInfo.InvariantCulture);
                _documentPath = source;
                _loadedSha = snapshot.Sha256;
                _dirty = false;
                SourceLabel.Text = source + " · 独立序列";
                ClearReplay();
                StatusText.Text = "已加载 " + ServiceRows.Count + " 个有效服务；未连接或发送 ECU CAN。";
            }
            finally { _loading = false; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or FormatException or
            NotSupportedException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            StatusText.Text = "打开 UDS 文档失败，原草稿未被覆盖：" + error.Message;
        }
        finally { SetEditorInputsEnabled(true); _busy = false; }
    }

    private bool TryGetDidScope(out UdsEndpointProfile endpoint,
        out ushort did, out string confirmation)
    {
        endpoint = null!;
        did = 0;
        confirmation = "";
        if (SelectedRow is not { } selected) return false;
        try
        {
            var pdu = UdsHex.Parse(selected.RequestHex);
            if (pdu.Length != 3 || pdu[0] != 0x22) return false;
            var tx = ParseHexCanId(RequestCanId.Text, "请求 CAN ID");
            var rx = ParseHexCanId(ResponseCanId.Text, "响应 CAN ID");
            if (!int.TryParse(P2Input.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var p2) ||
                !int.TryParse(P2StarInput.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var p2Star)) return false;
            endpoint = UdsToolDocumentStore.ToEndpoint(new UdsToolProfile(
                SequenceName.Text.Trim(), EcuIdentity.Text.Trim(),
                tx, rx, p2, p2Star));
            did = (ushort)((pdu[1] << 8) | pdu[2]);
            confirmation = PhysicalCommandFence.ReadDidConfirmation(tx, rx, did);
            return true;
        }
        catch (Exception error) when (error is FormatException or InvalidDataException or
            ArgumentException or NotSupportedException) { return false; }
    }

    private void UpdateDidScope()
    {
        // XAML TextChanged may run during InitializeComponent before these
        // named controls have been constructed.
        if (DidScopeText is null || DidConsentInput is null || ReadDidButton is null) return;
        if (!TryGetDidScope(out _, out _, out var confirmation))
        {
            DidScopeText.Text = "仅支持选中 22 HH LL，请设置合法的 ECU CAN 请求/响应 ID 和 P2 超时。";
            ReadDidButton.IsEnabled = false;
            return;
        }
        DidScopeText.Text = confirmation;
        ReadDidButton.IsEnabled = !_busy && _didCancel is null &&
            _hardware.IsConnected && _hardware.IsEcuCanBitrateApplied &&
            string.Equals(DidConsentInput.Text, confirmation, StringComparison.Ordinal);
    }

    private void DidConsentChanged(object sender, TextChangedEventArgs e) => UpdateDidScope();

    private void OnDidStationChanged(long stationEpoch)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded) return;
            if (stationEpoch == 0) _didCancel?.Cancel();
            UpdateDidScope();
        }));
    }

    private void CopyDidAuthorization_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDidScope(out _, out _, out var authorization))
        {
            StatusText.Text = "先选中完整的 22 HH LL 服务并填写有效的 ECU CAN 端点。";
            return;
        }
        try
        {
            Clipboard.SetText(authorization);
            StatusText.Text = "已复制本次精确 DID 授权文本；粘贴到输入框后可明确执行一次。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StatusText.Text = "复制授权文本失败：" + error.Message;
        }
    }

    private async void ReadDid_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _didCancel is not null || !FinishGrid() ||
            !TryGetDidScope(out var endpoint, out var did, out var authorization))
        {
            StatusText.Text = "请选择合法的单次只读 22 HH LL 服务和 ECU CAN 端点。";
            return;
        }
        if (!string.Equals(DidConsentInput.Text, authorization, StringComparison.Ordinal))
        {
            StatusText.Text = "实体 DID 读取需要完整输入本次端点和 DID 的授权文本。";
            return;
        }
        if (!_hardware.IsConnected || !_hardware.IsEcuCanBitrateApplied)
        {
            StatusText.Text = "先在主窗口连接真实主控、应用并读回 ECU CAN 码率。";
            return;
        }
        var expectedHex = SelectedRow?.ExpectedHex ?? "";
        byte[]? expectedResponse = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(expectedHex))
                expectedResponse = UdsHex.Parse(expectedHex);
        }
        catch (FormatException error)
        {
            StatusText.Text = "期望响应的十六进制格式有误，未发送实体诊断：" + error.Message;
            return;
        }
        _busy = true;
        _lastDidEvidence = null;
        ExportDidReportButton.IsEnabled = false;
        _didCancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        SetEditorInputsEnabled(false);
        DidConsentInput.IsEnabled = false;
        ReadDidButton.IsEnabled = false;
        CancelDidButton.IsEnabled = true;
        DidFrames.ItemsSource = null;
        DidResponseText.Text = "";
        DidOutcomeText.Text = "正在经真实 USB / ECU CAN 读取 DID 0x" +
            did.ToString("X4", CultureInfo.InvariantCulture) + "…";
        StatusText.Text = "物理授权仅涵盖本次 0x22 DID 与真实 ECU 响应流控；没有发送其他 UDS 服务。";
        try
        {
            var observed = await _hardware.ReadOnlyDidAsync(
                endpoint, did, authorization, _didCancel.Token);
            _lastDidEvidence = observed;
            _lastDidObservedUtc = DateTimeOffset.UtcNow;
            _lastDidIdentifier = did;
            _lastDidTxId = endpoint.RequestCanId;
            _lastDidRxId = endpoint.ResponseCanId;
            ExportDidReportButton.IsEnabled = true;
            var exchange = observed.Exchange;
            DidFrames.ItemsSource = observed.CanFrames;
            DidResponseText.Text = "请求  " + UdsHex.Format(exchange.RequestPdu) +
                "\n响应  " + (exchange.ResponsePdu.IsDefaultOrEmpty
                    ? "（未取得可确认的 ECU 响应）" : UdsHex.Format(exchange.ResponsePdu));
            bool? expectedMatch = expectedResponse is null ? null :
                exchange.ResponsePdu.SequenceEqual(expectedResponse);
            var detail = exchange.Outcome switch
            {
                Outcome.Accepted => "真实 ECU 正响应（不是正式检测 PASS）",
                Outcome.Rejected => "真实 ECU 拒绝 / NRC 0x" +
                    (exchange.NegativeResponseCode?.ToString("X2", CultureInfo.InvariantCulture) ?? "??"),
                _ => "结果不确定，不能认定 DID 读取成功"
            };
            DidOutcomeText.Text = "DID 0x" + did.ToString("X4", CultureInfo.InvariantCulture) +
                " · " + detail +
                (expectedMatch is null ? " · 未设置期望响应" :
                    expectedMatch.Value ? " · 与编辑期望相符" : " · 与编辑期望不符") +
                (string.IsNullOrWhiteSpace(observed.Error) ? "" : " · " + observed.Error);
            StatusText.Text = "已结束单次真实 DID 交换；收发记录来自当前主控会话，不生成生产 PASS。";
            _showReplayInCompact = true;
            ApplyCompactTab();
        }
        catch (OperationCanceledException)
        {
            DidOutcomeText.Text = "操作员停止或 30 秒安全期限到期；不能判定 ECU 已回复。";
            StatusText.Text = "DID 读取取消。物理授权已撤销；如有迟到报文不会作为本次成功。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DidOutcomeText.Text = "实体 DID 读取失败：" + error.Message;
            StatusText.Text = "未形成可信诊断结果；请检查 USB/ECU CAN、端点、P2 与实体授权。";
        }
        finally
        {
            _didCancel?.Dispose();
            _didCancel = null;
            _busy = false;
            SetEditorInputsEnabled(true);
            DidConsentInput.IsEnabled = true;
            DidConsentInput.Clear(); // A second physical request requires new consent.
            CancelDidButton.IsEnabled = false;
            UpdateDidScope();
        }
    }

    private async void ExportDidReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _lastDidEvidence is not { } observation) return;
        var picker = new SaveFileDialog
        {
            Title = "导出本次真实 CAN1 / UDS DID 诊断记录",
            FileName = "UDS_DID_" + _lastDidIdentifier.ToString("X4") + "_" +
                _lastDidObservedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss"),
            Filter = "只读 DID 诊断 JSON (*.json)|*.json",
            AddExtension = true, OverwritePrompt = false
        };
        if (picker.ShowDialog(this) != true) return;
        var destination = Path.GetFullPath(picker.FileName);
        if (File.Exists(destination))
        {
            StatusText.Text = "同名诊断记录已存在，禁止覆盖原证据；请选择其他文件名。";
            return;
        }
        _busy = true;
        UpdateDidScope();
        try
        {
            // Export only the frozen facts from this one actual WinUSB exchange,
            // never make it a production RECORD, a synthetic response or PASS.
            var data = new
            {
                Schema = 1,
                Kind = "ENGINEERING_UDS_READ_DID_NOT_PRODUCTION_RECORD",
                Source = "REAL_USB_0x41_ECU_CAN1_HOST_OBSERVED",
                ObservedUtc = _lastDidObservedUtc,
                StationEpoch = observation.StationEpoch,
                Device = observation.UsbDeviceIdentity,
                TxCanId = "0x" + _lastDidTxId.ToString("X"),
                RxCanId = "0x" + _lastDidRxId.ToString("X"),
                Did = "0x" + _lastDidIdentifier.ToString("X4"),
                Result = observation.Exchange.Outcome.ToString(),
                RequestPduHex = UdsHex.Format(observation.Exchange.RequestPdu),
                ResponsePduHex = UdsHex.Format(observation.Exchange.ResponsePdu),
                NegativeResponseCode = observation.Exchange.NegativeResponseCode is { } nrc
                    ? "0x" + nrc.ToString("X2") : null,
                Error = observation.Error,
                ActualFrames = observation.CanFrames
            };
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllBytesAsync(destination, json);
            StatusText.Text = "已导出本次真实诊断事实（非正式生产报告）：" + destination;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StatusText.Text = "诊断记录导出失败，原始数据显示仍保留：" + error.Message;
        }
        finally
        {
            _busy = false;
            UpdateDidScope();
        }
    }

    private void CancelDid_Click(object sender, RoutedEventArgs e)
    {
        if (_didCancel is null) return;
        CancelDidButton.IsEnabled = false;
        StatusText.Text = "正在取消实体 DID 交换并撤销授权…";
        _didCancel.Cancel();
    }

    private static uint ParseHexCanId(string text, string name)
    {
        var raw = text.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw[2..];
        if (!uint.TryParse(raw, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var value) || value > 0x1FFFFFFF)
            throw new InvalidDataException(name + " 必须是合法的 11/29 位 CAN ID（十六进制）。");
        return value;
    }

    private UdsToolDocument CaptureDocument()
    {
        if (!FinishGrid()) throw new InvalidDataException("请先完成正在编辑的表格单元格。");
        var requestId = ParseHexCanId(RequestCanId.Text, "请求 CAN ID");
        var responseId = ParseHexCanId(ResponseCanId.Text, "响应 CAN ID");
        if (!int.TryParse(P2Input.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var p2) ||
            !int.TryParse(P2StarInput.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var p2star))
            throw new InvalidDataException("请填写合法的 P2/P2* 毫秒整数。");
        var document = new UdsToolDocument(1,
            new UdsToolProfile(SequenceName.Text.Trim(), EcuIdentity.Text.Trim(),
                requestId, responseId, p2, p2star),
            ServiceRows.Select(r => new UdsServiceDefinition(
                r.Name.Trim(), r.RequestHex.Trim(), r.ExpectedHex.Trim())).ToList());
        UdsToolDocumentStore.Validate(document);
        return document;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private async Task<bool> SaveCurrentAsync(string destination, bool silent = false)
    {
        if (_busy) return false;
        _busy = true;
        try
        {
            var document = CaptureDocument();
            // Capture the committed edit before the first await. Prevent
            // further typing from being lost when the async save finishes
            // and clears the dirty flag.
            SetEditorInputsEnabled(false);
            var absolute = Path.GetFullPath(destination);
            if (File.Exists(absolute))
            {
                var currentSha = Hash(await File.ReadAllBytesAsync(absolute));
                if (_loadedSha is null || !string.Equals(
                        _documentPath, absolute, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_loadedSha, currentSha, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("文件已有内容或已被其他程序修改，拒绝覆盖。请选择新的文件名。");
            }
            else if (_documentPath is not null &&
                     string.Equals(_documentPath, absolute, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("已打开的序列文件已被移走，不能静默重建。", absolute);
            await UdsToolDocumentStore.SaveAsync(absolute, document);
            var verified = await UdsToolDocumentStore.LoadSnapshotAsync(absolute);
            if (verified.Document.Schema != document.Schema ||
                verified.Document.Profile != document.Profile ||
                !verified.Document.Services.SequenceEqual(document.Services))
                throw new InvalidDataException("UDS 序列保存后回读内容不一致。");
            _loadedSha = verified.Sha256;
            _documentPath = absolute;
            _dirty = false;
            SourceLabel.Text = absolute + " · 独立序列";
            StatusText.Text = "已保存 " + document.Services.Count + " 个真实配置的 UDS 服务；未发送任何 ECU 指令。";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException or FormatException or NotSupportedException or
            KeyNotFoundException or System.Text.Json.JsonException)
        {
            StatusText.Text = (silent ? "自动保存失败：" : "无法保存 UDS 序列：") +
                error.Message + "；草稿仍保留。";
            return false;
        }
        finally { SetEditorInputsEnabled(true); _busy = false; }
    }

    private async void SaveDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_documentPath is null) await SaveAsAsync();
        else await SaveCurrentAsync(_documentPath);
    }
    private async void SaveAsDocument_Click(object sender, RoutedEventArgs e) => await SaveAsAsync();
    private async Task SaveAsAsync()
    {
        if (_busy) return;
        var picker = new SaveFileDialog
        {
            Title = "保存独立 UDS 服务序列",
            Filter = "UDS 序列 JSON (*.json)|*.json",
            FileName = "诊断服务序列.uds.json",
            OverwritePrompt = false, AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        // Always reject overwriting a different existing file, even if the
        // standard SaveFileDialog was configured not to prompt.
        await SaveCurrentAsync(picker.FileName);
    }

    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !FinishGrid()) return;
        var n = ServiceRows.Count + 1;
        var name = "服务 " + n.ToString("D2", CultureInfo.InvariantCulture);
        while (ServiceRows.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = "服务 " + (++n).ToString("D2", CultureInfo.InvariantCulture);
        var row = new UdsServiceEditRow(name, "", "");
        AddRow(row);
        ServiceGrid.SelectedItem = row;
        ServiceGrid.ScrollIntoView(row);
        StatusText.Text = "已在序列中加入服务；填写实际请求 PDU，保存时进行 ISO-TP/UDS 参数校验。";
    }
    private void CopyService_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !FinishGrid() || SelectedRow is not { } src) return;
        var name = src.Name + " 副本";
        while (ServiceRows.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            name += " 2";
        var copy = new UdsServiceEditRow(name, src.RequestHex, src.ExpectedHex);
        ServiceRows.Insert(ServiceRows.IndexOf(src) + 1, copy);
        copy.PropertyChanged += (_, _) =>
        {
            MarkDirty();
            UpdateDidScope();
        };
        ServiceGrid.SelectedItem = copy;
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        if (_busy || !FinishGrid() || SelectedRow is not { } row) return;
        var a = ServiceRows.IndexOf(row);
        var b = a + delta;
        if (b < 0 || b >= ServiceRows.Count) return;
        ServiceRows.Move(a, b);
        ServiceGrid.SelectedItem = row;
    }
    private void DeleteService_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || SelectedRow is not { } row) return;
        if (MessageBox.Show(this, "从当前序列移除“" + row.Name +
                "”？已归档的独立诊断回放不会被删除。",
                "删除诊断服务", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ServiceRows.Remove(row);
    }
    private void ServiceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateReplayAvailability();
        UpdateDidScope();
    }

    private async void LoadReplay_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !FinishGrid()) return;
        var picker = new OpenFileDialog
        {
            Title = "载入已有的 UDS 诊断交换记录（只做离线回放）",
            Filter = "UDS 回放 JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        _busy = true;
        SetEditorInputsEnabled(false);
        try
        {
            var replay = new UdsReplayWorkspace();
            await replay.LoadAsync(picker.FileName);
            _replay = replay;
            _replayRevision = _documentRevision;
            ReplayFileLabel.Text = Path.GetFullPath(picker.FileName) + " · " +
                replay.Count + " 项录制交换";
            ReplayOutcome.Text = "文件回放已载入；尚未进行请求匹配";
            ReplayResponse.Text = "";
            ReplayExplanation.Text = "核对将按服务序列顺序使用文件中记录，不会发送 USB/CAN 请求。";
            ReplayFrames.ItemsSource = null;
            StatusText.Text = "已载入诊断记录；其来源真实性需由采集记录和现场证据另行核验。";
            UpdateReplayAvailability();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or FormatException or ArgumentException or
            System.Text.Json.JsonException)
        {
            StatusText.Text = "回放文件无法使用，原序列未修改：" + error.Message;
        }
        finally { SetEditorInputsEnabled(true); _busy = false; }
    }
    private void UpdateReplayAvailability() =>
        ReplayNext.IsEnabled = _replay is not null && _replayRevision == _documentRevision &&
            _replay.Position < _replay.Count && _replay.Position < ServiceRows.Count;

    private void NextReplay_Click(object sender, RoutedEventArgs e)
    {
        if (_replay is null || _replayRevision != _documentRevision ||
            !FinishGrid() || _replay.Position >= ServiceRows.Count) return;
        try
        {
            var position = _replay.Position;
            var step = ServiceRows[position];
            var exchange = _replay.Consume(step.RequestHex, step.ExpectedHex);
            ReplayOutcome.Text = (position + 1) + ". " + step.Name + " · " +
                exchange.Outcome + " · NRC " + exchange.Nrc;
            ReplayResponse.Text = "REQ  " + exchange.RequestHex + "\nRSP  " + exchange.ResponseHex;
            ReplayExplanation.Text = "期望响应：" +
                (exchange.ExpectedMatched is null ? "未设置" :
                    exchange.ExpectedMatched.Value ? "匹配" : "不匹配") +
                " · 来源：" + exchange.Source +
                " · " + exchange.ErrorCode + "（来自所选回放文件，非实时 ECU 回复）";
            ReplayFrames.ItemsSource = exchange.Frames.Select(f =>
                f.Direction + "  " + f.Hex).ToArray();
            StatusText.Text = "已核对回放中的第 " + (position + 1) +
                " 项；此操作不会生成测试 PASS 或执行 CAN 发送。";
            UpdateReplayAvailability();
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidOperationException or
            ArgumentException or FormatException)
        {
            StatusText.Text = "回放未通过一致性检查，位置保持不变：" + error.Message;
        }
    }
    private void ClearReplay_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) ClearReplay();
    }
    private void ClearReplay()
    {
        _replay = null;
        ReplayFileLabel.Text = "尚未加载回放文件";
        ReplayOutcome.Text = "暂无诊断结果";
        ReplayResponse.Text = "";
        ReplayExplanation.Text = "";
        ReplayFrames.ItemsSource = null;
        UpdateReplayAvailability();
    }

    /// <summary>Parent shutdown needs a synchronous completion result, not a pending child Closing event.</summary>
    public async Task<bool> PrepareForParentShutdownAsync()
    {
        if (_busy || _closing || !FinishGrid()) return false;
        if (_dirty)
        {
            if (_documentPath is null)
            {
                if (!ConfirmReplaceDraft()) return false;
            }
            else if (!await SaveCurrentAsync(_documentPath))
                return false;
        }
        _closeApproved = true;
        Close();
        return !IsVisible;
    }

    private async void ClosingGuard(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        if (_busy) { e.Cancel = true; StatusText.Text = "正在保存或读取真实序列，尚不能关闭。"; return; }
        if (!FinishGrid())
        {
            e.Cancel = true;
            StatusText.Text = "当前服务单元格仍无法结束编辑；请先修正或取消编辑。";
            return;
        }
        if (!_dirty) { _closeApproved = true; return; }
        if (_documentPath is null)
        {
            if (!ConfirmReplaceDraft()) { e.Cancel = true; return; }
            _closeApproved = true;
            return;
        }
        // Saved documents auto-persist; a validation or disk error leaves the
        // editor open with its draft and a visible corrective action.
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            if (await SaveCurrentAsync(_documentPath))
            {
                _closeApproved = true;
                _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
            }
        }
        finally { _closing = false; }
    }
}
