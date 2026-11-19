using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Rebuild;

public sealed class ProductionWorkspace : UserControl
{
    private readonly UiStudyStore _store;
    private readonly Action<string> _notify;
    private readonly TextBlock _captureStatus = UiVisual.Text("", 14, true);
    public ProductionWorkspace(UiStudyStore store, Action<string> notify)
    {
        _store = store; _notify = notify;
        var layout = UiVisual.Columns(new GridLength(1, GridUnitType.Star), new GridLength(390));
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("扫码生产工作", 18, true));
        left.Children.Add(UiVisual.Note("输入来自扫码枪或键盘。暂不接入正式编号目录、身份匹配与自动测试执行。"));
        var serial = UiVisual.Input(store.Current.ScanInput);
        serial.FontSize = 21;
        serial.Height = 50;
        serial.Margin = new Thickness(0, 22, 0, 10);
        left.Children.Add(UiVisual.Field("编号输入", serial));
        serial.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Capture(serial.Text); e.Handled = true; } };
        left.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("记录本次 UI 输入", () => Capture(serial.Text), true),
            UiVisual.Button("清空", () => { serial.Clear(); _captureStatus.Text = "输入已清空"; })
        ));
        _captureStatus.Margin = new Thickness(0, 15, 0, 12);
        left.Children.Add(_captureStatus);
        left.Children.Add(UiVisual.Note("真实自动识别、重复判定、程序绑定、预检与运行结果都不能根据 UI 输入凭空生成。"));
        UiVisual.At(layout, UiVisual.Box(left, new Thickness(25)), 0);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("任务与工位", 16, true));
        right.Children.Add(UiVisual.Rule());
        UiVisual.AddRow(right, "测试项目", "待正式绑定");
        UiVisual.AddRow(right, "工位状态", "未连接 / 未验证");
        UiVisual.AddRow(right, "当前 DUT", "未识别");
        UiVisual.AddRow(right, "运行权限", "不可用");
        UiVisual.AddRow(right, "运行结果", "无真实执行记录");
        right.Children.Add(UiVisual.Rule());
        var start = UiVisual.Button("自动执行正式测试", () => { }, true,
            "尚未接入真实扫描匹配、预检与 TEST 执行服务");
        start.IsEnabled = false;
        right.Children.Add(start);
        UiVisual.At(layout, UiVisual.Box(right, new Thickness(22)), 1);
        _captureStatus.Text = store.Current.ScanInput.Length == 0 ? "等待扫码输入" : "仅保留了 UI 输入草稿";
    }
    private void Capture(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { _captureStatus.Text = "请输入编号"; return; }
        _store.Current.ScanInput = text.Trim();
        _store.Save();
        _captureStatus.Text = "UI 已记录编号输入；正式身份匹配尚不可用";
        _notify("扫码输入仅作为 UI 草稿，尚未执行任何测试");
    }
}

public sealed class RecordsWorkspace : UserControl
{
    public RecordsWorkspace(UiStudyStore store)
    {
        var layout = UiVisual.Columns(new GridLength(300), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("运行记录筛选", 17, true));
        left.Children.Add(UiVisual.Field("编号 / 关键词", UiVisual.Input()));
        var from = UiVisual.Input(); var to = UiVisual.Input();
        left.Children.Add(UiVisual.Field("起始日期", from));
        left.Children.Add(UiVisual.Field("截止日期", to));
        var status = UiVisual.Combo();
        status.ItemsSource = new[] { "全部", "通过", "失败", "中止", "错误", "无法判定" };
        status.SelectedIndex = 0;
        left.Children.Add(UiVisual.Field("运行结果", status));
        var info = UiVisual.Note("没有已接入的正式 RunRecord，筛选不生成示例记录。");
        left.Children.Add(UiVisual.Button("查询记录", () => info.Text =
            "当前记录库未接线。查询条件已输入，但没有真实结果。", true));
        info.Margin = new Thickness(0, 16, 0, 0);
        left.Children.Add(info);
        UiVisual.At(layout, UiVisual.Box(left), 0);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("历史运行与证据", 17, true));
        right.Children.Add(UiVisual.Note("未来从运行编号定位测试用例、步骤、关联 CAN/UDS/波形证据与报告。"));
        right.Children.Add(UiVisual.Empty("尚无正式历史记录", "UI 当前不读取记录库，不会展示虚假的 PASS、FAIL、测试时长或报表。"));
        UiVisual.At(layout, UiVisual.Box(right), 1);
        Content = layout;
    }
}

public sealed class DeviceWorkspace : UserControl
{
    public DeviceWorkspace()
    {
        var stack = UiVisual.Stack();
        stack.Children.Add(UiVisual.Text("工位连接与硬件能力", 18, true));
        stack.Children.Add(UiVisual.Note("当前为纯 UI 状态设计，没有 USB 会话，也不声称识别到实体主控板。"));
        var grid = UiVisual.Columns(UiVisual.Star(), UiVisual.Star());
        var device = UiVisual.Stack();
        device.Children.Add(UiVisual.Text("主控板连接", 16, true));
        device.Children.Add(UiVisual.Rule());
        UiVisual.AddRow(device, "连接状态", "未连接（UI 阶段）");
        UiVisual.AddRow(device, "设备身份", "未读取");
        UiVisual.AddRow(device, "通信代次", "未知");
        UiVisual.AddRow(device, "最近请求/ACK", "尚无数据");
        device.Children.Add(UiVisual.Toolbar(
            UiToolBuilders.Disabled("连接 USB", "纯 UI 无设备驱动"),
            UiToolBuilders.Disabled("断开 USB", "没有已建立连接")));
        UiVisual.At(grid, UiVisual.Box(device), 0);
        var routes = UiVisual.Stack();
        routes.Children.Add(UiVisual.Text("逻辑通信与外部输出", 16, true));
        routes.Children.Add(UiVisual.Rule());
        UiVisual.AddRow(routes, "DUT CAN", "未连接");
        UiVisual.AddRow(routes, "KEY_SIM CAN", "未连接");
        UiVisual.AddRow(routes, "IGN 与夹具", "物理状态未知");
        UiVisual.AddRow(routes, "20 Key / 5 路电压", "未采集");
        UiVisual.AddRow(routes, "LED1–LED6", "无实际确认");
        routes.Children.Add(UiVisual.Note("USB 请求、主控 ACK、ECU 响应和外部物理结果应分级显示，不能用一种成功颜色合并。"));
        UiVisual.At(grid, UiVisual.Box(routes), 1);
        grid.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(grid);
        Content = UiVisual.Scroll(stack);
    }
}
