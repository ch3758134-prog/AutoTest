using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AutoTest.Desktop.Rebuild;

public sealed class UiKeySelector : UserControl
{
    private uint _mask;
    private readonly TextBlock _caption = UiVisual.Text("", 12, true, UiVisual.Accent);
    private readonly List<ToggleButton> _buttons = [];
    public uint Mask => _mask;
    public string MaskHex => _mask.ToString("X5", CultureInfo.InvariantCulture);
    public event Action? Changed;
    public UiKeySelector()
    {
        var s = UiVisual.Stack();
        s.Children.Add(UiVisual.Text("20 路电阻网络开关组合", 15, true));
        s.Children.Add(UiVisual.Note("每个 K 编号代表内部电阻网络开关，不代表 ECU 的 20 个独立功能。可同时闭合并保持。"));
        var grid = new UniformGrid { Columns = 5, Rows = 4, Margin = new Thickness(0, 13, 0, 9) };
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            var b = new ToggleButton
            {
                Content = "K" + (i + 1).ToString("00"),
                Margin = new Thickness(3), Height = 41,
                BorderBrush = UiVisual.Line,
                Background = UiVisual.Surface,
                ToolTip = "切换电阻网络开关 K" + (i + 1).ToString("00")
            };
            b.Checked += (_, _) => Toggle(index, true);
            b.Unchecked += (_, _) => Toggle(index, false);
            _buttons.Add(b);
            grid.Children.Add(b);
        }
        s.Children.Add(grid);
        var bottom = UiVisual.Toolbar();
        bottom.Children.Add(_caption);
        bottom.Children.Add(UiVisual.Button("全部释放", Reset));
        s.Children.Add(bottom);
        Content = UiVisual.Box(s, new Thickness(12));
        RenderMask();
    }
    private void Toggle(int index, bool on)
    {
        if (on) _mask |= 1u << index; else _mask &= ~(1u << index);
        RenderMask();
        Changed?.Invoke();
    }
    private void RenderMask() => _caption.Text = "当前组合：0x" + MaskHex + "    ";
    public void Reset()
    {
        foreach (var b in _buttons) b.IsChecked = false;
        _mask = 0;
        RenderMask();
        Changed?.Invoke();
    }
    public void SetMask(string hex)
    {
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)) mask = 0;
        mask &= 0xFFFFF;
        for (int i = 0; i < 20; i++) _buttons[i].IsChecked = (mask & (1u << i)) != 0;
        _mask = mask;
        RenderMask();
    }
}

