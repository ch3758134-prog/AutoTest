using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

public sealed class ProjectWorkspace : Grid
{
    public ProjectWorkspace(UiSession state, Action changed, Action<string> status)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        var header = UiKit.Header("工程与资源", "组织工作对象、资源引用和文件列表 · 当前只编辑 UI 会话，不触碰真实工程文件");
        Children.Add(header);
        var actions = UiKit.Actions(
            UiKit.Button("新建资源", () =>
            {
                state.Resources.Add(new UiResource { Name = "新建资源" });
                changed(); status("已加入 UI 资源列表，可直接修改单元格。");
            }, true));
        Grid.SetRow(actions, 1); Children.Add(actions);
        var grid = UiKit.Columns(240, 0);
        Grid.SetRow(grid, 2); Children.Add(grid);
        var nav = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        grid.Children.Add(UiKit.Card(nav));
        nav.Children.Add(UiKit.Text("资源类别", 15, true));
        var types = new ListBox { Margin = new Thickness(0, 12, 0, 0),
            ItemsSource = new[] { "全部资源", "测试项目", "测试用例", "公共流程", "DBC", "输入与台架配置",
                "波形与参考", "固件和烧录配置", "其他配置" }, SelectedIndex = 0, BorderThickness = new Thickness(0) };
        nav.Children.Add(types);
        var pane = new DockPanel();
        Grid.SetColumn(pane, 1); grid.Children.Add(UiKit.Card(pane));
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(top, Dock.Top); pane.Children.Add(top);
        top.Children.Add(UiKit.Text("资源清单", 16, true));
        var filter = UiKit.Input();
        filter.Margin = new Thickness(0, 8, 0, 0); filter.ToolTip = "按名称搜索当前 UI 资源";
        top.Children.Add(filter);
        var table = UiKit.Table(state.Resources,
            ("名称", "Name", 210), ("类型", "Kind", 145), ("关联文件", "Path", 260), ("备注", "Note", 0));
        table.CellEditEnding += (_, _) => changed();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(state.Resources);
        filter.TextChanged += (_, _) =>
        {
            var query = filter.Text.Trim();
            view.Filter = item => item is UiResource r &&
                (r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 r.Kind.Contains(query, StringComparison.OrdinalIgnoreCase));
        };
        types.SelectionChanged += (_, _) =>
        {
            var type = types.SelectedItem as string;
            view.Filter = item => item is UiResource r &&
                (type == "全部资源" || type is null || r.Kind == type) &&
                (filter.Text.Length == 0 || r.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase));
        };
        var bar = UiKit.Actions(
            UiKit.Button("复制选中", () =>
            {
                if (table.SelectedItem is not UiResource r) return;
                state.Resources.Add(new UiResource { Name = r.Name + " - 副本", Kind = r.Kind, Path = r.Path, Note = r.Note });
                changed();
            }),
            UiKit.Button("移除选中", () =>
            {
                if (table.SelectedItem is not UiResource r) return;
                state.Resources.Remove(r); changed();
            }));
        DockPanel.SetDock(bar, Dock.Bottom); pane.Children.Add(bar);
        pane.Children.Add(table);
    }
}
