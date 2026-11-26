using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Task-first production preparation navigation. Preserve the exact existing
/// editor controls as secondary views instead of duplicating their models or
/// teaching first-time operators the underlying rule-table schema.
/// </summary>
public partial class ProductionPreparationWindow
{
    private void ConfigureProductPreparationWorkspace()
    {
        if (PreparationTabs.Items.Count != 5 ||
            PreparationTabs.Items.Cast<object>().Any(item => item is not TabItem))
            throw new InvalidOperationException(
                "生产准备原生编辑器布局改变，禁止默默丢失配置页或发布入口。");

        var original = PreparationTabs.Items.Cast<TabItem>().ToArray();
        PreparationTabs.Items.Clear();

        original[0].Header = "产品与工程绑定";
        original[1].Header = "编号解析规则";
        original[2].Header = "登记表匹配";
        var advanced = new TabControl
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 240)),
            Margin = new Thickness(7)
        };
        foreach (var item in original.Take(3))
            advanced.Items.Add(item);
        advanced.SelectedIndex = 0;

        PreparationTabs.Items.Add(new TabItem
        {
            Header = "1  交付准备",
            Content = CreateGuidedOverview()
        });
        original[3].Header = "2  扫码与工位设备";
        PreparationTabs.Items.Add(original[3]);
        PreparationTabs.Items.Add(new TabItem
        {
            Header = "3  高级识别配置",
            Content = advanced
        });
        original[4].Header = "4  发布详情与批量核验";
        PreparationTabs.Items.Add(original[4]);
        PreparationTabs.SelectedIndex = 0;
    }
}
