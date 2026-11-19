using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.Desktop.Rebuild;

public sealed record ToolInfo(string Key, string Name, string Icon, string Description);

public static class ToolCatalog
{
    public static readonly ToolInfo[] Items =
    [
        new("dbc", "DBC 工作台", "▣", "报文、信号、位定义、别名与编辑"),
        new("can", "CAN 调试", "⇄", "多条报文、周期配置与接收监视"),
        new("bench", "综合手动台架", "⌘", "CAN、外部输入组合与 DUT 观察"),
        new("inputs", "输入配置", "▤", "20 路电阻开关、5 路输出与动作时序"),
        new("uds", "UDS 诊断", "⌥", "诊断服务、请求序列和响应时间线"),
        new("flash", "ECU 烧录", "⇧", "固件、Profile、阶段与风险反馈"),
        new("wave", "波形分析", "〽", "信号、时间轴、游标与参考"),
        new("device", "设备诊断", "⌁", "USB、逻辑通路、状态与错误"),
        new("records", "记录与报告", "◷", "历史运行、过滤、结果与证据")
    ];
}

public sealed class ToolWindow : Window
{
    public ToolWindow(string key, UiStudyStore store, Action<string> notify)
    {
        var info = ToolCatalog.Items.First(x => x.Key == key);
        Title = "AutoTest · " + info.Name + " · 纯 UI";
        Width = 1320; Height = 830; MinWidth = 850; MinHeight = 550;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = UiVisual.Back;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(81) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
        var h = UiVisual.Stack();
        h.Margin = new Thickness(22, 12, 18, 0);
        h.Children.Add(UiVisual.Text(info.Name, 21, true));
        var note = UiVisual.Note(info.Description + "   ·   UI 草稿操作，不连接硬件");
        note.Margin = new Thickness(0, 4, 0, 0);
        h.Children.Add(note);
        var header = new Border { Background = System.Windows.Media.Brushes.White, Child = h,
            BorderBrush = UiVisual.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        UiVisual.At(grid, header, 0, 0);
        var view = key switch
        {
            "dbc" => UiToolBuilders.Dbc(store, notify),
            "can" => UiToolBuilders.Can(store, notify),
            "bench" => UiToolBuilders.Bench(store, notify),
            "inputs" => UiToolBuilders.Inputs(store, notify),
            "uds" => UiToolBuilders.Uds(store, notify),
            "flash" => UiToolBuilders.Flash(store, notify),
            "wave" => UiToolBuilders.Wave(store, notify),
            "device" => new DeviceWorkspace().Content as UIElement ?? UiVisual.Empty("设备诊断","当前设备未接入"),
            "records" => new RecordsWorkspace(store).Content as UIElement ?? UiVisual.Empty("记录","尚无数据"),
            _ => UiVisual.Empty("工具不可用", "工具键无效")
        };
        var content = new ContentControl { Content = view, Margin = new Thickness(16, 12, 16, 14) };
        UiVisual.At(grid, content, 0, 1);
        var status = new Border { Background = System.Windows.Media.Brushes.White,
            Child = UiVisual.Text("纯 UI 体验   ·   所有实体输出与正式历史操作均未接线", 11, false, UiVisual.Muted),
            Padding = new Thickness(16, 5, 0, 0) };
        UiVisual.At(grid, status, 0, 2);
        Content = grid;
        Closing += (_, _) =>
        {
            try { store.Save(); }
            catch (Exception error) { notify("工具 UI 草稿保存失败：" + error.Message); }
        };
    }
}

public static partial class UiToolBuilders
{
    public static System.Windows.Controls.DataGridColumn TextColumn(string field, string title, double width = 110)
        => new DataGridTextColumn { Header = title,
            Binding = new System.Windows.Data.Binding(field) { Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(width) };
    public static System.Windows.Controls.DataGridColumn CheckColumn(string field, string title, double width = 60)
        => new DataGridCheckBoxColumn { Header = title,
            Binding = new System.Windows.Data.Binding(field) { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = new DataGridLength(width) };
    public static DataGrid GridWith(object items, Action save, params DataGridColumn[] columns)
    {
        var g = UiVisual.Table(items);
        g.AutoGenerateColumns = false;
        foreach (var column in columns) g.Columns.Add(column);
        g.CellEditEnding += (_, _) => g.Dispatcher.BeginInvoke(
            new Action(save), System.Windows.Threading.DispatcherPriority.Background);
        return g;
    }
    public static Button Disabled(string label, string reason)
    {
        var b = UiVisual.Button(label, () => { }, true, reason);
        b.IsEnabled = false; b.ToolTip = reason;
        return b;
    }
    public static StackPanel TopTitle(string title, string note)
    {
        var s = UiVisual.Stack();
        s.Children.Add(UiVisual.Text(title, 15, true));
        var label = UiVisual.Note(note);
        label.Margin = new Thickness(0, 7, 0, 9);
        s.Children.Add(label);
        return s;
    }
}
