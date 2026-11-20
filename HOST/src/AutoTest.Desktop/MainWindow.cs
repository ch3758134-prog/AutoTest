using AutoTest.Desktop.Core;
using AutoTest.Desktop.Views;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop;

public sealed class MainWindow : Window
{
    private readonly UiStore _state;
    private readonly TabControl _documents = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TreeView _explorer = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(8) };
    private readonly TextBlock _footer = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0) };
    private readonly TextBox _resourceSearch = new() { Margin = new Thickness(10, 10, 10, 8) };
    private readonly Dictionary<string, TabItem> _open = [];
    private readonly List<Window> _tools = [];
    private readonly List<Button> _nav = [];
    private string _current = "home";

    public UiStore State => _state;
    public MainWindow(UiStore state)
    {
        _state = state;
        Title = "AutoTest · ECU 工程与测试工作环境  |  UI 设计验证版";
        MinWidth = 1000;
        MinHeight = 660;
        Width = 1540;
        Height = 940;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        Background = UiKit.Canvas;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        BuildShell();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiStore.Status)) _footer.Text = _state.Status;
            if (e.PropertyName == nameof(UiStore.Workspace)) Title = "AutoTest · " + _state.Workspace + " | 纯 UI 验证版";
        };
        _state.Resources.CollectionChanged += (_, _) => RefreshResources();
        _state.Cases.CollectionChanged += (_, _) => RefreshResources();
        _state.DbcMessages.CollectionChanged += (_, _) => RefreshResources();
        _resourceSearch.TextChanged += (_, _) => RefreshResources();
        _footer.Text = _state.Status;
        Closing += OnClosing;
        PreviewKeyDown += OnKeyboard;
        Open("home", "开始工作");
    }
    private void BuildShell()
    {
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(53) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
        Content = main;

        var head = new DockPanel { Background = Brushes.White, LastChildFill = true };
        Grid.SetRow(head, 0);
        main.Children.Add(head);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 23, 0) };
        DockPanel.SetDock(brand, Dock.Left);
        head.Children.Add(brand);
        brand.Children.Add(new Border
        {
            Child = new TextBlock { Text = "AT", FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Background = UiKit.Accent, Width = 36, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 9, 10, 9)
        });
        brand.Children.Add(new TextBlock { Text = "AutoTest", FontSize = 19, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(UiKit.Note("  /  工程与测试", 12, new Thickness(0, 0, 0, 0)));
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 7, 12, 0) };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        right.Children.Add(UiKit.Note("纯 UI · 不连接 ECU  ", 12));
        right.Children.Add(UiKit.Button("保存 UI 会话", (_, _) => SaveSession(), true));
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = UiKit.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(UiStore.Workspace)) { Source = _state });
        head.Children.Add(title);

        var action = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)) };
        Grid.SetRow(action, 1);
        main.Children.Add(action);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 3, 0, 0) };
        DockPanel.SetDock(quick, Dock.Left);
        action.Children.Add(quick);
        quick.Children.Add(UiKit.Button("新建测试用例", (_, _) => { var c = _state.NewCase(); Open("test:" + c.Id, c.Name); }));
        quick.Children.Add(UiKit.Button("测试项目", (_, _) => Open("test-project", "测试项目")));
        quick.Children.Add(UiKit.Button("运行与调试", (_, _) => Open("run", "运行与调试")));
        quick.Children.Add(UiKit.Button("打开工具", (_, _) => Open("tools", "专业工具")));
        var mode = UiKit.Note("设计验证环境  ·  UI 会话数据不构成正式测试工程", 11);
        DockPanel.SetDock(mode, Dock.Right);
        mode.Margin = new Thickness(0, 0, 18, 0);
        action.Children.Add(mode);

        var work = new Grid();
        Grid.SetRow(work, 2);
        main.Children.Add(work);
        work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(186) });
        work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252), MinWidth = 190 });
        work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(242, 246, 251)) };
        Grid.SetColumn(nav, 0);
        work.Children.Add(nav);
        var navStack = new StackPanel { Margin = new Thickness(9, 13, 8, 10) };
        nav.Content = navStack;
        NavSection(navStack, "工作环境");
        Nav(navStack, "home", "⌂  开始工作");
        Nav(navStack, "test-project", "▤  测试项目");
        Nav(navStack, "test-editor", "▣  测试程序");
        Nav(navStack, "run", "▷  运行与调试");
        Nav(navStack, "flows", "↳  公共流程");
        NavSection(navStack, "工程开发");
        Nav(navStack, "resources", "▦  工程资源");
        Nav(navStack, "dbc", "▥  DBC 数据库");
        Nav(navStack, "can", "⇄  CAN 工具");
        Nav(navStack, "bench", "◈  综合台架");
        Nav(navStack, "inputs", "▧  输入配置");
        NavSection(navStack, "诊断与分析");
        Nav(navStack, "uds", "⌁  UDS 诊断");
        Nav(navStack, "flash", "⇪  ECU 烧录");
        Nav(navStack, "wave", "〽  波形分析");
        NavSection(navStack, "现场与记录");
        Nav(navStack, "scan", "▣  生产扫码");
        Nav(navStack, "records", "◷  报告与历史");
        Nav(navStack, "device", "⚙  设备诊断");

        var explorerBorder = UiKit.Card(new DockPanel());
        Grid.SetColumn(explorerBorder, 1);
        work.Children.Add(explorerBorder);
        var explorer = (DockPanel)explorerBorder.Child;
        var explorerHead = new StackPanel();
        DockPanel.SetDock(explorerHead, Dock.Top);
        explorer.Children.Add(explorerHead);
        explorerHead.Children.Add(UiKit.Heading("工程资源", 14, new Thickness(12, 13, 0, 1)));
        _resourceSearch.ToolTip = "搜索当前 UI 工作会话资源";
        _resourceSearch.Text = "";
        explorerHead.Children.Add(_resourceSearch);
        var bar = new WrapPanel { Margin = new Thickness(10, 0, 8, 4) };
        explorerHead.Children.Add(bar);
        bar.Children.Add(UiKit.Button("＋ 资源", (_, _) => AddResource()));
        bar.Children.Add(UiKit.Button("展开", (_, _) => ExpandAll()));
        explorer.Children.Add(_explorer);
        RefreshResources();

        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(223, 230, 239)), ShowsPreview = true };
        Grid.SetColumn(splitter, 2);
        work.Children.Add(splitter);

        var documentBox = new Border { Background = Brushes.White, BorderBrush = UiKit.Line, BorderThickness = new Thickness(1, 0, 0, 0) };
        Grid.SetColumn(documentBox, 3);
        work.Children.Add(documentBox);
        documentBox.Child = _documents;
        _documents.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _documents) || _documents.SelectedItem is not TabItem item) return;
            _current = item.Tag as string ?? "home";
        };
        var foot = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(230, 238, 249)) };
        Grid.SetRow(foot, 3);
        main.Children.Add(foot);
        foot.Children.Add(_footer);
    }
    private void NavSection(Panel target, string label) => target.Children.Add(new TextBlock
    {
        Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
        Foreground = UiKit.Muted, Margin = new Thickness(9, 14, 0, 5)
    });
    private void Nav(Panel target, string key, string title)
    {
        var btn = UiKit.Button(title, (_, _) => Open(key, title[3..].Trim()));
        btn.Style = (Style)Application.Current.FindResource("QuietButton");
        btn.HorizontalContentAlignment = HorizontalAlignment.Left;
        btn.Padding = new Thickness(11, 9, 8, 9);
        btn.Margin = new Thickness(0, 1, 0, 1);
        btn.Tag = key;
        target.Children.Add(btn);
        _nav.Add(btn);
    }
    private void RefreshResources()
    {
        var query = _resourceSearch.Text?.Trim() ?? "";
        _explorer.Items.Clear();
        void Group(string title, string key, IEnumerable<(string Label, string Id)> items)
        {
            var root = new TreeViewItem { Header = title, FontWeight = FontWeights.SemiBold, IsExpanded = true };
            foreach (var item in items.Where(x => query.Length == 0 || x.Label.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var child = new TreeViewItem { Header = item.Label, Tag = item.Id, FontWeight = FontWeights.Normal };
                child.MouseDoubleClick += (_, e) => { Open(item.Id, item.Label); e.Handled = true; };
                root.Items.Add(child);
            }
            if (root.Items.Count > 0 || query.Length == 0) _explorer.Items.Add(root);
        }
        Group("测试用例", "test", _state.Cases.Select(c => (c.Name, "test:" + c.Id)));
        Group("公共流程", "flow", [("公共流程编辑", "flows")]);
        Group("DBC", "dbc", _state.DbcMessages.Select(m => (m.Name, "dbc")));
        Group("CAN 待发列表", "can", _state.CanMessages.Select(m => (m.Name, "can")));
        Group("配置与工具", "tools", [
            ("外部输入与动作", "inputs"), ("UDS 请求序列", "uds"), ("烧录配置", "flash"),
            ("波形与参考", "wave"), ("历史记录", "records")]);
        Group("自定义资源", "resources", _state.Resources.Where(r => r.Kind == "自定义").Select(r => (r.Name, "resources")));
    }
    private void ExpandAll()
    {
        foreach (var item in _explorer.Items.OfType<TreeViewItem>()) item.IsExpanded = true;
    }
    private void AddResource()
    {
        var name = UiKit.AskText(this, "创建 UI 资源", "资源名称", "新建资源");
        if (string.IsNullOrWhiteSpace(name)) return;
        _state.Resources.Add(new UiResource { Name = name.Trim(), Kind = "自定义" });
        _state.Changed("添加资源 " + name);
        Open("resources", "工程资源");
    }
    public void Open(string key, string title)
    {
        if (key == "test-editor") key = "test:" + _state.Cases[0].Id;
        if (_open.TryGetValue(key, out var existing))
        {
            _documents.SelectedItem = existing;
            return;
        }
        var view = Workspaces.Build(key, _state, this);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 165, TextTrimming = TextTrimming.CharacterEllipsis });
        var close = UiKit.Button("×", (_, _) => CloseTab(key));
        close.Style = (Style)Application.Current.FindResource("QuietButton");
        close.Padding = new Thickness(5, 0, 4, 0);
        close.MinHeight = 22;
        close.MinWidth = 22;
        close.Margin = new Thickness(7, 0, 0, 0);
        header.Children.Add(close);
        var tab = new TabItem { Header = header, Content = view, Tag = key, Padding = new Thickness(12, 7, 9, 7), Background = Brushes.White };
        _open.Add(key, tab);
        _documents.Items.Add(tab);
        _documents.SelectedItem = tab;
    }
    private void CloseTab(string key)
    {
        if (!_open.TryGetValue(key, out var tab)) return;
        _documents.Items.Remove(tab);
        _open.Remove(key);
        if (_documents.Items.Count == 0) Open("home", "开始工作");
    }
    public void OpenTool(string key, string title)
    {
        var active = _tools.FirstOrDefault(w => w.Tag as string == key);
        if (active is not null) { active.Activate(); return; }
        var tool = new Window
        {
            Title = "AutoTest · " + title + "  |  UI 工作环境",
            Owner = this, Tag = key, Width = 1280, Height = 790,
            MinWidth = 860, MinHeight = 570, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = Workspaces.Build(key, _state, this)
        };
        _tools.Add(tool);
        tool.Closed += (_, _) => _tools.Remove(tool);
        tool.Show();
    }
    public void SaveSession()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "AutoTest UI 会话 (*.at-ui.json)|*.at-ui.json",
            DefaultExt = ".at-ui.json", FileName = "AutoTest-UI-Session.at-ui.json",
            Title = "保存 UI 体验会话（不是真实测试工程）"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { _state.SaveUiSession(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "UI 会话保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public void LoadSession()
    {
        var dialog = new OpenFileDialog { Filter = "AutoTest UI 会话 (*.at-ui.json)|*.at-ui.json", Title = "恢复 UI 体验会话" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _state.OpenUiSession(dialog.FileName);
            foreach (var tab in _open.Values.ToArray()) _documents.Items.Remove(tab);
            _open.Clear();
            Open("home", "开始工作");
            RefreshResources();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "UI 会话打开失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_state.Dirty) return;
        var answer = MessageBox.Show(this, "UI 会话中的修改尚未保存。\n\n保存的是交互体验数据，不是正式 ECU 工程。\n要放弃当前修改并退出吗？",
            "未保存的 UI 会话", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
    private void OnKeyboard(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S) { SaveSession(); e.Handled = true; }
        if (e.Key == Key.O) { LoadSession(); e.Handled = true; }
        if (e.Key == Key.N) { var c = _state.NewCase(); Open("test:" + c.Id, c.Name); e.Handled = true; }
        if (e.Key == Key.Tab && _documents.Items.Count > 1)
        {
            _documents.SelectedIndex = (_documents.SelectedIndex + 1) % _documents.Items.Count;
            e.Handled = true;
        }
    }
}
