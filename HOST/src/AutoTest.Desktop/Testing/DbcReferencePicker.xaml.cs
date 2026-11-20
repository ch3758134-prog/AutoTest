using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// One reusable, search-first DBC resource selector for TEST authoring.
/// Searching never changes the StepNode or its edited CAN DATA; only a
/// deliberate result click or Enter raises ChoiceCommitted.
/// </summary>
public partial class DbcReferencePicker : UserControl
{
    public sealed record Option(MappingChoice Choice, string Title, string Detail,
        string KindLabel, string SearchText, string FullDetail);

    private readonly List<Option> _all = [];
    private MappingChoice? _current;
    private bool _changing;

    public event EventHandler<DbcReferenceChosenEventArgs>? ChoiceCommitted;
    public MappingChoice? SelectedChoice => _current;

    public DbcReferencePicker()
    {
        InitializeComponent();
        ResultsList.ItemsSource = Array.Empty<Option>();
    }

    public void SetChoices(IEnumerable<MappingChoice> source, MappingChoice? selected)
    {
        _changing = true;
        try
        {
            _all.Clear();
            _all.AddRange(source.Select(ToOption));
            _current = selected;
            RenderSelection();
            ApplyFilter();
        }
        finally { _changing = false; }
    }

    public void OpenWithSearch(string query = "")
    {
        if (!IsEnabled) return;
        ChoicePopup.IsOpen = true;
        SearchBox.Text = query;
        ApplyFilter();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void RenderSelection()
    {
        if (_current is null)
        {
            CurrentTitle.Text = "选择 DBC 资源";
            CurrentDetail.Text = "点击搜索名称、中文别名、信号或 CAN ID";
            SelectionButton.ToolTip = CurrentDetail.Text;
            return;
        }
        var info = ToOption(_current);
        CurrentTitle.Text = info.Title;
        CurrentDetail.Text = info.Detail;
        SelectionButton.ToolTip = info.FullDetail;
    }

    private static Option ToOption(MappingChoice choice)
    {
        var mapping = choice.Mapping;
        var message = choice.Message;
        var formattedId = "0x" + mapping.CanId.ToString("X", CultureInfo.InvariantCulture);
        var frameType = mapping.Extended ? "扩展帧" : "标准帧";
        var resourceName = mapping.Name.Split('.')[0];
        string title;
        string kind;
        string detail;
        if (mapping.Kind == TestTargetKind.RawCan)
        {
            title = "不绑定 DBC · 原始 CAN";
            kind = "原始报文";
            detail = "直接编辑 CAN ID、DLC 与 DATA";
        }
        else if (mapping.Kind == TestTargetKind.Signal)
        {
            var signal = message?.Signals.FirstOrDefault(x => x.Name == mapping.SignalName);
            title = signal is not null && choice.DisplayNames is not null
                ? choice.DisplayNames.SignalLabel(message!, signal)
                : mapping.SignalName ?? mapping.Name.Split('.').Last();
            var messageName = message is null ? mapping.Name :
                choice.DisplayNames?.MessageLabel(message) ?? message.Name;
            kind = message is null ? "引用待核对" : "信号";
            detail = messageName + "  ·  " + formattedId + "  ·  " +
                (mapping.Unit?.Length > 0 ? mapping.Unit + "  ·  " : "") +
                resourceName + " r" + mapping.AssetRevision;
        }
        else if (mapping.Kind == TestTargetKind.CanMessage)
        {
            title = message is null
                ? mapping.Name.Split('.').Last()
                : choice.DisplayNames?.MessageLabel(message) ?? message.Name;
            kind = message is null ? "引用待核对" : "CAN 报文";
            detail = formattedId + "  ·  " + frameType + "  ·  " +
                resourceName + " r" + mapping.AssetRevision;
        }
        else
        {
            title = mapping.Name;
            kind = "资源";
            detail = choice.Label;
        }
        var full = title + "\n" + detail + "\n" + mapping.Name + "\n" + choice.Label;
        var search = string.Join(" ", new[]
        {
            title, detail, mapping.Name, choice.Label, message?.Name ?? "",
            mapping.SignalName ?? "", formattedId, mapping.CanId.ToString("X",
                CultureInfo.InvariantCulture),
            mapping.CanId.ToString(CultureInfo.InvariantCulture)
        });
        return new Option(choice, title, detail, kind, search, full);
    }

    private void ApplyFilter()
    {
        if (SearchBox is null || ResultsList is null) return;
        var query = SearchBox.Text.Trim();
        var options = query.Length == 0 ? _all :
            _all.Where(o => o.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        ResultsList.ItemsSource = options;
        ResultCount.Text = options.Count == 0
            ? "没有匹配结果 · 不会更改当前步骤"
            : "找到 " + options.Count + " 项 · 点击确认选择";
        SearchPlaceholder.Visibility = query.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChoicePopup.IsOpen) ChoicePopup.IsOpen = false;
        else OpenWithSearch();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_changing) ApplyFilter();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ChoicePopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Min(ResultsList.Items.Count - 1,
                ResultsList.SelectedIndex + 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is Option option) Commit(option);
            else if (ResultsList.Items.Count == 1 &&
                     ResultsList.Items[0] is Option single) Commit(single);
            e.Handled = true;
        }
    }

    private void Result_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: Option option })
        {
            Commit(option);
            e.Handled = true;
        }
    }

    private void Commit(Option option)
    {
        ChoicePopup.IsOpen = false;
        if (_current?.Mapping == option.Choice.Mapping) return;
        _current = option.Choice;
        RenderSelection();
        ChoiceCommitted?.Invoke(this, new DbcReferenceChosenEventArgs(option.Choice));
    }
}

public sealed class DbcReferenceChosenEventArgs : EventArgs
{
    public MappingChoice Choice { get; }
    public DbcReferenceChosenEventArgs(MappingChoice choice) => Choice = choice;
}