public static partial class UiToolBuilders
{
    public static UIElement Dbc(UiStudyStore store, Action<string> notify)
    {
        void Save() { store.Save(); notify("DBC UI 草稿已更新"); }
        var layout = UiVisual.Columns(new GridLength(310), new GridLength(5), UiVisual.Star());
        var sidebar = UiVisual.Stack();
        sidebar.Children.Add(UiVisual.Text("报文列表", 16, true));
        var filter = UiVisual.Input();
        filter.ToolTip = "按 ID 或报文名搜索";
        sidebar.Children.Add(filter);
        var list = new ListBox { BorderThickness = new Thickness(0), MinHeight = 260,
            DisplayMemberPath = "Name" };
        void Refresh()
        {
            list.ItemsSource = store.Current.DbcMessages
                .Where(x => (x.Name + " " + x.CanId).Contains(filter.Text,
                    StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
        filter.TextChanged += (_, _) => Refresh();
        sidebar.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 报文", () =>
            {
                var item = new UiDbcMessage();
                store.Current.DbcMessages.Add(item);
                Save(); Refresh(); list.SelectedItem = item;
            }, true),
            UiVisual.Button("删除", () =>
            {
                if (list.SelectedItem is not UiDbcMessage msg) return;
                if (!UiDialogs.Confirm(Window.GetWindow(layout)!, "删除 UI 报文", "确认删除当前 UI 草稿报文及其信号？")) return;
                store.Current.DbcMessages.Remove(msg);
                Save(); Refresh();
            })
        ));
        sidebar.Children.Add(UiVisual.Scroll(list));
        UiVisual.At(layout, UiVisual.Box(sidebar), 0);
        UiVisual.At(layout, new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, Background = UiVisual.Line }, 1);
        var detailHost = new ContentControl();
        UiVisual.At(layout, detailHost, 2);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not UiDbcMessage msg)
            { detailHost.Content = UiVisual.Empty("选择 DBC 报文", "新建报文并在右侧配置 CAN ID 与信号定义。"); return; }
            var detail = UiVisual.Stack();
            detail.Children.Add(UiVisual.Text("报文属性与信号", 18, true));
            var head = UiVisual.Stack(true);
            var name = UiVisual.Input(msg.Name, 210);
            var id = UiVisual.Input(msg.CanId, 125);
            var len = UiVisual.Input(msg.Length.ToString(), 75);
            var cycle = UiVisual.Input(msg.CycleMs.ToString(), 95);
            head.Children.Add(UiVisual.Field("报文名称", name));
            head.Children.Add(UiVisual.Field("CAN ID", id));
            head.Children.Add(UiVisual.Field("DLC", len));
            head.Children.Add(UiVisual.Field("周期 ms", cycle));
            detail.Children.Add(head);
            DataGrid? signals = null;
            detail.Children.Add(UiVisual.Toolbar(
                UiVisual.Button("应用报文属性", () =>
                {
                    if (!int.TryParse(len.Text, out int n) || n < 0 || n > 64)
                    { notify("DLC 必须为 0–64"); return; }
                    if (!int.TryParse(cycle.Text, out int c) || c < 0)
                    { notify("周期必须为非负整数"); return; }
                    msg.Name = name.Text.Trim(); msg.CanId = id.Text.Trim(); msg.Length = n; msg.CycleMs = c;
                    Save(); Refresh();
                }, true),
                UiVisual.Button("＋ 新建信号", () =>
                {
                    msg.Signals.Add(new UiDbcSignal()); Save();
                    list.SelectedItem = null; list.SelectedItem = msg;
                }),
                UiVisual.Button("删除选中信号", () =>
                {
                    if (signals?.SelectedItem is not UiDbcSignal signal) return;
                    msg.Signals.Remove(signal); Save();
                    list.SelectedItem = null; list.SelectedItem = msg;
                })
            ));
            signals = GridWith(msg.Signals, Save,
                TextColumn("Name","信号名",165), TextColumn("Alias","中文别名",150),
                TextColumn("StartBit","起始位",80), TextColumn("Length","位长度",78),
                TextColumn("ByteOrder","字节序",100), TextColumn("Factor","系数",82),
                TextColumn("Offset","偏移",82), TextColumn("Unit","单位",80),
                TextColumn("Comment","注释",180));
            signals.Height = 460;
            detail.Children.Add(UiVisual.Section("信号定义", signals,
                "所有编辑均为 UI 草稿。真实 DBC 导入、编码校验和 ECU 解析由后续业务集成提供。"));
            detailHost.Content = UiVisual.Scroll(detail);
        };
        Refresh();
        detailHost.Content = UiVisual.Empty("DBC 工作台", "左侧选择报文；新建后可直接编辑其信号。当前无假 DBC 内容。");
        return layout;
    }

    public static UIElement Can(UiStudyStore store, Action<string> notify)
    {
        void Save() { store.Save(); notify("已修改 CAN 待发草稿；未向任何总线发送"); }
        var layout = UiVisual.Columns(new GridLength(490), new GridLength(330), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("发送清单", 16, true));
        left.Children.Add(UiVisual.Note("可同时配置多条单次/周期报文。勾选只修改 UI 草稿，不是实际开始发送。"));
        var tx = GridWith(store.Current.CanMessages, Save,
            CheckColumn("Enabled","启用",58), TextColumn("Name","名称",145),
            TextColumn("CanId","报文 ID",100), TextColumn("HexData","HEX 数据",220),
            TextColumn("CycleMs","周期 ms",85), TextColumn("DbcName","DBC 报文",140));
        tx.Height = 390;
        void Refresh() { tx.ItemsSource = null; tx.ItemsSource = store.Current.CanMessages; }
        left.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 添加报文", () => { store.Current.CanMessages.Add(new UiCanMessage()); Save(); Refresh(); }, true),
            UiVisual.Button("使用 DBC 报文", () =>
            {
                if (store.Current.DbcMessages.Count == 0)
                { notify("还没有 DBC 报文，请先打开 DBC 工作台创建。"); return; }
                var message = store.Current.DbcMessages[0];
                store.Current.CanMessages.Add(new UiCanMessage { Name = message.Name, CanId = message.CanId,
                    DbcName = message.Name, HexData = string.Join(" ", Enumerable.Repeat("00", message.Length)) });
                Save(); Refresh();
            }),
            UiVisual.Button("删除选中", () =>
            {
                if (tx.SelectedItem is not UiCanMessage item) return;
                store.Current.CanMessages.Remove(item); Save(); Refresh();
            })
        ));
        left.Children.Add(tx);
        left.Children.Add(UiVisual.Toolbar(
            Disabled("发送选中", "纯 UI 阶段未接入 CAN 发送服务"),
            Disabled("开始周期发送", "纯 UI 阶段未接入 CAN 发送服务"),
            Disabled("停止发送", "没有真实发送中的报文")
        ));
        left.Children.Add(UiVisual.Note("发送中的真实状态、统计和 ACK 只可由后端连接后提供。"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left), new Thickness(15)), 0);
        var middle = UiVisual.Stack();
        middle.Children.Add(UiVisual.Text("工位快捷操作", 16, true));
        middle.Children.Add(UiVisual.Note("本区域只编辑输入组合，不输出到 DUT。"));
        var selector = new UiKeySelector();
        selector.Margin = new Thickness(0, 13, 0, 10);
        middle.Children.Add(selector);
        middle.Children.Add(Disabled("应用至真实输入", "未接入工位硬件，不能输出电压"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(middle), new Thickness(12)), 1);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("CAN 实时监视", 16, true));
        right.Children.Add(UiVisual.Note("实时日志来自真实通路。未连接时不填充演示报文。"));
        right.Children.Add(UiVisual.Empty("尚无 CAN 帧", "请在完成硬件接线后查看时间戳、方向、ID、数据与丢帧统计。"));
        UiVisual.At(layout, UiVisual.Box(right, new Thickness(14)), 2);
        return layout;
    }

    public static UIElement Bench(UiStudyStore store, Action<string> notify)
    {
        void Save() { store.Save(); notify("已更新综合台架 UI 动作草稿"); }
        var layout = UiVisual.Columns(new GridLength(330), new GridLength(430), UiVisual.Star());
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("CAN 激励配置", 16, true));
        left.Children.Add(UiVisual.Note("面向目标 ECU 的多报文待发清单。实际发送需要后续服务。"));
        var tx = GridWith(store.Current.CanMessages, Save,
            CheckColumn("Enabled","启用",55), TextColumn("CanId","ID",95),
            TextColumn("HexData","数据",170), TextColumn("CycleMs","周期",75));
        tx.Height = 290;
        left.Children.Add(UiVisual.Button("＋ 添加待发报文", () =>
        { store.Current.CanMessages.Add(new UiCanMessage()); Save(); tx.ItemsSource = null; tx.ItemsSource = store.Current.CanMessages; }, true));
        left.Children.Add(tx);
        left.Children.Add(Disabled("发送到 DUT", "不连接真实 CAN 服务"));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(left), new Thickness(12)), 0);

        var mid = UiVisual.Stack();
        mid.Children.Add(UiVisual.Text("外部输入与动作序列", 16, true));
        mid.Children.Add(UiVisual.Note("按键为电阻组合。IGN 与夹具属于独立物理操作，本阶段不可执行。"));
        var keys = new UiKeySelector();
        keys.Margin = new Thickness(0, 10, 0, 10);
        mid.Children.Add(keys);
        var hold = UiVisual.Input("500", 100);
        DataGrid? list = null;
        var row = UiVisual.Stack(true);
        row.Children.Add(UiVisual.Field("保持时长 ms", hold));
        row.Children.Add(UiVisual.Button("＋ 追加输入动作", () =>
        {
            if (!int.TryParse(hold.Text, out var ms) || ms < 0) { notify("保持时长不能为负数"); return; }
            store.Current.KeySequences.Add(new UiKeySequence { MaskHex = keys.MaskHex, HoldMs = ms,
                Name = "按键组合 " + (store.Current.KeySequences.Count + 1) });
            Save();
            list.ItemsSource = null; list.ItemsSource = store.Current.KeySequences;
        }, true));
        mid.Children.Add(row);
        list = GridWith(store.Current.KeySequences, Save,
            TextColumn("Name","动作",140), TextColumn("MaskHex","20 Key 掩码",105),
            TextColumn("HoldMs","保持 ms",90));
        list.Height = 170;
        mid.Children.Add(list);
        mid.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("删除动作", () =>
            {
                if (list?.SelectedItem is not UiKeySequence action) return;
                store.Current.KeySequences.Remove(action); Save();
                list.ItemsSource = null; list.ItemsSource = store.Current.KeySequences;
            }),
            Disabled("IGN 切换", "未接入真实 IGN 硬件控制"),
            Disabled("夹具动作", "未接入真实夹具控制")
        ));
        UiVisual.At(layout, UiVisual.Box(UiVisual.Scroll(mid), new Thickness(12)), 1);
        var right = UiVisual.Stack();
        right.Children.Add(UiVisual.Text("DUT 响应观察", 16, true));
        right.Children.Add(UiVisual.Note("此处显示真实 CAN 响应与操作时序，当前尚无数据。"));
        right.Children.Add(UiVisual.Empty("等待真实采集", "不能用界面操作草稿冒充 ECU 回应或物理执行完成。"));
        UiVisual.At(layout, UiVisual.Box(right, new Thickness(12)), 2);
        return layout;
    }
}
