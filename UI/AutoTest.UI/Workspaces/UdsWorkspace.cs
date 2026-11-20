using System.Windows;
using System.Windows.Controls;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Editable diagnostic service catalog and persistent request sequence UX.</summary>
public sealed class UdsWorkspace : Grid
{
    private static readonly (string Name, string Request)[] ServiceCatalog =
    [
        ("0x10 · DiagnosticSessionControl", "10 03"),
        ("0x11 · ECUReset", "11 01"),
        ("0x14 · ClearDiagnosticInformation", "14 FF FF FF"),
        ("0x19 · ReadDTCInformation", "19 02 FF"),
        ("0x22 · ReadDataByIdentifier", "22 F1 90"),
        ("0x23 · ReadMemoryByAddress", "23"),
        ("0x27 · SecurityAccess", "27 01"),
        ("0x28 · CommunicationControl", "28 00 03"),
        ("0x2E · WriteDataByIdentifier", "2E"),
        ("0x31 · RoutineControl", "31 01"),
        ("0x34 · RequestDownload", "34"),
        ("0x36 · TransferData", "36"),
        ("0x37 · RequestTransferExit", "37"),
        ("0x3E · TesterPresent", "3E 00"),
        ("0x85 · ControlDTCSetting", "85 01")
    ];
    public UdsWorkspace(UiSession state, Action changed, Action<string> status)
    {
        Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("UDS 诊断工作台", "服务模板 → 参数编辑 → 请求序列 → 实际响应；编辑不依赖设备连接"));
        var body = UiKit.Columns(245, 0, 355);
        Grid.SetRow(body, 1); Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(UiKit.Card(left));
        var leftHead = new StackPanel();
        DockPanel.SetDock(leftHead, Dock.Top); left.Children.Add(leftHead);
        leftHead.Children.Add(UiKit.Text("诊断服务", 16, true));
        leftHead.Children.Add(UiKit.Text("选择模板插入可编辑请求，不必手工记住全部服务码。", 12, false, UiKit.Muted));
        var serviceList = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0),
            ItemsSource = ServiceCatalog.Select(x => x.Name) };
        left.Children.Add(serviceList);

        var mid = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(mid, 1); body.Children.Add(UiKit.Card(mid));
        var mh = new StackPanel();
        DockPanel.SetDock(mh, Dock.Top); mid.Children.Add(mh);
        mh.Children.Add(UiKit.Text("连续诊断请求", 17, true));
        mh.Children.Add(UiKit.Note("直接双击数据单元格编辑。请求可以排序并保存在本地 UI 会话；当前不发送 ISO-TP 报文。"));
        var table = UiKit.Table(state.UdsRequests,
            ("服务", "Service", 220), ("请求 HEX", "Request", 190),
            ("描述", "Note", 210), ("状态", "LastResponse", 120));
        table.Columns.Insert(0, new DataGridCheckBoxColumn
        {
            Header = "选择", Binding = new System.Windows.Data.Binding(nameof(UiUdsRequest.Enabled))
            { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = new DataGridLength(60)
        });
        table.CellEditEnding += (_, _) => changed();
        table.Columns[^1].IsReadOnly = true;
        mh.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 从服务模板添加", () =>
            {
                if (serviceList.SelectedItem is not string name)
                { status("先选择一个诊断服务模板。"); return; }
                var definition = ServiceCatalog.First(x => x.Name == name);
                var item = new UiUdsRequest { Service = definition.Name, Request = definition.Request };
                state.UdsRequests.Add(item); table.SelectedItem = item; changed();
            }, true),
            UiKit.Button("＋ 空白请求", () =>
            {
                var item = new UiUdsRequest { Service = "自定义 UDS", Request = "" };
                state.UdsRequests.Add(item); table.SelectedItem = item; changed();
            }),
            UiKit.Button("复制请求", () =>
            {
                if (table.SelectedItem is not UiUdsRequest q) return;
                state.UdsRequests.Add(new UiUdsRequest
                { Service = q.Service, Request = q.Request, Note = q.Note }); changed();
            })));
        mh.Children.Add(UiKit.Actions(
            UiKit.Button("上移", () => Move(-1)),
            UiKit.Button("下移", () => Move(1)),
            UiKit.Button("删除", () =>
            {
                if (table.SelectedItem is UiUdsRequest q) { state.UdsRequests.Remove(q); changed(); }
            })));
        mid.Children.Add(table);
        void Move(int delta)
        {
            if (table.SelectedItem is not UiUdsRequest q) return;
            var i = state.UdsRequests.IndexOf(q);
            if (i + delta < 0 || i + delta >= state.UdsRequests.Count) return;
            state.UdsRequests.Move(i, i + delta); changed();
        }

        var right = new StackPanel();
        Grid.SetColumn(right, 2); body.Children.Add(UiKit.Card(UiKit.Scroll(right)));
        right.Children.Add(UiKit.Text("请求与响应", 17, true));
        right.Children.Add(UiKit.Note("不发送真实请求时，不显示成功响应、NRC 或耗时数据。ISO-TP/UDS 实际会话由后端接线阶段处理。", true));
        var address = UiKit.Input(state.DiagnosticAddress);
        address.LostFocus += (_, _) => { state.DiagnosticAddress = address.Text; changed(); };
        right.Children.Add(UiKit.Field("诊断目标地址（UI 备注）", address));
        right.Children.Add(UiKit.Text("执行控制", 14, true));
        var send = UiKit.Button("发送选中", () => { }, true);
        var execute = UiKit.Button("连续执行序列", () => { });
        var cancel = UiKit.Button("取消会话", () => { });
        foreach (var b in new[] { send, execute, cancel })
            UiKit.Disabled(b, "纯 UI 阶段未接真实 UDS/ISO-TP 服务");
        right.Children.Add(UiKit.Actions(send, execute, cancel));
        right.Children.Add(UiKit.Text("请求时间线", 14, true));
        right.Children.Add(UiKit.Empty("暂无交互记录", "真实请求、响应及 NRC 将按关联 ID 呈现。"));
    }
}
