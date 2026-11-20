using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Message, signal and bit preview editing; no DBC parser is claimed.</summary>
public sealed class DbcWorkspace : Grid
{
    private readonly UiSession _state;
    private readonly Action _changed;
    private readonly Action<string> _status;
    private readonly ListBox _messages = new();
    private readonly DataGrid _signals = new();
    private readonly StackPanel _detail = new();
    private readonly UniformGrid _bits = new() { Columns = 8, Rows = 8 };
    private UiDbcMessage? _active;
    private readonly TextBox _name = UiKit.Input();
    private readonly TextBox _id = UiKit.Input();
    private readonly TextBox _dlc = UiKit.Input();
    private readonly TextBox _note = UiKit.Input("", true);
    public DbcWorkspace(UiSession state, Action changed, Action<string> status)
    {
        _state = state; _changed = changed; _status = status;
        Margin = new Thickness(18);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("DBC 工作台", "报文、信号、中文别名与位视图一体化编辑 · 本版本编辑 UI 草稿"));
        var split = UiKit.Columns(250, 0, 285);
        Grid.SetRow(split, 1); Children.Add(split);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        split.Children.Add(UiKit.Card(left, new Thickness(12)));
        var lt = new StackPanel();
        DockPanel.SetDock(lt, Dock.Top); left.Children.Add(lt);
        lt.Children.Add(UiKit.Text("报文", 16, true));
        lt.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 报文", () =>
            {
                var msg = new UiDbcMessage();
                state.Messages.Add(msg); _messages.SelectedItem = msg; changed();
            }, true),
            UiKit.Button("删除", () =>
            {
                if (_messages.SelectedItem is UiDbcMessage msg) { state.Messages.Remove(msg); changed(); }
            })));
        var search = UiKit.Input(); search.ToolTip = "按报文名和 CAN ID 检索";
        lt.Children.Add(UiKit.Field("查找报文", search));
        _messages.ItemsSource = state.Messages;
        _messages.BorderThickness = new Thickness(0);
        _messages.DisplayMemberPath = nameof(UiDbcMessage.Name);
        _messages.SelectionChanged += (_, _) => SwitchMessage(_messages.SelectedItem as UiDbcMessage);
        left.Children.Add(_messages);
        search.TextChanged += (_, _) =>
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(state.Messages);
            var q = search.Text.Trim();
            view.Filter = x => x is UiDbcMessage m &&
                (m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 m.CanId.Contains(q, StringComparison.OrdinalIgnoreCase));
        };

        var mid = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(mid, 1); split.Children.Add(UiKit.Card(mid, new Thickness(13)));
        var mt = new StackPanel();
        DockPanel.SetDock(mt, Dock.Top); mid.Children.Add(mt);
        mt.Children.Add(UiKit.Text("信号列表", 16, true));
        mt.Children.Add(UiKit.Note("单元格可直接修改，报文及信号编辑不要求反复打开属性弹窗。位图仅作 UI 交互预览，非正式 DBC 语义校验。"));
        mt.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 信号", () =>
            {
                if (_active is null) { status("先选择或新建报文。"); return; }
                _active.Signals.Add(new UiDbcSignal()); changed();
            }, true),
            UiKit.Button("删除信号", () =>
            {
                if (_active is null || _signals.SelectedItem is not UiDbcSignal s) return;
                _active.Signals.Remove(s); changed(); DrawBits();
            })));
        _signals.AutoGenerateColumns = false;
        _signals.CanUserAddRows = false; _signals.CanUserDeleteRows = false;
        _signals.Background = UiKit.White; _signals.BorderBrush = UiKit.Outline;
        _signals.RowHeight = 33; _signals.EnableRowVirtualization = true;
        foreach (var (title, prop, width) in new (string, string, double)[]
        { ("信号名", "Name", 130), ("中文别名", "Alias", 130), ("起始位", "StartBit", 72),
          ("位长", "Length", 65), ("字节序", "Endian", 80), ("系数", "Factor", 65),
          ("偏移", "Offset", 65), ("单位", "Unit", 70), ("备注", "Comment", 130) })
        {
            _signals.Columns.Add(new DataGridTextColumn
            {
                Header = title, Binding = new System.Windows.Data.Binding(prop)
                { Mode = System.Windows.Data.BindingMode.TwoWay },
                Width = new DataGridLength(width)
            });
        }
        _signals.CellEditEnding += (_, _) => { changed(); Dispatcher.BeginInvoke(new Action(DrawBits)); };
        _signals.SelectionChanged += (_, _) => DrawBits();
        mid.Children.Add(_signals);

        var right = new StackPanel();
        Grid.SetColumn(right, 2); split.Children.Add(UiKit.Card(UiKit.Scroll(right)));
        right.Children.Add(UiKit.Text("报文定义", 16, true));
        right.Children.Add(UiKit.Field("报文名称", _name));
        right.Children.Add(UiKit.Field("CAN ID", _id));
        right.Children.Add(UiKit.Field("DLC", _dlc));
        right.Children.Add(UiKit.Field("说明", _note));
        right.Children.Add(UiKit.Button("应用报文修改", ApplyMessage, true));
        right.Children.Add(UiKit.Text("64 位布局预览", 14, true));
        right.Children.Add(UiKit.Text("高亮当前信号占用范围；Motorola 位定义需正式 DBC 校验。", 11, false, UiKit.Muted));
        _bits.Margin = new Thickness(0, 12, 0, 0);
        right.Children.Add(_bits);
        SwitchMessage(state.Messages.FirstOrDefault());
        if (state.Messages.Count > 0) _messages.SelectedIndex = 0;
    }
    private void SwitchMessage(UiDbcMessage? msg)
    {
        _active = msg; _signals.ItemsSource = msg?.Signals;
        _name.Text = msg?.Name ?? ""; _id.Text = msg?.CanId ?? "";
        _dlc.Text = msg?.Dlc.ToString() ?? ""; _note.Text = msg?.Comment ?? "";
        DrawBits();
    }
    private void ApplyMessage()
    {
        if (_active is null) return;
        if (!int.TryParse(_dlc.Text, out var dlc) || dlc is < 0 or > 8)
        { _status("经典 CAN 帧 DLC 必须为 0～8；此处不是 CAN FD 配置。"); return; }
        if (string.IsNullOrWhiteSpace(_name.Text)) { _status("报文名称不能为空。"); return; }
        _active.Name = _name.Text.Trim(); _active.CanId = _id.Text.Trim();
        _active.Dlc = dlc; _active.Comment = _note.Text;
        _messages.Items.Refresh(); _changed(); DrawBits();
        _status("已修改 UI DBC 草稿；尚未导出或验证正式 DBC 文件。");
    }
    private void DrawBits()
    {
        _bits.Children.Clear();
        var selected = _signals.SelectedItem as UiDbcSignal;
        var begin = selected?.StartBit ?? -1;
        var end = begin + (selected?.Length ?? 0);
        for (var bit = 0; bit < 64; bit++)
        {
            var active = selected is not null && bit >= begin && bit < end;
            var box = new Border
            {
                Width = 25, Height = 25, Margin = new Thickness(1),
                BorderBrush = UiKit.Outline, BorderThickness = new Thickness(1),
                Background = active ? UiKit.Accent : UiKit.Soft,
                ToolTip = "Bit " + bit,
                Child = new TextBlock { Text = bit.ToString(), FontSize = 10,
                    Foreground = active ? UiKit.White : UiKit.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center }
            };
            _bits.Children.Add(box);
        }
    }
}
