using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;
using AutoTest.Specialists.Uds.Workbench;
using Microsoft.Win32;

namespace AutoTest.UdsLab;

/// <summary>
/// One actual UDS attempt. The request is copied at Send time; subsequent UI
/// edits and request replay never rewrite an earlier physical exchange.
/// </summary>
public sealed record DiagnosticHistoryRow(
    DateTimeOffset At, Guid CorrelationId, string Name, string Request,
    string Response, string Status, string Decoded, string Detail, long Milliseconds,
    string Decoder, int DataOffset, double Scale, string Unit, string ExpectedPrefix)
{
    public string Time => At.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Elapsed => Milliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DiagnosticAction> _favorites = [];
    private readonly ObservableCollection<DiagnosticHistoryRow> _history = [];
    private readonly List<AuditRecord> _audit = [];
    private readonly List<string> _traceLines = [];
    private UdsLabHardwareSession? _hardware;
    private CancellationTokenSource? _pendingCancel;
    private Task? _pendingSend;
    private string? _workspacePath;
    private bool _ready;
    private bool _settingEditor;
    private bool _needsReconnect;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        FavoriteCombo.ItemsSource = _favorites;
        HistoryList.ItemsSource = _history;
        DecodeBox.SelectedIndex = 0;
        ServiceCombo.ItemsSource = UdsOperationTemplates.All;
        _ready = true;
        _settingEditor = true;
        try
        {
            ServiceCombo.SelectedItem = UdsOperationTemplates.Find(0x22);
            PrimaryBox.Text = "";
            AuxiliaryBox.Text = "";
            ServiceHelp.Text = "填写目标 ECU 的两字节 DID；软件会自动组成 0x22 请求。没有 ECU 诊断定义时不猜测 DID。";
            RequestBox.Text = "";
            ExpectedBox.Text = "";
        }
        finally { _settingEditor = false; }
        RefreshTemplate();
        RefreshButtons();
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        AppendAudit("START", "UDS 调试窗口启动。仅实际 ECU 返回的数据会出现在交互历史里。");
    }

    private static string HexId(uint id) => "0x" + id.ToString(id > 0x7FF ? "X8" : "X3");
    private static uint ParseCanId(string input)
    {
        string text = input.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return uint.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private UdsEndpointProfile ReadEndpoint()
    {
        if (!int.TryParse(P2Box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p2) ||
            !int.TryParse(P2StarBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p2Star) ||
            p2 is < 1 or > 60000 || p2Star < p2 || p2Star > 120000)
            throw new FormatException("P2 应为 1–60000 ms，P2* 不小于 P2 且不超过 120000 ms。");
        var endpoint = new UdsEndpointProfile(
            BusRole.DutCan, ParseCanId(TxIdBox.Text), ParseCanId(RxIdBox.Text),
            TimeSpan.FromMilliseconds(p2), TimeSpan.FromMilliseconds(p2Star), "Normal");
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        return endpoint;
    }

    private void ServiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingEditor || RawModeCheck.IsChecked == true) return;
        var template = ServiceCombo.SelectedItem as UdsOperationTemplate;
        if (template is null) return;
        _settingEditor = true;
        try
        {
            PrimaryBox.Text = template.PrimaryDefault;
            AuxiliaryBox.Text = template.AuxiliaryDefault;
            SelectDecoder("Hex");
            BindPresets(template);
        }
        finally { _settingEditor = false; }
        ComposeRequest();
    }

    private void BindPresets(UdsOperationTemplate? template, string? currentValue = null)
    {
        UdsSubfunctionPreset[] choices = template is null ? [] : UdsOperationTemplates.PresetsFor(template.Sid).ToArray();
        bool previouslySetting = _settingEditor;
        _settingEditor = true; // Binding a preset must not replace an ECU-specific parameter.
        try
        {
            PresetCombo.ItemsSource = choices;
            PresetCombo.Visibility = choices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (choices.Length > 0 && !string.IsNullOrWhiteSpace(currentValue))
                PresetCombo.SelectedItem = choices.FirstOrDefault(x =>
                    string.Equals(x.PrimaryHex, currentValue.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        finally { _settingEditor = previouslySetting; }
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingEditor || PresetCombo.SelectedItem is not UdsSubfunctionPreset p) return;
        _settingEditor = true;
        try
        {
            PrimaryBox.Text = p.PrimaryHex;
            if (p.Sid is 0x19 or 0x10 or 0x11 or 0x3E)
                AuxiliaryBox.Text = p.AuxiliaryHex;
        }
        finally { _settingEditor = false; }
        ComposeRequest();
    }

    private void RawModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _settingEditor) return;
        RefreshTemplate();
        if (RawModeCheck.IsChecked != true) ComposeRequest();
        else
        {
            ComposerStatus.Text = "原始模式：直接修改请求 HEX，软件不会覆盖你的字节。实体发送仍由底层授权校验。";
            DraftStatus.Text = "自由 HEX";
            RefreshButtons();
        }
    }

    private void RefreshTemplate()
    {
        bool raw = RawModeCheck.IsChecked == true;
        var template = ServiceCombo.SelectedItem as UdsOperationTemplate;
        ServiceCombo.IsEnabled = !raw;
        PrimaryBox.IsEnabled = AuxiliaryBox.IsEnabled = !raw && template is not null;
        PresetCombo.IsEnabled = !raw;
        RequestBox.IsReadOnly = !raw;
        if (template is not null)
        {
            PrimaryLabel.Text = template.PrimaryLabel;
            AuxiliaryLabel.Text = template.AuxiliaryLabel;
            ServiceHelp.Text = raw ? "原始请求可直接编辑；服务参数不会同步覆盖 HEX。"
                : template.Guidance;
            if (!raw) BindPresets(template, PrimaryBox.Text);
        }
        else ServiceHelp.Text = "可以使用原始 HEX 构造厂商自定义请求。";
    }

    private void ParametersChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _settingEditor || RawModeCheck.IsChecked == true) return;
        ComposeRequest();
    }

    private void ComposeRequest()
    {
        if (!_ready || _settingEditor || RawModeCheck.IsChecked == true) return;
        if (ServiceCombo.SelectedItem is not UdsOperationTemplate template) return;
        _settingEditor = true;
        try
        {
            RefreshTemplate();
            if (string.IsNullOrWhiteSpace(PrimaryBox.Text) &&
                (template.Sid is 0x22 or 0x24 or 0x19 or 0x23))
            {
                RequestBox.Text = "";
                ExpectedBox.Text = "";
                ComposerStatus.Text = "填写 " + template.PrimaryLabel + " 后即可生成请求。";
                DraftStatus.Text = "等待填写参数";
                return;
            }
            var composed = UdsOperationTemplates.Compose(template, PrimaryBox.Text, AuxiliaryBox.Text);
            RequestBox.Text = composed.RequestHex;
            ExpectedBox.Text = composed.ExpectedPrefixHex;
            if (template.Sid is 0x19 or 0x22 or 0x24)
            {
                SelectDecoder(composed.Decoder);
                OffsetBox.Text = composed.DecoderOffset.ToString(CultureInfo.InvariantCulture);
            }
            ComposerStatus.Text = template.PhysicalEnabled
                ? "请求已生成，可在连接后发送；实体帧仍需人工确认。"
                : "已生成请求；该服务目前未获准在真实 ECU 上发送。";
            DraftStatus.Text = "当前请求可编辑";
        }
        catch (FormatException ex)
        {
            RequestBox.Text = "";
            ExpectedBox.Text = "";
            ComposerStatus.Text = "参数待补充：" + ex.Message;
            DraftStatus.Text = "参数不完整";
        }
        finally { _settingEditor = false; RefreshButtons(); }
    }

    private void RequestChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _settingEditor) return;
        DraftStatus.Text = "已修改 · 可再次发送";
        RefreshButtons();
    }

    private void DecoderChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _settingEditor) return;
        DraftStatus.Text = "请求/解析配置已修改";
    }

    private void SelectDecoder(string id)
    {
        var item = DecodeBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(c => (string?)c.Tag == id);
        if (item is not null) DecodeBox.SelectedItem = item;
    }

    private DiagnosticAction CaptureEditor()
    {
        string request = UdsHex.Format(UdsHex.Parse(RequestBox.Text));
        string expected = string.IsNullOrWhiteSpace(ExpectedBox.Text)
            ? "" : UdsHex.Format(UdsHex.Parse(ExpectedBox.Text));
        if (!int.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ||
            offset is < 0 or > 4095)
            throw new FormatException("响应数据偏移必须为 0–4095。");
        if (!double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
            !double.IsFinite(scale) || Math.Abs(scale) > 1e12)
            throw new FormatException("比例必须为有效数字。");
        var template = RawModeCheck.IsChecked == true
            ? UdsOperationTemplates.Find(UdsHex.Parse(request)[0])
            : ServiceCombo.SelectedItem as UdsOperationTemplate;
        string name = template is null ? "自定义诊断请求" : template.Name;
        if (template is not null)
            name += " " + (RawModeCheck.IsChecked == true || PrimaryBox.Text.Trim().Length == 0
                ? request : PrimaryBox.Text.Trim());
        return new DiagnosticAction
        {
            Name = name, Group = "常用请求",
            RequestHex = request, ExpectedPrefixHex = expected,
            ServiceSidHex = RawModeCheck.IsChecked == true ? "" : template?.Sid.ToString("X2") ?? "",
            PrimaryHex = PrimaryBox.Text, AuxiliaryHex = AuxiliaryBox.Text,
            RawMode = RawModeCheck.IsChecked == true,
            Decoder = (DecodeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Hex",
            DataOffset = offset, Scale = scale, Unit = UnitBox.Text
        };
    }

    private static byte[] ValidateReadPdu(DiagnosticAction action)
    {
        var pdu = UdsHex.Parse(action.RequestHex);
        // Keep independent UI, shared client and real DEVICE gates aligned.
        // This intentionally does not authorize 0x10/0x3E/0x2A or OEM writes.
        if (pdu[0] is not (0x19 or 0x22 or 0x23 or 0x24))
            throw new UnauthorizedAccessException("实体诊断目前只开放 0x19/0x22/0x23/0x24 只读服务。");
        var descriptor = new UdsServiceCatalog().Get(pdu[0]);
        if (pdu.Length < descriptor.MinimumRequestLength)
            throw new FormatException(descriptor.Name + " 请求长度不足。");
        if (pdu[0] == 0x22 && (pdu.Length - 1) % 2 != 0)
            throw new FormatException("DID 必须每两字节成组。");
        return pdu;
    }

    private void RefreshButtons()
    {
        if (!_ready) return;
        bool canSend = !_needsReconnect && _hardware?.IsReady == true && _pendingCancel is null;
        try
        {
            var pdu = UdsHex.Parse(RequestBox.Text);
            canSend &= pdu[0] is 0x19 or 0x22 or 0x23 or 0x24;
        }
        catch (FormatException) { canSend = false; }
        SendButton.IsEnabled = canSend;
        CancelButton.IsEnabled = _pendingCancel is not null;
        RequestState.Text = _pendingCancel is not null ? "等待真实 ECU 最终响应…"
            : _needsReconnect ? "链路状态不确定，请断开重连"
            : _hardware?.IsReady == true ? "选择参数后发送；可修改再发"
            : "先设置 CAN ID 并连接主控";
    }

    private void SaveFavorite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = CaptureEditor();
            if (_favorites.Count >= 500) throw new InvalidOperationException("最多收藏 500 条请求。");
            // A favorite is an immutable snapshot until explicitly selected,
            // edited and re-saved. No name/group wizard before ordinary sending.
            var existing = _favorites.Count(x => x.Name.StartsWith(draft.Name, StringComparison.Ordinal));
            if (existing > 0) draft.Name += " (" + (existing + 1).ToString(CultureInfo.InvariantCulture) + ")";
            _favorites.Add(draft);
            FavoriteCombo.SelectedItem = draft;
            AppendAudit("FAVORITE", "收藏请求 " + draft.Name + " · " + draft.RequestHex);
            ComposerStatus.Text = "已添加常用请求；点击“保存配置”可写入 JSON 文件。";
        }
        catch (Exception ex) { ShowError("收藏失败", ex); }
    }

    private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (FavoriteCombo.SelectedItem is not DiagnosticAction selected) return;
        _favorites.Remove(selected);
        FavoriteCombo.SelectedItem = null;
        AppendAudit("FAVORITE", "删除收藏 " + selected.Name);
    }

    private void FavoriteSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingEditor || FavoriteCombo.SelectedItem is not DiagnosticAction action) return;
        LoadAction(action);
    }

    private void LoadAction(DiagnosticAction action)
    {
        _settingEditor = true;
        try
        {
            byte[] pdu = UdsHex.Parse(action.RequestHex);
            var template = UdsOperationTemplates.Find(pdu[0]);
            ServiceCombo.SelectedItem = template;
            var parts = template is null ? (Primary: "", Auxiliary: "")
                : UdsOperationTemplates.SplitRequest(template, pdu);
            PrimaryBox.Text = parts.Primary;
            AuxiliaryBox.Text = parts.Auxiliary;
            RawModeCheck.IsChecked = action.RawMode || template is null;
            RequestBox.Text = action.RequestHex;
            ExpectedBox.Text = action.ExpectedPrefixHex;
            SelectDecoder(action.Decoder);
            OffsetBox.Text = action.DataOffset.ToString(CultureInfo.InvariantCulture);
            ScaleBox.Text = action.Scale.ToString("G", CultureInfo.InvariantCulture);
            UnitBox.Text = action.Unit;
            DraftStatus.Text = "已回填请求 · 可以修改并重发";
        }
        finally { _settingEditor = false; }
        RefreshTemplate();
        RefreshButtons();
    }

    private void Reuse_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not DiagnosticHistoryRow row) return;
        var action = new DiagnosticAction
        {
            Name = row.Name, RequestHex = row.Request,
            ExpectedPrefixHex = row.ExpectedPrefix, RawMode = false,
            Decoder = row.Decoder, DataOffset = row.DataOffset, Scale = row.Scale,
            Unit = row.Unit
        };
        LoadAction(action);
        AppendAudit("REUSE", "回填历史请求 " + row.Request + "，尚未重新发送。");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_hardware is not null) return;
        UdsEndpointProfile endpoint;
        try { endpoint = ReadEndpoint(); }
        catch (Exception ex)
        {
            SettingsExpander.IsExpanded = true;
            ShowError("通信参数无效", ex);
            return;
        }
        ConnectButton.IsEnabled = false;
        ConnectionStatus.Text = "正在连接真实主控板及 ISO-TP 接收通路…";
        try
        {
            _hardware = new UdsLabHardwareSession(endpoint, ConfirmFrameAsync, AppendTrace);
            await _hardware.ConnectAsync(CancellationToken.None);
            _needsReconnect = false;
            TxIdBox.IsEnabled = RxIdBox.IsEnabled = P2Box.IsEnabled = P2StarBox.IsEnabled = false;
            SettingsExpander.IsExpanded = false;
            DisconnectButton.IsEnabled = true;
            ConnectionStatus.Text = "主控在线 · 请求 " + HexId(endpoint.RequestCanId) +
                " / 响应 " + HexId(endpoint.ResponseCanId) +
                " · USB Ping 已通过（不代表 ECU 已响应）";
            SessionStatus.Text = "ECU 会话未知；仅真实 UDS 响应可确认 ECU 通信。";
            AppendAudit("CONNECT", "真实主控连接成功；未自动发送任何 ECU 诊断请求。");
        }
        catch (Exception ex)
        {
            AppendAudit("CONNECT_ERROR", ex.ToString());
            if (_hardware is not null) { await _hardware.DisposeAsync(); _hardware = null; }
            ConnectionStatus.Text = "主控连接失败：" + ex.Message;
            SessionStatus.Text = "ECU 会话未知";
            ConnectButton.IsEnabled = true;
            ShowError("主控连接失败", ex);
        }
        finally { RefreshButtons(); }
    }

    private async Task<string?> ConfirmFrameAsync(CanFrame frame, byte[] payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string expected = "AUTHORIZE CAN TX " + Convert.ToHexString(payload);
        string desc = "DUT CAN  ID=0x" + frame.Id.ToString("X") +
            "  DLC=" + frame.Dlc + "  DATA=" + UdsHex.Format(frame.Data);
        var op = Dispatcher.InvokeAsync(() =>
        {
            var dlg = new FrameConsentDialog(this, expected, desc);
            using var reg = ct.Register(() =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (dlg.IsVisible) dlg.Close();
                })));
            return dlg.ShowDialog() == true ? dlg.Confirmation : null;
        });
        return await op.Task.WaitAsync(ct);
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();

    private async Task DisconnectAsync()
    {
        _pendingCancel?.Cancel();
        if (_pendingSend is not null)
        {
            try { await _pendingSend; } catch { /* outcome is logged by SendCurrentAsync */ }
        }
        if (_hardware is not null)
        {
            await _hardware.DisposeAsync();
            _hardware = null;
        }
        _needsReconnect = false;
        TxIdBox.IsEnabled = RxIdBox.IsEnabled = P2Box.IsEnabled = P2StarBox.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        ConnectButton.IsEnabled = true;
        ConnectionStatus.Text = "已断开 · 请选择通信参数并重新连接";
        SessionStatus.Text = "ECU 会话未知 / 已断开";
        AppendAudit("DISCONNECT", "主控断开；不得继承旧 ECU 会话状态。");
        RefreshButtons();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingCancel is not null || _pendingSend is not null) return;
        if (_hardware?.IsReady != true || _needsReconnect)
        {
            MessageBox.Show(this, _needsReconnect ? "上次请求状态不确定，请断开重连后再试。"
                : "请先连接真实主控板。", "无法发送");
            return;
        }
        DiagnosticAction draft;
        byte[] pdu;
        try
        {
            draft = CaptureEditor();
            pdu = ValidateReadPdu(draft);
        }
        catch (Exception ex) { ShowError("请求检查失败", ex); return; }

        _pendingCancel = new CancellationTokenSource();
        RefreshButtons();
        _pendingSend = SendCurrentAsync(draft, pdu, _pendingCancel.Token);
        try { await _pendingSend; }
        finally
        {
            _pendingSend = null;
            _pendingCancel?.Dispose();
            _pendingCancel = null;
            RefreshButtons();
        }
    }

    private async Task SendCurrentAsync(DiagnosticAction draft, byte[] pdu, CancellationToken ct)
    {
        var began = DateTimeOffset.UtcNow;
        Guid correlation = Guid.NewGuid();
        string responseHex = "", decoded = "";
        string status = "未确认", detail = "";
        AppendAudit("UDS_REQUEST", correlation + " " + UdsHex.Format(pdu));
        try
        {
            var (result, trace) = await _hardware!.RequestAsync(pdu, correlation, ct);
            foreach (var entry in trace)
                AppendAudit("UDS_" + entry.Direction.ToString().ToUpperInvariant(),
                    correlation + " " + (entry.Data.Length > 0 ? UdsHex.Format(entry.Data) : entry.Note));

            var response = result.ResponsePdu.ToArray();
            responseHex = UdsHex.Format(response);
            if (result.Outcome == Outcome.Accepted)
            {
                bool matches = string.IsNullOrWhiteSpace(draft.ExpectedPrefixHex) ||
                    response.AsSpan().StartsWith(UdsHex.Parse(draft.ExpectedPrefixHex));
                bool didEcho = pdu[0] != 0x22 ||
                    (pdu.Length >= 3 && response.Length >= 3 &&
                     response[1] == pdu[1] && response[2] == pdu[2]);
                decoded = DiagnosticWorkspace.Decode(draft, response);
                status = matches && didEcho ? "ECU 正响应" : "正响应内容不匹配";
                detail = "已收到 ECU 最终正响应；期望前缀=" + matches +
                    "，首 DID 回显=" + didEcho + "。";
                SessionStatus.Text = "ECU 已返回 UDS 正响应；当前会话类型仍未知。";
            }
            else if (result.NegativeResponseCode is byte nrc)
            {
                status = "ECU NRC 0x" + nrc.ToString("X2");
                detail = NrcDescription(nrc) + "；请核对会话、权限及 ECU 条件。";
                SessionStatus.Text = "ECU 返回 NRC；不能由 NRC 推定当前会话。";
            }
            else
            {
                status = "响应未确认";
                detail = "未获得完整最终 ECU 响应。ISO-TP 状态可能不确定，需断开重连。";
                _needsReconnect = true;
            }
        }
        catch (OperationCanceledException)
        {
            status = "已取消 / 未确认";
            detail = "已取消本地等待或实体帧授权；不能据此断定 ECU 取消了操作。请断开重连。";
            _needsReconnect = true;
        }
        catch (Exception ex)
        {
            status = "执行异常 / 未确认";
            detail = ex.GetType().Name + ": " + ex.Message +
                "。本次 ECU 实际执行情况可能未知，需断开重连。";
            _needsReconnect = true;
            AppendAudit("UDS_ERROR", correlation + " " + ex);
        }

        long ms = (long)Math.Max(0, (DateTimeOffset.UtcNow - began).TotalMilliseconds);
        var row = new DiagnosticHistoryRow(began, correlation, draft.Name,
            UdsHex.Format(pdu), responseHex, status, decoded, detail, ms,
            draft.Decoder, draft.DataOffset, draft.Scale, draft.Unit, draft.ExpectedPrefixHex);
        _history.Add(row);
        EmptyHistory.Visibility = Visibility.Collapsed;
        HistoryCount.Text = _history.Count.ToString(CultureInfo.InvariantCulture) + " 条";
        HistoryList.SelectedItem = row;
        HistoryList.ScrollIntoView(row);
        AppendAudit("UDS_RESULT", correlation + " " + status + " " + ms + "ms");
        if (_needsReconnect) SessionStatus.Text = "诊断链路状态未知 · 断开并重新连接";
    }

    private static string NrcDescription(byte nrc) => nrc switch
    {
        0x10 => "GeneralReject",
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
        0x78 => "ResponsePending（应等待最终响应，出现最终 NRC 0x78 表明异常）",
        0x7E => "SubFunctionNotSupportedInActiveSession",
        0x7F => "ServiceNotSupportedInActiveSession",
        _ => "ECU 诊断负响应"
    };

    private void HistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not DiagnosticHistoryRow row) return;
        ResultStatus.Text = row.Status + " · " + row.Time + " · " + row.Elapsed;
        ResultStatus.Foreground = row.Status == "ECU 正响应" ? Brushes.ForestGreen :
            row.Status.StartsWith("ECU NRC", StringComparison.Ordinal) ? Brushes.DarkOrange :
            Brushes.DarkRed;
        DecodedResultBox.Text = row.Decoded;
        RecordedRequestBox.Text = row.Request;
        RecordedResponseBox.Text = row.Response;
        ResultDetailsBox.Text = row.Detail + Environment.NewLine +
            "关联 ID：" + row.CorrelationId;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingCancel?.Cancel();
        AppendAudit("CANCEL", "用户请求停止等待；不意味着 ECU 操作被取消。");
    }

    private DiagnosticWorkspaceDocument CaptureWorkspace() => new()
    {
        TxIdHex = TxIdBox.Text.Trim(), RxIdHex = RxIdBox.Text.Trim(),
        P2Ms = int.TryParse(P2Box.Text, out int p2) ? p2 : 1000,
        P2StarMs = int.TryParse(P2StarBox.Text, out int p2Star) ? p2Star : 5000,
        Actions = _favorites.ToList(), ManualSequence = [], PollIntervalMs = 2000
    };

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = _workspacePath;
            if (path is null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "UDS 诊断配置 (*.uds-workspace.json)|*.uds-workspace.json|JSON (*.json)|*.json",
                    FileName = "UdsRequests.uds-workspace.json"
                };
                if (dlg.ShowDialog(this) != true) return;
                path = dlg.FileName;
            }
            await DiagnosticWorkspace.SaveAsync(path, CaptureWorkspace());
            _workspacePath = path;
            AppendAudit("FILE", "保存诊断配置 " + path);
        }
        catch (Exception ex) { ShowError("保存配置失败", ex); }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_hardware is not null || _pendingSend is not null)
        {
            MessageBox.Show(this, "请先断开主控，再切换诊断配置。");
            return;
        }
        var dlg = new OpenFileDialog
        {
            Filter = "UDS 诊断配置 (*.uds-workspace.json)|*.uds-workspace.json|JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var doc = await DiagnosticWorkspace.LoadAsync(dlg.FileName);
            _favorites.Clear();
            foreach (var a in doc.Actions) _favorites.Add(a);
            TxIdBox.Text = doc.TxIdHex;
            RxIdBox.Text = doc.RxIdHex;
            P2Box.Text = doc.P2Ms.ToString(CultureInfo.InvariantCulture);
            P2StarBox.Text = doc.P2StarMs.ToString(CultureInfo.InvariantCulture);
            _workspacePath = dlg.FileName;
            FavoriteCombo.SelectedItem = null;
            AppendAudit("FILE", "加载诊断配置 " + dlg.FileName);
        }
        catch (Exception ex) { ShowError("打开配置失败", ex); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "诊断证据 (*.json)|*.json",
            FileName = "UDS_Trace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var evidence = new
            {
                Format = "AutoTest.UdsLab.Exchange.v2",
                ExportUtc = DateTimeOffset.UtcNow,
                DeviceIdentity = _hardware?.DeviceIdentity,
                Endpoint = new { RequestId = TxIdBox.Text, ResponseId = RxIdBox.Text, P2 = P2Box.Text, P2Star = P2StarBox.Text },
                RealExchanges = _history.ToArray(),
                Audit = _audit.ToArray(),
                UsbCanIsoTpTrace = _traceLines.ToArray(),
                Note = "USB ACK is not a final ECU response. Physical CAN frames require exact human consent."
            };
            await File.WriteAllTextAsync(dlg.FileName,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            AppendAudit("EXPORT", "导出诊断证据 " + dlg.FileName);
        }
        catch (Exception ex) { ShowError("导出失败", ex); }
    }

    private void AppendTrace(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendTrace(message)));
            return;
        }
        string line = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + message;
        if (_traceLines.Count < 20000) _traceLines.Add(line);
        TraceBox.AppendText(line + Environment.NewLine);
        if (TraceBox.Text.Length > 200000) TraceBox.Text = TraceBox.Text[^150000..];
        TraceBox.ScrollToEnd();
    }

    private void AppendAudit(string kind, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendAudit(kind, message)));
            return;
        }
        _audit.Add(new AuditRecord(DateTimeOffset.UtcNow, kind, message));
    }

    private void ShowError(string title, Exception error)
    {
        AppendAudit("ERROR", title + " " + error);
        MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            if (SendButton.IsEnabled) Send_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            Save_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing || _hardware is null) return;
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try { await DisconnectAsync(); }
        finally { Close(); }
    }
}
