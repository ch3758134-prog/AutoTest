using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Bench;

public sealed record BenchDbcPickerRow(DbcMessage Message, string Name, string IdText,
    string Detail);

/// <summary>
/// Chooses a message from the live PROJECT DBC catalog; the selected definition
/// is revalidated against the current project/revision before being added.
/// This window does not open CAN/USB or transmit anything.
/// </summary>
public partial class BenchDbcMessagePickerWindow : Window
{
    private readonly ICollectionView _visibleRows;
    public DbcMessage? PickedMessage { get; private set; }

    public BenchDbcMessagePickerWindow(IEnumerable<DbcMessage> messages, string dbcName)
    {
        InitializeComponent();
        ProjectDbcName.Text = $"工程 DBC：{dbcName} · 双击报文即可加入发送列表";
        var rows = new ObservableCollection<BenchDbcPickerRow>(
            messages.OrderBy(m => m.Id).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new BenchDbcPickerRow(m, m.Name,
                    $"0x{m.Id:X} / {(m.IsExtended ? "扩展帧" : "标准帧")}",
                    $"DLC {m.Dlc} · {m.Signals.Length} 个信号 · 发送节点：{m.Transmitter}")));
        MessageList.ItemsSource = rows;
        _visibleRows = CollectionViewSource.GetDefaultView(rows);
        _visibleRows.Filter = MatchesFilter;
        if (rows.Count > 0) MessageList.SelectedIndex = 0;
        Loaded += (_, _) => FilterBox.Focus();
    }

    private bool MatchesFilter(object candidate)
    {
        if (candidate is not BenchDbcPickerRow row) return false;
        var filter = FilterBox.Text.Trim();
        return filter.Length == 0 ||
            row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.IdText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.Message.Id.ToString("X").Contains(filter.TrimStart('0', 'x', 'X'),
                StringComparison.OrdinalIgnoreCase);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_visibleRows is null) return; // XAML initializes TextBox before constructor body
        _visibleRows.Refresh();
        if (MessageList.Items.Count > 0) MessageList.SelectedIndex = 0;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (MessageList.SelectedItem is not BenchDbcPickerRow row)
        {
            MessageBox.Show(this, "请选择一条 DBC 报文。", "添加发送报文",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        PickedMessage = row.Message;
        DialogResult = true;
    }

    private void MessageList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Confirm_Click(sender, e);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
