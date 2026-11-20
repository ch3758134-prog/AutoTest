using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Integrated stimulation and DUT observation, never EPB-specific logical buttons.</summary>
public sealed class BenchWorkspace : Grid
{
    public BenchWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("综合手动台架", "外部条件激励、操作序列和 DUT 响应观察同处一个持续工作环境"));
        var area = UiKit.Columns(0, 360);
        Grid.SetRow(area, 1); Children.Add(area);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        area.Children.Add(UiKit.Card(left, new Thickness(14)));
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        top.Children.Add(UiKit.Note("UI 只编辑按键组合、保持时间和动作队列。不计算未经标定的模拟电压，也不向实体主控发送指令。"));
        top.Children.Add(UiKit.Text("20 Key 电阻组合状态", 16, true));
        var mask = UiKit.Text("组合掩码：" + CanWorkspace.Mask(state), 13, true, UiKit.Accent);
        top.Children.Add(mask);
        var toggleGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 12, 0, 13) };
        top.Children.Add(toggleGrid);
        foreach (var key in state.Keys)
        {
            var toggle = new ToggleButton
            {
                Content = key.Name,
                IsChecked = key.Pressed, MinHeight = 42,
                Margin = new Thickness(3), Background = UiKit.White,
                BorderBrush = UiKit.Outline
            };
            toggle.Checked += (_, _) => { key.Pressed = true; changed(); mask.Text = "组合掩码：" + CanWorkspace.Mask(state); };
            toggle.Unchecked += (_, _) => { key.Pressed = false; changed(); mask.Text = "组合掩码：" + CanWorkspace.Mask(state); };
            toggleGrid.Children.Add(toggle);
        }
        var duration = UiKit.Input("500");
        top.Children.Add(UiKit.Field("保持时间（ms）", duration));
        top.Children.Add(UiKit.Actions(
            UiKit.Button("保存组合为动作", () =>
            {
                if (!int.TryParse(duration.Text, out var ms) || ms < 0) { status("保持时间必须是非负整数。"); return; }
                state.Inputs.Add(new UiInputAction { Name = "按键动作", KeyMask = CanWorkspace.Mask(state), HoldMs = ms });
                changed(); status("已将当前 20 Key 组合和保持时间加入本地 UI 动作列表。");
            }, true),
            UiKit.Button("清除选择", () =>
            {
                foreach (var k in state.Keys) k.Pressed = false;
                foreach (var tb in toggleGrid.Children.OfType<ToggleButton>()) tb.IsChecked = false;
                mask.Text = "组合掩码：00000"; changed();
            })));
        top.Children.Add(UiKit.Text("模拟输出", 15, true));
        top.Children.Add(UiKit.Text("5 路电压输出取决于按键板实际电阻网络；未做真实电压标定时不显示虚构电压。", 12, false, UiKit.Muted));
        top.Children.Add(UiKit.Actions(
            MakeDisabled("执行当前组合", "未连接真实 KEY_SIM 输出服务"),
            MakeDisabled("IGN ON / OFF", "未连接真实 IGN 控制服务"),
            MakeDisabled("夹具压紧 / 释放", "未连接真实夹具控制服务")));
        var actionTable = UiKit.Table(state.Inputs,
            ("动作名称", "Name", 160), ("20 Key 组合 HEX", "KeyMask", 135),
            ("保持 ms", "HoldMs", 105), ("说明", "Note", 0));
        actionTable.CellEditEnding += (_, _) => changed();
        var actionTools = UiKit.Actions(
            UiKit.Button("删除动作", () =>
            {
                if (actionTable.SelectedItem is UiInputAction action)
                { state.Inputs.Remove(action); changed(); }
            }),
            UiKit.Button("复制动作", () =>
            {
                if (actionTable.SelectedItem is not UiInputAction action) return;
                state.Inputs.Add(new UiInputAction
                { Name = action.Name + " 副本", KeyMask = action.KeyMask, HoldMs = action.HoldMs, Note = action.Note });
                changed();
            }));
        DockPanel.SetDock(actionTools, Dock.Bottom); left.Children.Add(actionTools);
        left.Children.Add(actionTable);

        var right = new StackPanel();
        Grid.SetColumn(right, 1); area.Children.Add(UiKit.Card(right));
        right.Children.Add(UiKit.Text("DUT 观察", 17, true));
        right.Children.Add(UiKit.Note("发送请求、主控 ACK、ECU 响应和物理状态必须分开展示。当前没有任何真实事件。"));
        var tabs = new TabControl { Margin = new Thickness(0, 10, 0, 12) };
        foreach (var tab in new[] { "CAN 原始帧", "DBC 信号", "操作时间线", "异常" })
            tabs.Items.Add(new TabItem { Header = tab,
                Content = UiKit.Empty("暂无" + tab, "尚未连接设备和实际观察源。") });
        right.Children.Add(tabs);
        right.Children.Add(UiKit.Text("工位状态：未连接 / 未知", 12, true));
        right.Children.Add(UiKit.Text("没有物理采样时，不能凭界面组合推断实际电压或动作完成。", 12, false, UiKit.Muted));
    }
    private static Button MakeDisabled(string title, string reason)
    {
        var b = UiKit.Button(title, () => { });
        UiKit.Disabled(b, reason);
        return b;
    }
}
