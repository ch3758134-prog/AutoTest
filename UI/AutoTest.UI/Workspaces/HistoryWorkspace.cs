using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

public sealed class HistoryWorkspace : Grid
{
    public HistoryWorkspace(UiSession state, Action changed, Action<string> status)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("报告与历史", "按 DUT、日期、结果与测试程序查找历史 Run；关联步骤、波形和证据"));
        var toolbar = UiKit.Actions();
        Grid.SetRow(toolbar, 1); Children.Add(toolbar);
        var search = UiKit.Input();
        search.Width = 360;
        search.ToolTip = "搜索历史记录中的编号/用例";
        toolbar.Children.Add(search);
        var export = UiKit.Button("导出报告", () => { });
        UiKit.Disabled(export, "还没有连接正式历史和报告服务");
        toolbar.Children.Add(export);
        var body = UiKit.Columns(0, 335);
        Grid.SetRow(body, 2); Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(UiKit.Card(left));
        var label = UiKit.Text("运行记录", 17, true);
        DockPanel.SetDock(label, Dock.Top); left.Children.Add(label);
        var table = UiKit.Table(state.History,
            ("执行时间", "Time", 145), ("DUT 编号", "Barcode", 170),
            ("测试项目 / 用例", "Case", 200), ("结果", "Verdict", 95), ("证据", "Evidence", 0));
        table.IsReadOnly = true;
        left.Children.Add(table);
        var view = CollectionViewSource.GetDefaultView(state.History);
        search.TextChanged += (_, _) =>
        {
            var q = search.Text.Trim();
            view.Filter = x => x is UiRunRecord record &&
                (record.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 record.Case.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 record.Verdict.Contains(q, StringComparison.OrdinalIgnoreCase));
        };
        var right = new StackPanel();
        Grid.SetColumn(right, 1); body.Children.Add(UiKit.Card(right));
        right.Children.Add(UiKit.Text("运行详情与证据", 17, true));
        right.Children.Add(UiKit.Note("真实 Run 历史不可由当前 UI 草稿生成或篡改。没有运行事实时列表保持空白。", true));
        var detail = UiKit.Text("选择一条真实运行记录以查看步骤、时间线、报告、波形及原始证据。", 12, false, UiKit.Muted);
        right.Children.Add(detail);
        table.SelectionChanged += (_, _) =>
        {
            if (table.SelectedItem is not UiRunRecord record) return;
            detail.Text = "DUT：" + record.Barcode + "\n执行时间：" + record.Time +
                "\n测试用例：" + record.Case + "\n判定：" + record.Verdict + "\n证据：" + record.Evidence;
        };
        right.Children.Add(UiKit.Empty("暂无真实测试记录", "进入真实集成阶段后由 RECORD 读取报告和实际执行证据。"));
    }
}
