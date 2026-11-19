using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>
/// A pinned result is an observation of real completed diagnostic exchanges.
/// Pinning does NOT schedule any CAN request and never synthesizes ECU values.
/// </summary>
public sealed class PinnedDiagnosticValue : INotifyPropertyChanged
{
    private string _display = "";
    private string _updatedAt = "";

    public string Label { get; }
    public string RequestHex { get; }
    public string Display
    {
        get => _display;
        set { if (_display == value) return; _display = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display))); }
    }
    public string UpdatedAt
    {
        get => _updatedAt;
        set { if (_updatedAt == value) return; _updatedAt = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdatedAt))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PinnedDiagnosticValue(string label, string requestHex, string display, string updatedAt)
    { Label = label; RequestHex = requestHex; _display = display; _updatedAt = updatedAt; }
}

public partial class MainWindow
{
    private readonly ObservableCollection<PinnedDiagnosticValue> _pinnedValues = [];
    private DiagnosticAction? _scratchAction;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void InitializeConsole()
    {
        WatchList.ItemsSource = _pinnedValues;
        HistoryCounter.Text = "0 条交互";
        RefreshActionsEmptyState();
        EmptyHistoryText.Visibility = Visibility.Visible;
        _scratchAction = new DiagnosticAction
        {
            Name = "临时诊断请求",
            Group = "即时交互",
            RawMode = false,
            ServiceSidHex = "22"
        };
        _editing = _scratchAction;
        LoadEditor();
        ComposeCurrent();
        ConnectionSettingsExpander.IsExpanded = false;
        AdvancedEditExpander.IsExpanded = true;
        EditorContextStatus.Text = "当前：临时请求 · 无需保存即可反复发送";
        RefreshSendButton();
    }

    private void RefreshActionsEmptyState()
    {
        ActionsEmptyHint.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuickNew_Click(object sender, RoutedEventArgs e)
    {
        ActionsList.SelectedItem = null;
        _scratchAction = new DiagnosticAction
        {
            Name = "临时诊断请求", Group = "即时交互",
            RawMode = false, ServiceSidHex = "22"
        };
        _editing = _scratchAction;
        LoadEditor();
        ComposeCurrent();
        AdvancedEditExpander.IsExpanded = true;
        Audit("QUICK_NEW", "已切换到未保存的即时诊断请求。");
    }

    private void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        UpdateActionFromEditor();
        try
        {
            if (string.IsNullOrWhiteSpace(_editing.RequestHex))
                throw new FormatException("当前请求为空，请选择服务或填写 UDS PDU。");
            _ = UdsHex.Parse(_editing.RequestHex);
            if (_actions.Contains(_editing))
            {
                ActionsList.Items.Refresh();
                Audit("ACTION_EDIT", "当前常用操作已更新：" + _editing.Name);
                return;
            }
            if (_actions.Count >= 500)
                throw new InvalidOperationException("常用诊断操作最多 500 项。");
            var saved = _editing.Duplicate();
            string readableName = (ServiceCombo.SelectedItem as UdsOperationTemplate)?.Name ?? "自定义诊断";
            if (_editing == _scratchAction || string.IsNullOrWhiteSpace(saved.Name) ||
                saved.Name.StartsWith("临时诊断请求", StringComparison.Ordinal))
                saved.Name = readableName + " · " +
                    (string.IsNullOrWhiteSpace(saved.PrimaryHex)
                    ? saved.RequestHex.Trim()
                    : saved.PrimaryHex.Trim());
            if (saved.Group == "即时交互") saved.Group =
                (ServiceCombo.SelectedItem as UdsOperationTemplate)?.Group ?? "自定义";
            // Keep the newly saved action visible even when the user had filtered the list.
            OperationSearchBox.Text = "";
            _actions.Add(saved);
            ActionsList.SelectedItem = saved;
            ActionsList.ScrollIntoView(saved);
            RefreshActionsEmptyState();
            Audit("ACTION_SAVE", "保存常用诊断操作：" + saved.Name +
                "（仍需点击顶部保存配置才能持久写入 JSON）。");
        }
        catch (Exception ex) { ShowFailure("保存操作失败", ex); }
    }

