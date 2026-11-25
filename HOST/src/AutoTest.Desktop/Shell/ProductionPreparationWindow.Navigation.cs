using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// D1 product workspace alignment: the four top-level preparation pages are
/// defined by the engineer's tasks, not by the legacy scanner/TEST classes.
/// Actual editors and publish services are REUSED; no production recipe added.
/// </summary>
public partial class ProductionPreparationWindow
{
    private void ConfigureProductPreparationWorkspace()
    {
        // Existing independent editors are preserved so no binding or real
        // scanner command is replaced by a fake layout. The three identity
        // sub-editors are grouped under one production-identity responsibility.
        if (PreparationTabs.Items.Count != 5 ||
            PreparationTabs.Items.Cast<object>().Any(x => x is not TabItem))
            throw new InvalidOperationException("生产准备界面阶段结构已变化；必须重新检查四页工作区映射。");

        var original = PreparationTabs.Items.Cast<TabItem>().ToArray();
        PreparationTabs.Items.Clear();

        original[0].Header = "产品型号与唯一工程";
        original[1].Header = "编码形制";
        original[2].Header = "唯一编号与身份";
        var identityEditor = new TabControl
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 240)),
            Margin = new Thickness(7, 7, 7, 7)
        };
        foreach (var tab in original.Take(3))
            identityEditor.Items.Add(tab);

        PreparationTabs.Items.Add(new TabItem
        {
            Header = "1  产品识别",
            Content = identityEditor
        });
        original[3].Header = "2  工位设备";
        PreparationTabs.Items.Add(original[3]);
        PreparationTabs.Items.Add(new TabItem
        {
            Header = "3  生产工艺",
            Content = CreateStationProcessOverview()
        });
        original[4].Header = "4  发布与验证";
        PreparationTabs.Items.Add(original[4]);
        PreparationTabs.SelectedIndex = 0;
    }

    private UIElement CreateStationProcessOverview()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(21),
            MaxWidth = 1050
        };
        content.Children.Add(new TextBlock
        {
            Text = "标准生产过程",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 7)
        });
        content.Children.Add(new TextBlock
        {
            Text = "工位物理设备、产品工艺与 TEST 功能步骤职责分离。以下是应当验证的生产过程合同，不是当前工位已经具备安全放行条件。",
            Foreground = new SolidColorBrush(Color.FromRgb(95, 113, 132)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15)
        });

        var stages = new[]
        {
            ("01", "放入 ECU · 扫码识别",
                "唯一编号 → 已发布型号及硬件适用性 → 唯一 ECU 工程；扫码本身不触发任意电磁阀。"),
            ("02", "受控工装压紧",
                "确认已批准的机械防护/互锁许可，发送夹具控制命令；USB ACK 不等于机械到位。"),
            ("03", "等待真实实体 START",
                "物理输入必须来自已核验的固件事件；PC 不能以虚拟按钮冒充实体启动。"),
            ("04", "原生 FLASH 与 APP 后验",
                "每次生产检测和复检都执行当前已发布工艺；没有 ECU 后验时不能进入 TEST。"),
            ("05", "完整有序 TEST 项目",
                "项目准备建立的周期 CAN 在全部调用实例间保持；用例各自拥有判定、证据和 Cleanup。"),
            ("06", "归档、异常及受控释放",
                "先归档真实 Inspection 事实；在已验证释放条件下处理夹具。故障 UNKNOWN 不等于安全。")
        };
        foreach (var (number, title, explanation) in stages)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = number,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 104, 174)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 8, 4, 0)
            };
            var box = new StackPanel
            {
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 0)
            };
            box.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 53, 73))
            });
            box.Children.Add(new TextBlock
            {
                Text = explanation,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(91, 111, 132)),
                Margin = new Thickness(0, 5, 0, 0)
            });
            Grid.SetColumn(label, 0);
            Grid.SetColumn(box, 1);
            line.Children.Add(label);
            line.Children.Add(box);
            content.Children.Add(line);
        }

        var evidence = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 249, 237)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(233, 207, 161)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 12)
        };
        var blocked = new TextBlock { TextWrapping = TextWrapping.Wrap };
        blocked.SetBinding(TextBlock.TextProperty, new Binding(nameof(StationBlockerSummary)));
        evidence.Child = blocked;
        content.Children.Add(evidence);

        var open = new Button
        {
            Content = "打开工程特殊工位动作编辑器",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(13, 8, 13, 8),
            ToolTip = "编辑现有真实工位特殊动作；不能用它替代标准工位机械互锁和实体输入。",
            Margin = new Thickness(0, 4, 0, 0),
            IsEnabled = _openSpecialProcedure is not null
        };
        open.Click += (_, _) => _openSpecialProcedure?.Invoke();
        content.Children.Add(open);
        content.Children.Add(new TextBlock
        {
            Text = "当前标准阶段不可用自由动作列表重排。设备输入与夹具机械许可没有真正接线前，正式生产始终阻断。",
            Foreground = new SolidColorBrush(Color.FromRgb(112, 81, 48)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }
}
