using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Rebuild;

namespace AutoTest.Desktop.Shell;

/// <summary>New application shell. Has zero dependency on old G4 UI or any real hardware service.</summary>
public sealed class MainWindow : Window
{
    private readonly UiStudyStore _store = new();
    private readonly Dictionary<string, Window> _tools = new();
    private readonly StackPanel _navigation = UiVisual.Stack();
    private readonly ContentControl _page = new();
    private readonly TextBlock _statusText = UiVisual.Text("", 11, false, UiVisual.Muted);
    private readonly TextBlock _heading = UiVisual.Text("", 22, true);
    private readonly TextBlock _subheading = UiVisual.Note("");
    private readonly TextBlock _projectLabel = UiVisual.Text("", 12, true);
    private readonly TextBlock _routeLabel = UiVisual.Text("", 12, false, UiVisual.Muted);
    private TestWorkspace? _activeTest;
    private string _route = "home";
    private Guid? _selectedCase;

    public MainWindow()
    {
        Title = "AutoTest | ECU 测试开发工作环境 · UI 设计体验";
        MinWidth = 960; MinHeight = 620;
        Width = 1460; Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = UiVisual.Back;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        BuildShell();
        Navigate("home");
        Closing += (_, _) =>
        {
            try { _store.Save(); }
            catch (Exception error) { MessageBox.Show(this, "UI 草稿未能保存：\n" + error.Message, "保存提示", MessageBoxButton.OK, MessageBoxImage.Warning); }
            foreach (var tool in _tools.Values.ToList()) tool.Close();
        };
        PreviewKeyDown += OnShortcut;
    }

    private void BuildShell()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
        Content = root;

