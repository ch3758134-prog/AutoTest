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
            ?? throw new InvalidOperationException("生产配置总览未初始化。");
        var oldRoot = guided.Content as StackPanel
            ?? throw new InvalidOperationException("生产配置总览结构异常。");
        guided.Content = null;
        var summary = oldRoot.Children.Cast<UIElement>().ToArray();
        oldRoot.Children.Clear();
        if (summary.Length != 6)
            throw new InvalidOperationException("生产配置总览内容发生变化，拒绝丢弃配置。");

        var root = new StackPanel
        {
            MaxWidth = 1080,
            Margin = new Thickness(18, 15, 18, 24)
        };

        // The first two items are the old duplicate title and introduction.
        // The window has one title and one persistent save/status strip already.
        root.Children.Add(summary[2]); // Current ECU and full TEST/FLASH
        root.Children.Add(Section(0, "产品与 ECU 工程", original[0], false));

        root.Children.Add(summary[3]); // Scanner observation / connection
        root.Children.Add(Section(1, "编号格式与识别规则", original[1], false));
        root.Children.Add(Section(2, "多型号编号登记（仅混线需要）", original[2], false));

        root.Children.Add(summary[4]); // Clamp process and actual device facts
        root.Children.Add(Section(3, "工位硬件、扫码枪、夹具与实体输入", original[3], false));

        root.Children.Add(Section(4, "扫码试验与版本完整性检查", original[4], false));
        root.Children.Add(summary[5]); // The ONLY visible "apply to station" action

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
