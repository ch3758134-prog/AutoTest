using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;
using AutoTest.Specialists.Uds.Workbench;
using Microsoft.Win32;

namespace AutoTest.UdsLab;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DiagnosticAction> _actions = [];
    private readonly List<AuditRecord> _audit = [];
    private readonly List<string> _rawLines = [];
    private readonly DiagnosticEcuSessionTracker _sessionTracker = new();
    private DiagnosticAction? _editing;
    private UdsLabHardwareSession? _hardware;
    private CancellationTokenSource? _operation;
    private Task? _pendingRequest;
    private bool _loadingEditor;
    private bool _requiresReconnect;
    private bool _closing;
    private string? _workspaceFile;

    public MainWindow()
    {
        InitializeComponent();
        InitializeServiceComposer();
        InitializeExchangeHistory();
        ActionsList.ItemsSource = _actions;
        _actions.Add(new DiagnosticAction
        {
            Name = "示例 · 读取 VIN（先核对 ECU 规范）",
            Group = "ECU 信息",
            RequestHex = "22 F1 90",
            ExpectedPrefixHex = "62 F1 90",
            Decoder = "Ascii", DataOffset = 3
        });
        ActionsList.SelectedIndex = 0;
        NameBox.LostFocus += (_, _) => ActionsList.Items.Refresh();
        GroupBox.LostFocus += (_, _) => ActionsList.Items.Refresh();
        Closing += OnClosing;
        Audit("WORKSPACE", "UDS Lab 启动。预置操作仅为标准诊断示例，尚未经过本 ECU 实测。");
        RefreshSendButton();
    }

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _editing = ActionsList.SelectedItem as DiagnosticAction;
        LoadEditor();
    }

    private void LoadEditor()
    {
        _loadingEditor = true;
        try
        {
            var a = _editing;
            NameBox.Text = a?.Name ?? "";
            GroupBox.Text = a?.Group ?? "";
            RequestBox.Text = a?.RequestHex ?? "";
            ExpectedBox.Text = a?.ExpectedPrefixHex ?? "";
            OffsetBox.Text = a?.DataOffset.ToString(CultureInfo.InvariantCulture) ?? "3";
            ScaleBox.Text = a?.Scale.ToString("G", CultureInfo.InvariantCulture) ?? "1";
            UnitBox.Text = a?.Unit ?? "";
            string decoder = a?.Decoder ?? "Hex";
            DecodeBox.SelectedItem = DecodeBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string?)i.Tag == decoder) ?? DecodeBox.Items[0];
            LoadServiceEditor(a);
        }
        finally { _loadingEditor = false; }
    }

    private void EditorChanged(object sender, TextChangedEventArgs e) => UpdateActionFromEditor();
    private void EditorChangedSelection(object sender, SelectionChangedEventArgs e) => UpdateActionFromEditor();

    private void UpdateActionFromEditor()
    {
        if (_loadingEditor || _editing is null) return;
        _editing.Name = NameBox.Text;
        _editing.Group = GroupBox.Text;
        _editing.RequestHex = RequestBox.Text;
        _editing.ExpectedPrefixHex = ExpectedBox.Text;
        _editing.Decoder = (DecodeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Hex";
        _editing.Unit = UnitBox.Text;
        if (int.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            _editing.DataOffset = offset;
        if (double.TryParse(ScaleBox.Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var scale))
            _editing.Scale = scale;
        SaveServiceEditorState();
        RefreshSendButton();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var action = new DiagnosticAction { RawMode = true };
        _actions.Add(action);
        ActionsList.SelectedItem = action;
        NameBox.Focus();
        NameBox.SelectAll();
        Audit("EDIT", "新建空白诊断操作。");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var clone = _editing.Duplicate();
        _actions.Add(clone);
        ActionsList.SelectedItem = clone;
        Audit("EDIT", "复制诊断操作：" + clone.Name);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var name = _editing.Name;
        if (MessageBox.Show(this, "确定删除操作「" + name + "」？", "删除诊断操作",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        int index = ActionsList.SelectedIndex;
        while (_manualQueue.Remove(_editing)) { }
        _actions.Remove(_editing);
        if (_actions.Count > 0) ActionsList.SelectedIndex = Math.Min(index, _actions.Count - 1);
        else { _editing = null; LoadEditor(); }
        Audit("EDIT", "删除诊断操作：" + name);
    }

    private DiagnosticWorkspaceDocument CaptureWorkspace()
    {
        UpdateActionFromEditor();
        return new DiagnosticWorkspaceDocument
        {
            TxIdHex = TxIdBox.Text.Trim(),
            RxIdHex = RxIdBox.Text.Trim(),
            P2Ms = int.TryParse(P2Box.Text, out int p2) ? p2 : 1000,
            P2StarMs = int.TryParse(P2StarBox.Text, out int p2Star) ? p2Star : 5000,
            Actions = _actions.ToList(),
            ManualSequence = _manualQueue.Select(a => a.Id).ToList(),
            PollIntervalMs = int.TryParse(WatchIntervalBox.Text, out int poll) ? poll : 2000
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = _workspaceFile;
            if (path is null)
            {
                var save = new SaveFileDialog
                {
                    Filter = "UDS 工作台 (*.uds-workspace.json)|*.uds-workspace.json|JSON (*.json)|*.json",
                    FileName = "UdsWorkspace.uds-workspace.json"
                };
                if (save.ShowDialog(this) != true) return;
                path = save.FileName;
            }
            await DiagnosticWorkspace.SaveAsync(path, CaptureWorkspace());
            _workspaceFile = path;
            Audit("FILE", "已保存工作台：" + path);
        }
        catch (Exception ex) { ShowFailure("保存失败", ex); }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_hardware is not null || _pendingRequest is not null)
        {
            MessageBox.Show(this, "先断开真实硬件，再切换诊断配置，避免地址与实体会话不一致。");
            return;
        }
        var open = new OpenFileDialog
        {
            Filter = "UDS 工作台 (*.uds-workspace.json)|*.uds-workspace.json|JSON (*.json)|*.json"
        };
        if (open.ShowDialog(this) != true) return;
        try
        {
            var document = await DiagnosticWorkspace.LoadAsync(open.FileName);
            _manualQueue.Clear();
            _actions.Clear();
            foreach (var action in document.Actions) _actions.Add(action);
            var lookup = _actions.ToDictionary(a => a.Id);
            foreach (var id in document.ManualSequence)
                if (lookup.TryGetValue(id, out var action)) _manualQueue.Add(action);
            WatchIntervalBox.Text = document.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
            TxIdBox.Text = document.TxIdHex;
            RxIdBox.Text = document.RxIdHex;
            P2Box.Text = document.P2Ms.ToString(CultureInfo.InvariantCulture);
            P2StarBox.Text = document.P2StarMs.ToString(CultureInfo.InvariantCulture);
            ActionsList.SelectedIndex = _actions.Count > 0 ? 0 : -1;
            _workspaceFile = open.FileName;
            RefreshSendButton();
            Audit("FILE", "已打开工作台：" + open.FileName);
        }
        catch (Exception ex) { ShowFailure("打开失败", ex); }
    }

    private static uint ParseCanId(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return uint.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private UdsEndpointProfile ParseEndpoint()
    {
        if (!int.TryParse(P2Box.Text, out int p2) ||
            !int.TryParse(P2StarBox.Text, out int p2Star) ||
            p2 is < 1 or > 60000 || p2Star < p2 || p2Star > 120000)
            throw new FormatException("P2 必须为 1..60000 ms；P2* 必须大于等于 P2、最多 120000 ms。");
        var endpoint = new UdsEndpointProfile(
            BusRole.DutCan, ParseCanId(TxIdBox.Text), ParseCanId(RxIdBox.Text),
            TimeSpan.FromMilliseconds(p2), TimeSpan.FromMilliseconds(p2Star), "Normal");
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        return endpoint;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            var endpoint = ParseEndpoint();
            _hardware = new UdsLabHardwareSession(endpoint, ConfirmFrameAsync, Trace);
            ConnectionStatus.Text = "正在连接真实 WinUSB 主控并初始化 ISO-TP 接收...";
            await _hardware.ConnectAsync(CancellationToken.None);
            _requiresReconnect = false;
            TxIdBox.IsEnabled = RxIdBox.IsEnabled = P2Box.IsEnabled = P2StarBox.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            RefreshSendButton();
            ConnectionStatus.Text = "主控已连接 · " + _hardware.DeviceIdentity +
                " · UDS 接收已就绪；CAN 租约截止 " +
                _hardware.LeaseExpiresUtc?.ToLocalTime().ToString("HH:mm:ss") + "。";
            _sessionTracker.Invalidate("仅 USB 主控在线，诊断会话未确认");
            EcuSessionStatus.Text = _sessionTracker.Description + " 0x10/0x3E 尚未获实体授权。";
            Audit("CONNECT", "主控连接 / USB Ping / ISO-TP RX 已就绪；ECU 在线状态仍未确认。");
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = "连接失败：" + ex.Message;
            _sessionTracker.Invalidate("连接失败");
            EcuSessionStatus.Text = _sessionTracker.Description;
            Audit("CONNECT_ERROR", ex.ToString());
            if (_hardware is not null) await _hardware.DisposeAsync();
            _hardware = null;
            ConnectButton.IsEnabled = true;
            ShowFailure("连接失败", ex);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();

    private async Task DisconnectAsync()
    {
        _operation?.Cancel();
        if (_pendingRequest is not null)
        {
            try { await _pendingRequest; } catch { /* request outcome is logged by RunRequestAsync */ }
        }
        _pendingRequest = null;
        if (_hardware is not null)
        {
            await _hardware.DisposeAsync();
            _hardware = null;
        }
        _requiresReconnect = false;
        ConnectionStatus.Text = "已断开。旧诊断会话失效，重新连接前不能发送 ECU 请求。";
        _sessionTracker.Invalidate("断开 USB，不能继承 ECU 会话");
        EcuSessionStatus.Text = _sessionTracker.Description;
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        SendButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        TxIdBox.IsEnabled = RxIdBox.IsEnabled = P2Box.IsEnabled = P2StarBox.IsEnabled = true;
        Audit("DISCONNECT", "主控已断开。");
    }

    private async Task<string?> ConfirmFrameAsync(CanFrame frame, byte[] payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var expected = "AUTHORIZE CAN TX " + Convert.ToHexString(payload);
        var description = "DUT CAN / ID=0x" + frame.Id.ToString("X") +
            " / DLC=" + frame.Dlc + " / DATA=" + UdsHex.Format(frame.Data);
        // Exact-frame consent is provided manually, never fabricated in the hardware adapter.
        var dispatcherOperation = Dispatcher.InvokeAsync(() =>
        {
            var dialog = new FrameConsentDialog(this, expected, description);
            using var cancellation = ct.Register(() =>
                Dispatcher.BeginInvoke(new Action(() => { if (dialog.IsVisible) dialog.Close(); })));
            return dialog.ShowDialog() == true ? dialog.Confirmation : null;
        });
        return await dispatcherOperation.Task.WaitAsync(ct);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null || _pendingRequest is not null) return;
        if (_hardware is null || !_hardware.IsReady || _requiresReconnect)
        {
            MessageBox.Show(this, _requiresReconnect
                ? "上次诊断请求已取消或中断，为避免 ISO-TP 残帧污染，请先断开并重新连接。"
                : "请先连接主控并填写真实 ECU 诊断参数。", "无法发送");
            return;
        }
        try
        {
            var action = _editing ?? throw new InvalidOperationException("请先建立或选中诊断操作。");
            byte[] request = ValidateLiveRead(action, repeated: false);
            var snapshot = action.Duplicate();
            snapshot.Name = action.Name;
            var correlation = Guid.NewGuid();
            _operation = new CancellationTokenSource();
            SendButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            _pendingRequest = RunRequestAsync(snapshot, request, correlation, _operation.Token);
            await _pendingRequest;
        }
        catch (Exception ex) { ShowFailure("请求预检失败", ex); }
        finally
        {
            _pendingRequest = null;
            _operation?.Dispose();
            _operation = null;
            RefreshSendButton();
            CancelButton.IsEnabled = false;
        }
    }

    private async Task<bool> RunRequestAsync(DiagnosticAction action, byte[] request,
        Guid correlation, CancellationToken ct)
    {
        LastRequestBox.Text = UdsHex.Format(request);
        LastResponseBox.Text = "";
        ParsedBox.Text = "";
        DetailBox.Text = "等待 ECU 响应；USB ACK 不等于 ECU 收到。";
        ResultStatus.Text = "等待人工物理帧授权 / ECU 响应…";
        var started = DateTimeOffset.UtcNow;
        Audit("UDS_REQUEST", correlation + " · " + action.Name + " · " + UdsHex.Format(request));
        try
        {
            var (result, trace) = await _hardware!.RequestAsync(request, correlation, ct);
            ConnectionStatus.Text = "主控已连接 · " + _hardware.DeviceIdentity +
                " · HOST CAN 租约截止 " + _hardware.LeaseExpiresUtc?.ToLocalTime().ToString("HH:mm:ss") +
                "；ECU 诊断会话单独判断。";
            foreach (var entry in trace)
                Audit("UDS_" + entry.Direction.ToString().ToUpperInvariant(),
                    correlation + " · " + entry.Utc.ToString("O") + " · " +
                    (entry.Data.Length > 0 ? UdsHex.Format(entry.Data) : entry.Note));
            var response = result.ResponsePdu.ToArray();
            LastResponseBox.Text = UdsHex.Format(response);
            string elapsed = (DateTimeOffset.UtcNow - started).TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
            if (result.Outcome == Outcome.Accepted)
            {
                bool expectedMatches = string.IsNullOrWhiteSpace(action.ExpectedPrefixHex) ||
                    response.AsSpan().StartsWith(UdsHex.Parse(action.ExpectedPrefixHex));
                bool didMatches = request[0] != 0x22 ||
                    (request.Length >= 3 && response.Length >= 3 &&
                     response[1] == request[1] && response[2] == request[2]);
                ResultStatus.Text = !expectedMatches || !didMatches
                    ? "ECU 正响应，但数据结构 / 期望不匹配"
                    : "收到 ECU 正响应";
                ResultStatus.Foreground = expectedMatches && didMatches ? Brushes.ForestGreen : Brushes.DarkOrange;
                ParsedBox.Text = DiagnosticWorkspace.Decode(action, response);
                DetailBox.Text = "SID=0x" + response[0].ToString("X2") + " · " + elapsed +
                    " ms · 期望前缀=" + expectedMatches + " · 首 DID 回显=" + didMatches +
                    "。已收到正响应不代表 ECU 功能状态已产生实际变化。";
                Audit(expectedMatches && didMatches ? "UDS_POSITIVE" : "UDS_MISMATCH",
                    correlation + " · " + ResultStatus.Text + " · " + elapsed + "ms");
                RecordExchange(action.Name, request, LastResponseBox.Text, ParsedBox.Text,
                    ResultStatus.Text, DetailBox.Text, started, correlation);
                _sessionTracker.ObservePositive(request, response);
                EcuSessionStatus.Text = _sessionTracker.Description;
                return expectedMatches && didMatches;
            }
            else if (result.NegativeResponseCode is byte nrc)
            {
                ResultStatus.Text = "ECU 否定响应 NRC=0x" + nrc.ToString("X2");
                ResultStatus.Foreground = Brushes.DarkOrange;
                DetailBox.Text = NrcName(nrc) + " · " + elapsed +
                    " ms。否定响应不等于通信失败；请核对会话、权限、参数与 ECU 条件。";
                Audit("UDS_NEGATIVE", correlation + " · " + DetailBox.Text);
                RecordExchange(action.Name, request, LastResponseBox.Text, "",
                    ResultStatus.Text, DetailBox.Text, started, correlation);
                _sessionTracker.ObserveNegative(nrc);
                EcuSessionStatus.Text = _sessionTracker.Description;
                return false;
            }
            else
            {
                ResultStatus.Text = "响应未确认 / 通信结果不确定";
                ResultStatus.Foreground = Brushes.DarkRed;
                DetailBox.Text = "未收到完整有效的最终 ECU 响应。请检查 UDS 追踪、CAN/USB 原始记录。";
                _requiresReconnect = true;
                Audit("UDS_UNCERTAIN", correlation + " · " + DetailBox.Text);
                RecordExchange(action.Name, request, LastResponseBox.Text, "",
                    ResultStatus.Text, DetailBox.Text, started, correlation);
                _sessionTracker.Invalidate("链路不确定，必须重新连接");
                EcuSessionStatus.Text = _sessionTracker.Description;
                return false;
            }
        }
        catch (OperationCanceledException ex)
        {
            _requiresReconnect = true;
            ResultStatus.Text = "用户中止 / 物理发送授权被拒绝";
            ResultStatus.Foreground = Brushes.DarkOrange;
            DetailBox.Text = ex.Message + "。ECU 是否已执行副作用未知；必须重新连接。";
            Audit("UDS_CANCEL", correlation + " · " + DetailBox.Text);
            RecordExchange(action.Name, request, LastResponseBox.Text, "",
                ResultStatus.Text, DetailBox.Text, started, correlation);
            _sessionTracker.Invalidate("用户取消，旧会话不可确认");
            EcuSessionStatus.Text = _sessionTracker.Description;
            return false;
        }
        catch (Exception ex)
        {
            _requiresReconnect = true;
            ResultStatus.Text = "请求异常，结果不确定";
            ResultStatus.Foreground = Brushes.DarkRed;
            DetailBox.Text = ex.GetType().Name + ": " + ex.Message +
                "。请检查原始记录并重新连接。";
            Audit("UDS_ERROR", correlation + " · " + ex);
            RecordExchange(action.Name, request, LastResponseBox.Text, "",
                ResultStatus.Text, DetailBox.Text, started, correlation);
            _sessionTracker.Invalidate("通信异常");
            EcuSessionStatus.Text = _sessionTracker.Description;
            return false;
        }
    }

    private static string NrcName(byte nrc) => nrc switch
    {
        0x11 => "ServiceNotSupported",
        0x12 => "SubFunctionNotSupported",
        0x13 => "IncorrectMessageLengthOrInvalidFormat",
        0x21 => "BusyRepeatRequest",
        0x22 => "ConditionsNotCorrect",
        0x31 => "RequestOutOfRange",
        0x33 => "SecurityAccessDenied",
        0x35 => "InvalidKey",
        0x36 => "ExceededNumberOfAttempts",
        0x37 => "RequiredTimeDelayNotExpired",
        0x78 => "ResponsePending（中间状态，不是最终否定响应）",
        0x7E => "SubFunctionNotSupportedInActiveSession",
        0x7F => "ServiceNotSupportedInActiveSession",
        _ => "ECU 报告的诊断负响应码"
    };

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is null) return;
        _requiresReconnect = true;
        _operation.Cancel();
        Audit("UDS_CANCEL_REQUEST", "已请求取消等待；取消不表示 ECU 取消了诊断操作。");
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "UDS 诊断证据 JSON (*.json)|*.json",
            FileName = "UdsLab_Evidence_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var export = new
            {
                Format = "AutoTest.UdsLab.Evidence.v1",
                ExportUtc = DateTimeOffset.UtcNow,
                LiveSessionNow = _hardware?.IsReady ?? false,
                DeviceIdentity = _hardware?.DeviceIdentity,
                Profile = new { TxIdBox.Text, RxId = RxIdBox.Text, P2 = P2Box.Text, P2Star = P2StarBox.Text },
                Actions = CaptureWorkspace().Actions,
                Audit = _audit.ToArray(),
                PhysicalTrace = _rawLines.ToArray(),
                Exchanges = _exchangeHistory.ToArray(),
                Limitations = "USB ACK is not ECU response; not field-validated; exact-frame manual authorization required."
            };
            await File.WriteAllTextAsync(dialog.FileName,
                JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            Audit("EXPORT", "已导出诊断证据：" + dialog.FileName);
        }
        catch (Exception ex) { ShowFailure("导出失败", ex); }
    }

    private void Trace(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Trace(line)));
            return;
        }
        string entry = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "  " + line;
        _rawLines.Add(entry);
        RawTraceBox.AppendText(entry + Environment.NewLine);
        RawTraceBox.ScrollToEnd();
    }

    private void Audit(string kind, string detail)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Audit(kind, detail)));
            return;
        }
        var record = new AuditRecord(DateTimeOffset.UtcNow, kind, detail);
        _audit.Add(record);
        string line = record.Utc.ToLocalTime().ToString("HH:mm:ss.fff") +
                      "  " + kind + "  " + detail;
        HistoryList.Items.Add(line);
        HistoryList.ScrollIntoView(line);
    }

    private void ShowFailure(string title, Exception ex)
    {
        Audit("ERROR", title + "：" + ex);
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_hardware is null) return;
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try { await DisconnectAsync(); }
        finally { Close(); }
    }
}
