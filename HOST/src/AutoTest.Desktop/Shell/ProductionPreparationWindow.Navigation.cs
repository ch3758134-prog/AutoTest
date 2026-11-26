using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One station configuration surface, in the order of the real inspection
/// process. Existing editor controls are re-used as inline sections; there
/// is no parallel "basic" model or a second publishing route.
/// </summary>
public partial class ProductionPreparationWindow
{
    private readonly Dictionary<int, Expander> _stationSections = new();
    private Expander? _detailsPanel;

    private void OpenStationDetail(int detailIndex) => OpenStationSection(
        detailIndex switch
        {
            0 => 3, // Scanner hardware and fixture
            1 => 1, // Barcode formats
            2 => 4, // Validation / diagnostics
            _ => 0
        });

    private void OpenStationSection(int index)
    {
        if (!_stationSections.TryGetValue(index, out var target)) return;
        if (_detailsPanel is not null) _detailsPanel.IsExpanded = true;
        target.IsExpanded = true;
        Dispatcher.BeginInvoke(new Action(target.BringIntoView), DispatcherPriority.Loaded);
    }

    private void ConfigureProductPreparationWorkspace()
    {
        if (PreparationTabs.Items.Count != 5 ||
            PreparationTabs.Items.Cast<object>().Any(item => item is not TabItem))
            throw new InvalidOperationException(
                "生产配置布局不符合当前工程约定，禁止丢失已有编辑器。");

        var original = PreparationTabs.Items.Cast<TabItem>().ToArray();
        PreparationTabs.Items.Clear();

        var guided = CreateGuidedOverview() as ScrollViewer
            ?? throw new InvalidOperationException("生产设置未初始化。");
        var overview = guided.Content as StackPanel
            ?? throw new InvalidOperationException("生产设置页面结构异常。");
        guided.Content = null;
        var summary = overview.Children.Cast<UIElement>().ToArray();
        overview.Children.Clear();
        if (summary.Length != 3)
            throw new InvalidOperationException("生产设置概览结构发生变化。");

        var root = new StackPanel
        {
            MaxWidth = 1090,
            Margin = new Thickness(12, 12, 12, 18)
        };

        // Three concise task summaries first. Original specialist controls
        // remain below as on-demand details, not a second basic workflow.
        foreach (var item in summary) root.Children.Add(item);
        var detailRows = new StackPanel { Margin = new Thickness(9, 11, 9, 4) };
        detailRows.Children.Add(Section(0, "产品型号与工程绑定", original[0], false));
        detailRows.Children.Add(Section(1, "编码形制规则", original[1], false));
        detailRows.Children.Add(Section(2, "编号登记（混线时使用）", original[2], false));
        detailRows.Children.Add(Section(3, "扫码设备、夹具与实体输入", original[3], false));
        detailRows.Children.Add(Section(4, "验证与诊断", original[4], false));
        _detailsPanel = new Expander
        {
            Header = "详细配置",
            IsExpanded = false,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(38, 60, 79)),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 237)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 12),
            Padding = new Thickness(6),
            Content = detailRows
        };
        root.Children.Add(_detailsPanel);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Content = root
        };
        PreparationTabs.Items.Add(new TabItem { Header = "生产设置", Content = scroll });
        PreparationTabs.SelectedIndex = 0;
    }

    private Expander Section(int index, string title, TabItem source, bool expanded)
    {
        var editor = source.Content as UIElement
            ?? throw new InvalidOperationException("配置编辑器内容为空：" + title);
        source.Content = null;
        // The outer station page scrolls; nested full-page ScrollViewers would
        // trap the mouse wheel and hide controls below the fold.
        if (editor is ScrollViewer nested && nested.Content is UIElement inner)
        {
            nested.Content = null;
            editor = inner;
        }

        var section = new Expander
        {
            Header = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(38, 60, 79)),
            IsExpanded = expanded,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 237)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(8),
            Content = editor
        };
        _stationSections[index] = section;
        return section;
    }
}
