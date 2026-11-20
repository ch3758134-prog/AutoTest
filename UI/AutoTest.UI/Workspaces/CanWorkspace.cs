using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

public sealed class CanWorkspace : Grid
{
    public CanWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("CAN 工作台", "左：多报文发送编辑 · 中：外部输入快捷操作 · 右：CAN 接收监视"));
        var body = UiKit.Columns(0, 252, 390);
        Grid.SetRow(body, 1); Children.Add(body);
        var tx = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(UiKit.Card(tx));
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top); tx.Children.Add(top);
        top.Children.Add(UiKit.Text("发送列表", 17, true));
        top.Children.Add(UiKit.Note("多行 CAN 报文、数据和周期可以直接编辑。发送按钮在未接入主控时禁用。"));
        var table = UiKit.Table(state.CanTx,
            ("名称", "Name", 130), ("CAN ID", "CanId", 95),
            ("数据 HEX", "Data", 245), ("周期 ms", "PeriodMs", 82), ("状态", "State", 100));
        table.Columns.Insert(0, new DataGridCheckBoxColumn
        {
            Header = "选择",
            Binding = new System.Windows.Data.Binding(nameof(UiCanMessage.Enabled))
            { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = new DataGridLength(55)
        });
        table.CellEditEnding += (_, _) => changed();
        var sendOne = UiKit.Button("发送选中", () => { }, true);
        var start = UiKit.Button("启动选中周期", () => { });
        var stop = UiKit.Button("停止发送", () => { });
        foreach (var b in new[] { sendOne, start, stop })
            UiKit.Disabled(b, "未连接真实 CAN 发送服务。UI 不生成虚构主控 ACK。");
        top.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 报文", () => { state.CanTx.Add(new UiCanMessage()); changed(); }, true),
            UiKit.Button("从 DBC 添加", () =>
            {
                if (state.Messages.Count == 0) { status("请先在 DBC 工具添加报文。"); return; }
                var picker = new Window { Title = "选择 DBC 报文", Width = 420, Height = 380,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var content = new DockPanel { Margin = new Thickness(12) };
                var list = new ListBox { ItemsSource = state.Messages, DisplayMemberPath = nameof(UiDbcMessage.Name) };
                var add = UiKit.Button("加入发送列表", () =>
                {
                    if (list.SelectedItem is not UiDbcMessage msg) return;
                    state.CanTx.Add(new UiCanMessage { Name = msg.Name, CanId = msg.CanId });
                    changed(); picker.Close();
                }, true);
                DockPanel.SetDock(add, Dock.Bottom);
                content.Children.Add(add); content.Children.Add(list); picker.Content = content;
                picker.ShowDialog();
            }),
            UiKit.Button("复制", () =>
            {
                if (table.SelectedItem is not UiCanMessage msg) return;
                state.CanTx.Add(new UiCanMessage
                { Name = msg.Name + " 副本", CanId = msg.CanId, Data = msg.Data, PeriodMs = msg.PeriodMs });
                changed();
            }),
            UiKit.Button("移除", () =>
            {
                if (table.SelectedItem is UiCanMessage msg) { state.CanTx.Remove(msg); changed(); }
            }),
            UiKit.Button("设为周期草稿", () =>
            {
                if (table.SelectedItem is not UiCanMessage msg) return;
                if (msg.PeriodMs <= 0) msg.PeriodMs = 100;
                msg.Enabled = true; table.Items.Refresh(); changed();
                status("已设置待发周期草稿，未实际发送 CAN。");
            })));
        top.Children.Add(UiKit.Actions(sendOne, start, stop));
        tx.Children.Add(table);

        var keys = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(keys, 1);
        body.Children.Add(UiKit.Card(UiKit.Scroll(keys)));
        keys.Children.Add(UiKit.Text("外部输入", 17, true));
        keys.Children.Add(UiKit.Note("选择按键组合只是本地 UI 编辑。KEY_SIM 输出须由真实设备服务执行。"));
        var mask = UiKit.Text("按键组合：未选择", 12, true, UiKit.Accent);
        keys.Children.Add(mask);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 10) };
        keys.Children.Add(grid);
        foreach (var key in state.Keys)
        {
            var toggle = new CheckBox { Content = key.Name, IsChecked = key.Pressed,
                Margin = new Thickness(4, 6, 0, 6), ToolTip = "仅更改 UI 预选状态" };
            toggle.Checked += (_, _) => { key.Pressed = true; changed(); mask.Text = "按键组合：" + Mask(state); };
            toggle.Unchecked += (_, _) => { key.Pressed = false; changed(); mask.Text = "按键组合：" + Mask(state); };
            grid.Children.Add(toggle);
        }
        keys.Children.Add(UiKit.Text("辅助操作", 14, true));
        foreach (var label in new[] { "IGN ON", "IGN OFF", "夹具压紧", "夹具释放" })
        {
            var b = UiKit.Button(label, () => { });
            UiKit.Disabled(b, "该按钮只展示 UI 控制位置，没有连接物理控制服务");
            keys.Children.Add(b);
        }

        var rx = new StackPanel();
        Grid.SetColumn(rx, 2);
        body.Children.Add(UiKit.Card(rx));
        rx.Children.Add(UiKit.Text("CAN 接收监视", 17, true));
        var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 12) };
        tabs.Items.Add(new TabItem { Header = "原始帧", Content = UiKit.Empty("暂无 CAN 数据", "没有真实 CAN 接收源，不显示虚构时间戳和报文。") });
        tabs.Items.Add(new TabItem { Header = "DBC 解码", Content = UiKit.Empty("暂无信号采样", "真实帧到达后才能呈现解析值、采样质量与时间。") });
        rx.Children.Add(tabs);
        rx.Children.Add(UiKit.Text("总线：DUT CAN · A1/A12", 12, false, UiKit.Muted));
        rx.Children.Add(UiKit.Text("按键板总线：B1/B11", 12, false, UiKit.Muted));
        rx.Children.Add(UiKit.Text("过滤、暂停显示、导出日志将在真实监视接线后生效。", 11, false, UiKit.Muted));
    }
    internal static string Mask(UiSession state)
    {
        var bits = 0;
        foreach (var k in state.Keys.Where(k => k.Pressed && k.Index is >= 1 and <= 20))
            bits |= 1 << (k.Index - 1);
        return bits.ToString("X5");
    }
}
