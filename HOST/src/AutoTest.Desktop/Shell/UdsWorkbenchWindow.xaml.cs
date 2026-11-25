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
/// Independent, offline WPF editor of the existing UDS ToolDocument. File
/// replay consumes only provided records and cannot transmit CAN, claim a live
/// ECU response or publish a TEST/production verdict.
/// </summary>
public partial class UdsWorkbenchWindow : Window
{
    public ObservableCollection<UdsServiceEditRow> ServiceRows { get; } = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(4) };
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

    public UdsWorkbenchWindow()
    {
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
        Closed += (_, _) => _autosave.Stop();
        ServiceRows.CollectionChanged += (_, _) => { MarkDirty(); UpdateReplayAvailability(); };
    }

    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        var compact = ActualWidth < 1120 || ActualHeight < 690;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        // Endpoint/P2 fields remain one explicit expand-away action in
        // compact layouts; the active service table gets the available height.
        UdsProfileExpander.IsExpanded = !compact;
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
    private void Profile_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void AddRow(UdsServiceEditRow row)
    {
        row.PropertyChanged += (_, _) => MarkDirty();
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
        if (_busy || !ConfirmReplaceDraft()) return;
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
        if (_busy || !ConfirmReplaceDraft()) return;
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
            var loaded = await UdsToolDocumentStore.LoadAsync(source);
            var bytes = await File.ReadAllBytesAsync(source);
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
                _loadedSha = Hash(bytes);
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
            var verified = await UdsToolDocumentStore.LoadAsync(absolute);
            if (verified.Services.Count != document.Services.Count)
                throw new InvalidDataException("UDS 序列保存后回读不一致。");
            _loadedSha = Hash(await File.ReadAllBytesAsync(absolute));
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
        copy.PropertyChanged += (_, _) => MarkDirty();
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
    private void ServiceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateReplayAvailability();

    private async void LoadReplay_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
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
    private void ClearReplay_Click(object sender, RoutedEventArgs e) => ClearReplay();
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
        if (_busy || _closing) return false;
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
