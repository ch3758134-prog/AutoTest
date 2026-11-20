using System.Windows;
using System.Windows.Controls;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

public sealed class DeviceWorkspace : Grid
{
    public DeviceWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("设备与工位诊断", "主控身份、USB 链路、CAN 通路与请求 / ACK / ECU 响应 / UNKNOWN"));
        var body = UiKit.Columns(355, 0);
        Grid.SetRow(body, 1); Children.Add(body);
        var left = new StackPanel { Margin = new Thickness(0, 0, 11, 0) };
        body.Children.Add(UiKit.Card(UiKit.Scroll(left)));
        left.Children.Add(UiKit.Text("工位身份", 17, true));
        left.Children.Add(UiKit.Note("没有接入真实设备连接服务；主控、DUT 和硬件输出状态均不能由 UI 推测。", true));
        foreach (var (title, value) in new (string, string)[]
        { ("USB 主控", "未连接"), ("设备身份", "UNKNOWN"), ("工位所有权", "未获取"),
          ("DUT 总线", "外部 A1 / A12"), ("按键板总线", "外部 B1 / B11"),
          ("输出状态", "UNKNOWN"), ("最近 ACK", "无") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 9, 0, 9) };
            var valueText = UiKit.Text(value, 13, true);
            DockPanel.SetDock(valueText, Dock.Right); row.Children.Add(valueText);
            row.Children.Add(UiKit.Text(title, 12, false, UiKit.Muted));
            left.Children.Add(row);
        }
        var connect = UiKit.Button("连接主控", () => { }, true);
        var disconnect = UiKit.Button("断开主控", () => { });
        UiKit.Disabled(connect, "未接 DEVICE/USB 连接服务");
        UiKit.Disabled(disconnect, "没有真实主控连接");
        left.Children.Add(UiKit.Actions(connect, disconnect));
        var right = new StackPanel();
        Grid.SetColumn(right, 1); body.Children.Add(UiKit.Card(right));
        right.Children.Add(UiKit.Text("诊断时间线", 17, true));
        var filter = UiKit.Input();
        right.Children.Add(UiKit.Field("搜索请求、错误码或设备身份", filter));
        right.Children.Add(UiKit.Empty("暂无真实设备事件",
            "连接成功、请求、主控 ACK、超时、断连和 ECU 总线事件应按实际时间显示，不能用界面状态替代。"));
        right.Children.Add(UiKit.Text("事件丢失、通道异常与日志导出需使用真实设备日志证据。", 12, false, UiKit.Muted));
    }
}
