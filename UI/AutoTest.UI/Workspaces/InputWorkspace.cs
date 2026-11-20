using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

public sealed class InputWorkspace : Grid
{
    public InputWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("输入配置", "定义外部条件名称及动作组合；KEY_SIM 内部 20 Key 不等于 20 个 ECU 功能按钮"));
        var body = UiKit.Columns(365, 0);
        Grid.SetRow(body, 1); Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(UiKit.Card(left));
        var leftTop = new StackPanel();
        DockPanel.SetDock(leftTop, Dock.Top); left.Children.Add(leftTop);
        leftTop.Children.Add(UiKit.Text("电阻网络开关命名", 16, true));
        leftTop.Children.Add(UiKit.Note("开关是电阻网络内部开关；真实 ECU 功能及识别区间必须依据不同型号的独立配置。"));
        var keys = UiKit.Table(state.Keys, ("编号", "Index", 70), ("显示名称", "Name", 0));
        keys.Columns[0].IsReadOnly = true;
        keys.CellEditEnding += (_, _) => changed();
        left.Children.Add(keys);
        var right = new DockPanel();
        Grid.SetColumn(right, 1); body.Children.Add(UiKit.Card(right));
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
        top.Children.Add(UiKit.Text("组合动作表", 16, true));
        top.Children.Add(UiKit.Note("每项动作只有按键组合、保持时间和名称等 UI 数据；可保存会话和连续修改。"));
        var list = UiKit.Table(state.Inputs,
            ("动作名称", "Name", 200), ("20 Key 组合（5 位 HEX）", "KeyMask", 210),
            ("保持时间 ms", "HoldMs", 120), ("备注", "Note", 0));
        list.CellEditEnding += (_, _) => changed();
        top.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 新建动作", () => { state.Inputs.Add(new UiInputAction()); changed(); }, true),
            UiKit.Button("从当前按键组合添加", () =>
            {
                state.Inputs.Add(new UiInputAction
                { Name = "当前组合", KeyMask = CanWorkspace.Mask(state), HoldMs = 500 });
                changed();
            }),
            UiKit.Button("删除动作", () =>
            {
                if (list.SelectedItem is UiInputAction a) { state.Inputs.Remove(a); changed(); }
            })));
        right.Children.Add(list);
    }
}