    private void RunSavedAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DiagnosticAction action })
            return;
        if (ActionsList.SelectedItem != action) ActionsList.SelectedItem = action;
        else { _editing = action; LoadEditor(); }
        Send_Click(sender, e);
    }

    private static DiagnosticAction? ContextAction(object sender)
    {
        if (sender is not MenuItem menu) return null;
        var context = menu.Parent as ContextMenu ??
            ItemsControl.ItemsControlFromItemContainer(menu) as ContextMenu;
        return context?.PlacementTarget is FrameworkElement target
            ? target.DataContext as DiagnosticAction : null;
    }

    private void MenuRunAction_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAction(sender) is not DiagnosticAction action) return;
        ActionsList.SelectedItem = action;
        _editing = action;
        LoadEditor();
        Send_Click(sender, e);
    }

    private void MenuEditAction_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAction(sender) is not DiagnosticAction action) return;
        ActionsList.SelectedItem = action;
        _editing = action;
        LoadEditor();
        AdvancedEditExpander.IsExpanded = true;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void MenuCopyAction_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAction(sender) is not DiagnosticAction action) return;
        ActionsList.SelectedItem = action;
        _editing = action;
        Copy_Click(sender, e);
    }

    private void MenuDeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAction(sender) is not DiagnosticAction action) return;
        ActionsList.SelectedItem = action;
        _editing = action;
        Delete_Click(sender, e);
    }

    private void OperationSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (ActionsList?.ItemsSource is null) return;
        string term = OperationSearchBox.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(_actions);
        view.Filter = item =>
        {
            if (item is not DiagnosticAction action) return false;
            return term.Length == 0 ||
                action.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                action.Group.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                action.RequestHex.Contains(term, StringComparison.OrdinalIgnoreCase);
        };
        view.Refresh();
    }

    private void PinSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ExchangeList.SelectedItem is not DiagnosticExchangeRow row)
        {
            MessageBox.Show(this, "请先在中间的交互记录中选中一条真实 ECU 响应。");
            return;
        }
        if (!row.Outcome.StartsWith("收到 ECU 正响应", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "只能固定已取得有效正响应的诊断数据。负响应、超时或未知结果不能当作有效观察值。");
            return;
        }
        var previous = _pinnedValues.FirstOrDefault(v =>
            v.RequestHex == row.RequestHex && v.Label == row.ActionName);
        string display = !string.IsNullOrWhiteSpace(row.Parsed) ? row.Parsed : row.ResponseHex;
        if (previous is null)
        {
            if (_pinnedValues.Count >= 30)
            {
                MessageBox.Show(this, "最多固定 30 个诊断观察项。");
                return;
            }
            _pinnedValues.Add(new PinnedDiagnosticValue(row.ActionName, row.RequestHex,
                display, "采集时间 " + row.LocalTime + " · 实际 ECU 响应"));
        }
        else
        {
            previous.Display = display;
            previous.UpdatedAt = "采集时间 " + row.LocalTime + " · 实际 ECU 响应";
        }
        Audit("PIN", "固定实际响应 " + row.ActionName + " " + row.RequestHex);
    }

    private void UnpinSelected_Click(object sender, RoutedEventArgs e)
    {
        if (WatchList.SelectedItem is PinnedDiagnosticValue item)
            _pinnedValues.Remove(item);
    }

    private void UpdatePinnedFromExchange(DiagnosticExchangeRow row)
    {
        if (!row.Outcome.StartsWith("收到 ECU 正响应", StringComparison.Ordinal))
            return;
        foreach (var item in _pinnedValues.Where(v =>
                     v.RequestHex == row.RequestHex && v.Label == row.ActionName))
        {
            item.Display = !string.IsNullOrWhiteSpace(row.Parsed) ? row.Parsed : row.ResponseHex;
            item.UpdatedAt = "采集时间 " + row.LocalTime + " · 实际 ECU 响应";
        }
    }

    private void RefreshExchangeConsole(DiagnosticExchangeRow row)
    {
        EmptyHistoryText.Visibility = _exchangeHistory.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryCounter.Text = _exchangeHistory.Count.ToString(CultureInfo.InvariantCulture) + " 条交互";
        UpdatePinnedFromExchange(row);
    }

    private void ReuseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ExchangeList.SelectedItem is not DiagnosticExchangeRow row) return;
        try
        {
            var raw = UdsHex.Parse(row.RequestHex);
            if (raw.Length < 1) throw new FormatException("选中的历史请求为空。");
            var template = UdsOperationTemplates.Find(raw[0]);
            ActionsList.SelectedItem = null;
            _scratchAction = new DiagnosticAction
            {
                Name = row.ActionName + " · 复用",
                Group = "即时交互",
                RequestHex = UdsHex.Format(raw),
                ServiceSidHex = template?.Sid.ToString("X2") ?? "",
                RawMode = template is null,
                Decoder = template?.Sid == 0x22 && raw.Length == 3 && raw[1] == 0xF1 && raw[2] == 0x90
                    ? "Ascii" : "Hex"
            };
            _editing = _scratchAction;
            LoadEditor();
            AdvancedEditExpander.IsExpanded = true;
            Audit("REUSE", "已将真实交互记录回填到即时请求，未发送任何 ECU 帧：" + row.RequestHex);
        }
        catch (Exception ex) { ShowFailure("回填请求失败", ex); }
    }
}
