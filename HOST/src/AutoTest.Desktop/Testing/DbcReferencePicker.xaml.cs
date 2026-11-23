using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.Testing.Authoring;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// One reusable, search-first DBC resource selector for TEST authoring.
/// Searching never changes the StepNode or its edited CAN DATA; only a
/// deliberate result click or Enter raises ChoiceCommitted.
/// </summary>
public partial class DbcReferencePicker : UserControl
{
    public sealed record Option(MappingChoice? Choice, string Title, string Detail,
        string KindLabel, string SearchText, string FullDetail,
        ManualBenchViewModel.DbcChoice? BenchChoice = null);

    private readonly List<Option> _all = [];
    private MappingChoice? _current;
    private ManualBenchViewModel.DbcChoice? _benchCurrent;
    private bool _benchMode;
    private bool _changing;

    public event EventHandler<DbcReferenceChosenEventArgs>? ChoiceCommitted;
    public event EventHandler<BenchDbcReferenceChosenEventArgs>? BenchChoiceCommitted;
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
            _benchMode = false;
            _benchCurrent = null;
            _current = selected;
            RenderSelection();
            ApplyFilter();
        }
        finally { _changing = false; }
    }

    // The Bench and TEST editors deliberately share the same selection
    // component and visual interaction; only their resource contracts differ.
    public void SetBenchChoices(
        IEnumerable<ManualBenchViewModel.DbcChoice> source,
        ManualBenchViewModel.DbcChoice? selected)
    {
        _changing = true;
        try
        {
            _all.Clear();
            _all.AddRange(source.Select(ToBenchOption));
            _benchMode = true;
            _current = null;
            _benchCurrent = selected;
            RenderSelection();
            ApplyFilter();
        }
        finally { _changing = false; }
    }

    private static Option ToBenchOption(ManualBenchViewModel.DbcChoice choice)
    {
        var message = choice.Message;
        var title = choice.DisplayNames.MessageLabel(message);
        var id = "0x" + message.ArbitrationId.ToString("X", CultureInfo.InvariantCulture);
        var detail = id + "  ·  " + (message.IsExtended ? "扩展帧" : "标准帧") +
            "  ·  DLC " + message.Dlc + "  ·  " + choice.AssetName +
            "  r" + choice.Revision;
        var source = title + " " + message.Name + " " + detail + " " +
            choice.AssetName + " " +
            message.ArbitrationId.ToString(CultureInfo.InvariantCulture) + " " +
            choice.Purpose;
        return new Option(null, title, detail, "CAN 报文",
            source, title + "\n" + detail + "\n" + choice.Purpose, choice);
    }

    public void OpenWithSearch(string query = "")
    {
        if (!IsEnabled) return;
        ChoicePanel.Visibility = Visibility.Visible;
        SearchBox.Text = query;
        ApplyFilter();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void RenderSelection()
    {
        Option? info = _benchMode
            ? _benchCurrent is null ? null : ToBenchOption(_benchCurrent)
            : _current is null ? null : ToOption(_current);
        if (info is null)
        {
            CurrentTitle.Text = _benchMode
                ? "CAN DATA 字节直编" : "选择 DBC 资源";
            CurrentDetail.Text = _benchMode
                ? "可选 DBC 信号辅助；原始字节始终可编辑"
                : "点击搜索名称、中文别名、信号或 CAN ID";
            SelectionButton.ToolTip = CurrentDetail.Text;
            return;
        }
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
            title = "按字节发送 CAN";
            kind = "直编模式";
            detail = "DBC 辅助可选 · CAN ID 与 DATA 直接可用";
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
            detail = formattedId + " · DLC " + (message?.Dlc.ToString(CultureInfo.InvariantCulture) ?? "?") +
                " · " + frameType + " · r" + mapping.AssetRevision;
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
        if (ChoicePanel.Visibility == Visibility.Visible)
            ChoicePanel.Visibility = Visibility.Collapsed;
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
            ChoicePanel.Visibility = Visibility.Collapsed;
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
        ChoicePanel.Visibility = Visibility.Collapsed;
        if (_benchMode)
        {
            if (option.BenchChoice is not { } bench ||
                Equals(_benchCurrent, bench)) return;
            _benchCurrent = bench;
            RenderSelection();
            BenchChoiceCommitted?.Invoke(this,
                new BenchDbcReferenceChosenEventArgs(bench));
            return;
        }
        if (option.Choice is not { } choice ||
            _current?.Mapping == choice.Mapping) return;
        _current = choice;
        RenderSelection();
        ChoiceCommitted?.Invoke(this, new DbcReferenceChosenEventArgs(choice));
    }
}

public sealed class DbcReferenceChosenEventArgs : EventArgs
{
    public MappingChoice Choice { get; }
    public DbcReferenceChosenEventArgs(MappingChoice choice) => Choice = choice;
}
public sealed class BenchDbcReferenceChosenEventArgs : EventArgs
{
    public ManualBenchViewModel.DbcChoice Choice { get; }
    public BenchDbcReferenceChosenEventArgs(ManualBenchViewModel.DbcChoice choice)
        => Choice = choice;
}