        var top = UiVisual.Columns(new GridLength(250), UiVisual.Star(), new GridLength(320));
        top.Background = Brushes.White;
        UiVisual.At(root, top, 0, 0);
        var brand = UiVisual.Stack(true); brand.Margin = new Thickness(20, 11);
        var glyph = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9),
            Background = UiVisual.Accent, Child = UiVisual.Text("AT", 14, true, Brushes.White) };
        ((TextBlock)glyph.Child).HorizontalAlignment = HorizontalAlignment.Center;
        brand.Children.Add(glyph);
        var brandText = UiVisual.Stack();
        brandText.Margin = new Thickness(10, 0, 0, 0);
        brandText.Children.Add(UiVisual.Text("AutoTest", 17, true));
        brandText.Children.Add(UiVisual.Text("ECU 测试 · 设计体验", 10, false, UiVisual.Muted));
        brand.Children.Add(brandText);
        UiVisual.At(top, brand, 0);
        var route = UiVisual.Stack();
        route.VerticalAlignment = VerticalAlignment.Center;
        route.Children.Add(_routeLabel);
        _projectLabel.Margin = new Thickness(0, 3, 0, 0);
        route.Children.Add(_projectLabel);
        UiVisual.At(top, route, 1);
        var topTools = UiVisual.Stack(true);
        topTools.HorizontalAlignment = HorizontalAlignment.Right;
        topTools.VerticalAlignment = VerticalAlignment.Center;
        topTools.Margin = new Thickness(0, 0, 16, 0);
        topTools.Children.Add(UiVisual.Text("● 纯 UI · 无设备连接", 11, true, UiVisual.Brush("#9C6B2F")));
        var quickTools = UiVisual.Button("工具目录", () => Navigate("tools"));
        quickTools.Margin = new Thickness(14, 0, 0, 0);
        topTools.Children.Add(quickTools);
        UiVisual.At(top, topTools, 2);

        var workspace = UiVisual.Columns(new GridLength(94), UiVisual.Star());
        UiVisual.At(root, workspace, 0, 1);
        _navigation.Background = UiVisual.Brush("#EDF2F8");
        _navigation.Margin = new Thickness(0, 0, 0, 0);
        UiVisual.At(workspace, _navigation, 0);
        var main = new Grid { Background = UiVisual.Back };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        UiVisual.At(workspace, main, 1);
        var pageHeader = UiVisual.Stack();
        pageHeader.Margin = new Thickness(24, 15, 18, 10);
        pageHeader.Children.Add(_heading);
        _subheading.Margin = new Thickness(0, 5, 0, 0);
        pageHeader.Children.Add(_subheading);
        UiVisual.At(main, pageHeader, 0, 0);
        _page.Margin = new Thickness(18, 0, 18, 15);
        UiVisual.At(main, _page, 0, 1);

        var statusBar = UiVisual.Columns(UiVisual.Star(), new GridLength(220));
        statusBar.Background = Brushes.White;
        var status = UiVisual.Stack(true); status.Margin = new Thickness(18, 4, 0, 0);
        status.Children.Add(UiVisual.Text("UI 交互草稿", 11, true, UiVisual.Accent));
        status.Children.Add(UiVisual.Text("  ·  ", 11, false, UiVisual.Muted));
        status.Children.Add(_statusText);
        UiVisual.At(statusBar, status);
        var note = UiVisual.Text("不产生真实 CAN / UDS / ECU 结果", 11, false, UiVisual.Muted);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.Margin = new Thickness(0, 5, 12, 0);
        UiVisual.At(statusBar, note, 1);
        UiVisual.At(root, statusBar, 0, 2);
        RefreshNav();
    }

    private void RefreshNav()
    {
        _navigation.Children.Clear();
        foreach (var item in new (string Key, string Icon, string Label)[]
        {
            ("home","⌂","工作台"), ("project","▤","工程资源"),
            ("test","▣","自动测试"), ("tools","▦","专业工具"),
            ("production","▥","生产检测"), ("records","◷","历史记录"),
            ("device","⌁","设备状态")
        })
        {
            var active = _route == item.Key;
            var b = new Button
            {
                Background = active ? UiVisual.SoftAccent : Brushes.Transparent,
                Foreground = active ? UiVisual.Accent : UiVisual.Ink,
                BorderThickness = new Thickness(0), Height = 66,
                Margin = new Thickness(6, 5, 6, 0),
                Cursor = Cursors.Hand
            };
            var inner = UiVisual.Stack();
            inner.Children.Add(UiVisual.Text(item.Icon, 23, true, active ? UiVisual.Accent : UiVisual.Muted));
            inner.Children.Add(UiVisual.Text(item.Label, 11, active, active ? UiVisual.Accent : UiVisual.Ink));
            foreach (TextBlock t in inner.Children) t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Content = inner;
            b.Click += (_, _) => Navigate(item.Key);
            _navigation.Children.Add(b);
        }
    }

    public void Navigate(string route)
    {
        _route = route;
        _activeTest = null;
        _projectLabel.Text = _store.Current.ProjectName;
        _routeLabel.Text = "工作环境  /  " + route switch
        {
            "home" => "首页", "project" => "工程", "test" => "自动测试", "tools" => "专业工具",
            "production" => "生产", "records" => "历史", "device" => "设备", _ => route
        };
        (_heading.Text, _subheading.Text) = route switch
        {
            "home" => ("开始工作", "选择当前任务并继续工作。这里不是旧 G4 界面的兼容入口。"),
            "project" => ("工程与资源", "纯 UI 工作区：资源组织、定位、命名和编辑行为。"),
            "test" => ("自动测试", "测试项目、步骤编排与运行工作体验；尚不执行实体测试。"),
            "tools" => ("专业工具", "工具采用自己的专业工作环境，独立窗口不会覆盖工程导航。"),
            "production" => ("生产检测", "扫码、识别与运行准备的完整界面；检测执行暂不可用。"),
            "records" => ("历史与报告", "真实结果尚未接入，不能展示演示通过记录。"),
            "device" => ("设备与诊断", "当前仅设计状态表达，不执行 USB 连接和工位控制。"),
            _ => ("AutoTest", "")
        };
        _page.Content = route switch
        {
            "home" => HomeView(),
            "project" => new ProjectWorkspace(_store, OpenCase, Status),
            "test" => _activeTest = new TestWorkspace(_store, _selectedCase, Status),
            "tools" => ToolDirectory(),
            "production" => new ProductionWorkspace(_store, Status),
            "records" => new RecordsWorkspace(_store),
            "device" => new DeviceWorkspace(),
            _ => HomeView()
        };
        RefreshNav();
        Status("已打开：" + _heading.Text);
    }

    public void OpenCase(UiTestCase test)
    {
        _selectedCase = test.Id;
        Navigate("test");
    }
    public void Status(string value)
    {
        _statusText.Text = value;
        _projectLabel.Text = _store.Current.ProjectName;
    }
    public void Persist(string message = "UI 草稿已保存")
    {
        try { _store.Save(); Status(message); }
        catch (Exception error) { Status("草稿保存失败：" + error.Message); }
    }
    public Window OpenTool(string key)
    {
        if (_tools.TryGetValue(key, out var existing))
        {
            existing.Activate();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            return existing;
        }
        var win = new ToolWindow(key, _store, Persist)
        {
            Owner = this, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        win.Closed += (_, _) => _tools.Remove(key);
        _tools[key] = win;
        win.Show();
        return win;
    }
    private UIElement ToolDirectory()
    {
        var wrap = new WrapPanel { Margin = new Thickness(5) };
        foreach (var tool in ToolCatalog.Items)
        {
            var body = UiVisual.Stack();
            body.Children.Add(UiVisual.Text(tool.Icon, 30, true, UiVisual.Accent));
            body.Children.Add(UiVisual.Text(tool.Name, 16, true));
            var d = UiVisual.Note(tool.Description);
            d.Margin = new Thickness(0, 6, 0, 12);
            body.Children.Add(d);
            body.Children.Add(UiVisual.Button("打开专业工具", () => OpenTool(tool.Key), true));
            var border = UiVisual.Box(body, new Thickness(18));
            border.Width = 275;
            border.Height = 188;
            border.Margin = new Thickness(0, 0, 13, 13);
            wrap.Children.Add(border);
        }
        return UiVisual.Scroll(wrap);
    }
    private UIElement HomeView()
    {
        var panel = UiVisual.Stack();
        panel.Margin = new Thickness(10, 0, 10, 10);
        var intro = UiVisual.Box(UiVisual.Stack(), new Thickness(22));
        var head = (StackPanel)intro.Child;
        head.Children.Add(UiVisual.Text("继续你的 ECU 测试工作", 24, true));
        var detail = UiVisual.Note("先完成结构和交互设计。工程、测试、工具均使用纯 UI 草稿，不代表正式 TEST/PROJECT 文件或设备运行。");
        detail.Margin = new Thickness(0, 8, 0, 15);
        head.Children.Add(detail);
        head.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("进入自动测试编辑", () => Navigate("test"), true),
            UiVisual.Button("管理工程资源", () => Navigate("project")),
            UiVisual.Button("打开专业工具", () => Navigate("tools"))
        ));
        panel.Children.Add(intro);
        var h = UiVisual.Text("当前工作", 16, true);
        h.Margin = new Thickness(5, 22, 0, 12);
        panel.Children.Add(h);
        var cards = UiVisual.Stack(true);
        cards.Children.Add(SummaryTile("测试用例", _store.Current.TestCases.Count.ToString(), "编辑三阶段测试程序", () => Navigate("test")));
        cards.Children.Add(SummaryTile("公共流程", _store.Current.PublicFlows.Count.ToString(), "复用顺序与条件步骤", () => Navigate("test")));
        cards.Children.Add(SummaryTile("CAN 发送配置", _store.Current.CanMessages.Count.ToString(), "仅编辑待发内容", () => OpenTool("can")));
        cards.Children.Add(SummaryTile("DBC 报文", _store.Current.DbcMessages.Count.ToString(), "编辑信号定义", () => OpenTool("dbc")));
        panel.Children.Add(cards);
        var info = UiVisual.Box(UiVisual.Stack(), new Thickness(20));
        var infoStack = (StackPanel)info.Child;
        var nextTitle = UiVisual.Text("工作边界", 15, true);
        nextTitle.Margin = new Thickness(0, 0, 0, 10);
        infoStack.Children.Add(nextTitle);
        infoStack.Children.Add(UiVisual.Note("本阶段支持 UI 对象编辑和会话恢复。真实 USB / CAN / UDS / Flash 及运行记录未接入；相关操作不会制造虚假的成功状态。"));
        info.Margin = new Thickness(0, 19, 0, 0);
        panel.Children.Add(info);
        return UiVisual.Scroll(panel);
    }
    private UIElement SummaryTile(string name, string count, string note, Action action)
    {
        var s = UiVisual.Stack();
        s.Children.Add(UiVisual.Text(name, 13, true));
        var n = UiVisual.Text(count, 30, true, UiVisual.Accent);
        n.Margin = new Thickness(0, 7, 0, 7);
        s.Children.Add(n);
        s.Children.Add(UiVisual.Note(note));
        var card = UiVisual.Box(s, new Thickness(18));
        card.Width = 235;
        card.Margin = new Thickness(0, 0, 12, 0);
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => action();
        return card;
    }
    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S) { Persist(); e.Handled = true; }
        if (_activeTest is not null && e.Key == Key.Z) { _activeTest.Undo(); e.Handled = true; }
        if (_activeTest is not null && e.Key == Key.Y) { _activeTest.Redo(); e.Handled = true; }
    }
}
