using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Desktop.Rebuild;

/// <summary>Resource navigation belongs to the pure UX work environment, not old PROJECT storage.</summary>
public sealed class ProjectWorkspace : UserControl
{
    private readonly UiStudyStore _store;
    private readonly Action<UiTestCase> _open;
    private readonly Action<string> _status;
    private readonly StackPanel _resourceList = UiVisual.Stack();
    private readonly ContentControl _details = new();
    private string _filter = "";

    public ProjectWorkspace(UiStudyStore store, Action<UiTestCase> open, Action<string> status)
    {
        _store = store; _open = open; _status = status;
        var shell = UiVisual.Columns(new GridLength(284), new GridLength(5), UiVisual.Star());
        Content = shell;
        var sidebar = UiVisual.Stack();
        sidebar.Margin = new Thickness(0, 0, 5, 0);
        sidebar.Children.Add(UiVisual.Text("资源浏览器", 16, true));
        var search = UiVisual.Input();
        search.Margin = new Thickness(0, 10, 0, 8);
        search.ToolTip = "搜索所有 UI 资源";
        search.TextChanged += (_, _) => { _filter = search.Text.Trim(); RefreshList(); };
        sidebar.Children.Add(search);
        sidebar.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 测试用例", NewCase, true),
            UiVisual.Button("＋ 公共流程", NewFlow)));
        sidebar.Children.Add(UiVisual.Scroll(_resourceList));
        UiVisual.At(shell, UiVisual.Box(sidebar), 0);
        var split = new GridSplitter { Width = 5, Background = UiVisual.Line,
            HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
        UiVisual.At(shell, split, 1);
        UiVisual.At(shell, _details, 2);
        RefreshList();
        SelectOverview();
    }

    private void RefreshList()
    {
        _resourceList.Children.Clear();
        AddGroup("测试用例", _store.Current.TestCases.Select(x => (x.Name, (Action)(() => SelectCase(x)))));
        AddGroup("公共流程", _store.Current.PublicFlows.Select(x => (x.Name, (Action)(() => SelectFlow(x)))));
        AddGroup("DBC 报文", _store.Current.DbcMessages.Select(x => (x.Name, (Action)(() => SelectOverview()))));
        AddGroup("CAN 发送配置", _store.Current.CanMessages.Select(x => (x.Name, (Action)(() => SelectOverview()))));
    }
    private void AddGroup(string title, IEnumerable<(string Name, Action Open)> items)
    {
        var label = UiVisual.Text(title, 12, true, UiVisual.Muted);
        label.Margin = new Thickness(0, 13, 0, 6);
        _resourceList.Children.Add(label);
        var any = false;
        foreach (var item in items.Where(x => _filter.Length == 0 ||
            x.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            any = true;
            var b = UiVisual.Button("▤  " + item.Name, item.Open);
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 0, 4);
            _resourceList.Children.Add(b);
        }
        if (!any) _resourceList.Children.Add(UiVisual.Note("没有匹配的资源"));
    }
    private void SelectOverview()
    {
        var s = UiVisual.Stack();
        var nameField = UiVisual.Input(_store.Current.ProjectName);
        s.Children.Add(UiVisual.Text("UI 工作区", 19, true));
        s.Children.Add(UiVisual.Note("这是独立 UI 体验草稿，不是正式 ECU 工程。"));
        s.Children.Add(UiVisual.Rule());
        s.Children.Add(UiVisual.Field("工作区名称", nameField));
        s.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("保存名称", () => { _store.Current.ProjectName = nameField.Text.Trim();
                Save("名称已保存至 UI 草稿"); }, true),
            UiVisual.Button("导出 UI 草稿", Export),
            UiVisual.Button("导入 UI 草稿", Import)
        ));
        s.Children.Add(UiVisual.Text("主要资源", 15, true));
        s.Children.Add(UiVisual.Note("测试用例 " + _store.Current.TestCases.Count + " · 公共流程 " +
            _store.Current.PublicFlows.Count + " · DBC 报文 " + _store.Current.DbcMessages.Count +
            " · CAN 发送行 " + _store.Current.CanMessages.Count));
        _details.Content = UiVisual.Box(s, new Thickness(24));
    }
    private void SelectCase(UiTestCase item)
    {
        var s = UiVisual.Stack();
        s.Children.Add(UiVisual.Text("测试用例", 20, true));
        s.Children.Add(UiVisual.Note("独立 UI 对象，可命名、打开、移除。运行选择与编辑选择分离。"));
        var name = UiVisual.Input(item.Name);
        name.Margin = new Thickness(0, 17, 0, 12);
        s.Children.Add(UiVisual.Field("名称", name));
        s.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("打开编辑器", () => _open(item), true),
            UiVisual.Button("保存名称", () => { item.Name = name.Text.Trim(); Save("已更新用例名称"); RefreshList(); }),
            UiVisual.Button("删除用例", () =>
            {
                if (UiDialogs.Confirm(Window.GetWindow(this)!, "删除用例", "删除此 UI 草稿用例？可通过导出文件备份。"))
                { _store.Current.TestCases.Remove(item); Save("已删除 UI 用例"); RefreshList(); SelectOverview(); }
            })));
        s.Children.Add(UiVisual.Note("准备步骤：" + UiTestCase.Walk(item.Prepare).Count() +
            "　测试步骤：" + UiTestCase.Walk(item.Test).Count() +
            "　清理步骤：" + UiTestCase.Walk(item.Cleanup).Count()));
        _details.Content = UiVisual.Box(s, new Thickness(24));
    }
    private void SelectFlow(UiPublicFlow flow)
    {
        var s = UiVisual.Stack();
        s.Children.Add(UiVisual.Text("公共流程", 20, true));
        s.Children.Add(UiVisual.Note("可在自动测试编辑器中统一管理流程步骤和调用引用。"));
        var name = UiVisual.Input(flow.Name);
        s.Children.Add(UiVisual.Field("名称", name));
        s.Children.Add(UiVisual.Toolbar(UiVisual.Button("重命名", () =>
        {
            flow.Name = name.Text.Trim(); Save("公共流程已重命名"); RefreshList();
        }, true), UiVisual.Button("删除", () =>
        {
            if (UiDialogs.Confirm(Window.GetWindow(this)!, "删除公共流程", "确认删除 UI 草稿公共流程？"))
            { _store.Current.PublicFlows.Remove(flow); Save("已删除公共流程"); RefreshList(); SelectOverview(); }
        })));
        _details.Content = UiVisual.Box(s, new Thickness(24));
    }
    private void NewCase()
    {
        var name = UiDialogs.Text(Window.GetWindow(this)!, "创建测试用例", "测试用例名称", "新建测试用例");
        if (name is null) return;
        var item = new UiTestCase { Name = name };
        _store.Current.TestCases.Add(item);
        Save("已创建 UI 测试用例");
        RefreshList(); SelectCase(item);
    }
    private void NewFlow()
    {
        var name = UiDialogs.Text(Window.GetWindow(this)!, "创建公共流程", "公共流程名称", "新建公共流程");
        if (name is null) return;
        var item = new UiPublicFlow { Name = name };
        _store.Current.PublicFlows.Add(item);
        Save("已创建 UI 公共流程");
        RefreshList(); SelectFlow(item);
    }
    private void Save(string message) { _store.Save(); _status(message); }
    private void Export()
    {
        var d = new SaveFileDialog { Title = "导出纯 UI 工作区草稿",
            Filter = "AutoTest UI 草稿|*.autotest-ui.json", FileName = "autotest-ui.autotest-ui.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try { _store.Export(d.FileName); _status("UI 草稿已导出，不是正式工程文件"); }
        catch (Exception error) { _status("导出失败：" + error.Message); }
    }
    private void Import()
    {
        var d = new OpenFileDialog { Title = "导入纯 UI 工作区草稿",
            Filter = "AutoTest UI 草稿|*.autotest-ui.json|JSON|*.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try { _store.Import(d.FileName); _status("UI 草稿已导入"); RefreshList(); SelectOverview(); }
        catch (Exception error) { _status("导入失败：" + error.Message); }
    }
}
