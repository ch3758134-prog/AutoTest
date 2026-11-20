using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.UI.Design;
using AutoTest.UI.Model;
using AutoTest.UI.Workspaces;
using Microsoft.Win32;

namespace AutoTest.UI;

/// <summary>
/// Clean WPF Shell for visual / interaction acceptance. NO connection to backend services.
/// The user's prior task, document and secondary tool windows are distinct contexts.
/// </summary>
public sealed class MainWindow : Window
{
    private UiSession _session = UiSession.New();
    private readonly Border _center = new() { Background = UiKit.Canvas };
    private readonly TextBlock _contextName = UiKit.Text("未命名工作区", 13, true);
    private readonly TextBlock _status = UiKit.Text("", 12, false, UiKit.Muted);
    private readonly TextBlock _heading = UiKit.Text("自动测试", 16, true);
    private readonly StackPanel _nav = new();
    private readonly Dictionary<string, Window> _tools = [];
    private string _active = "自动测试";
    private bool _modified;
    private bool _closing;
    public MainWindow()
    {
        Title = "AutoTest · 工程测试工作环境（UI 设计体验版）";
        Width = 1450;
        Height = 920;
        MinWidth = 1024;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        Background = UiKit.Canvas;
        Foreground = UiKit.Ink;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = BuildChrome();
        ShowWorkspace("自动测试");
        SetStatus("独立纯 UI 体验：没有连接真实设备或任何业务执行器。");
        Closing += OnClosing;
        PreviewKeyDown += OnKeyDown;
    }
    private UIElement BuildChrome()
    {
        var root = new DockPanel { LastChildFill = true };
        var top = new Border { Background = UiKit.White, BorderBrush = UiKit.Outline,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(17, 10, 17, 10) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var header = UiKit.Columns(255, 0, 600);
        top.Child = header;
        var branding = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        branding.Children.Add(new Border { Width = 32, Height = 32, Background = UiKit.Accent,
            CornerRadius = new CornerRadius(6), Child = new TextBlock { Text = "AT",
                Foreground = UiKit.White, FontSize = 14, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        branding.Children.Add(new StackPanel { Margin = new Thickness(11, 0, 0, 0),
            Children = { UiKit.Text("AutoTest", 17, true), UiKit.Text("工程测试工作环境", 11, false, UiKit.Muted) } });
        header.Children.Add(branding);
        var current = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(current, 1);
        header.Children.Add(current);
        current.Children.Add(_heading);
        var context = new Border { Background = UiKit.Soft, Padding = new Thickness(11, 5, 11, 5),
            CornerRadius = new CornerRadius(5), Margin = new Thickness(16, 0, 0, 0),
            Child = _contextName };
        current.Children.Add(context);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);
        actions.Children.Add(UiKit.Button("新建工作区", NewSession));
        actions.Children.Add(UiKit.Button("打开 UI 会话", OpenSession));
        actions.Children.Add(UiKit.Button("保存 UI 会话", SaveSession, true));

        var footer = new Border { Background = UiKit.White, BorderBrush = UiKit.Outline,
            BorderThickness = new Thickness(0, 1, 0, 0), Height = 31, Padding = new Thickness(15, 4, 15, 4) };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Child = new DockPanel
        {
            Children =
            {
                new TextBlock { Text = "UI ONLY · 不连接 ECU / 主控板 · 无真实运行结果",
                    Foreground = UiKit.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                _status
            }
        };
        DockPanel.SetDock(_status, Dock.Right);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(footer);

        var body = new Grid { Background = UiKit.Canvas };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);
        var navigation = new Border { Background = UiKit.White, BorderBrush = UiKit.Outline,
            BorderThickness = new Thickness(0, 0, 1, 0) };
        body.Children.Add(navigation);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        navigation.Child = scroll;
        var group = new StackPanel { Margin = new Thickness(12, 18, 12, 12) };
        scroll.Content = group;
        group.Children.Add(UiKit.Text("工作", 11, true, UiKit.Muted));
        _nav.Margin = new Thickness(0, 8, 0, 18);
        group.Children.Add(_nav);
        group.Children.Add(UiKit.Text("专业工具", 11, true, UiKit.Muted));
        var tools = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        group.Children.Add(tools);
        foreach (var label in new[] { "DBC 编辑", "CAN 工作台", "综合手动台架", "输入配置", "UDS 诊断", "ECU 烧录", "波形分析", "设备诊断" })
        {
            var name = label;
            var button = UiKit.Button("  " + name, () => OpenTool(name));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.MinHeight = 36;
            button.Margin = new Thickness(0, 0, 0, 4);
            button.BorderThickness = new Thickness(0);
            button.Background = UiKit.White;
            tools.Children.Add(button);
        }
        var work = new Border { Margin = new Thickness(19, 18, 19, 15), Child = _center };
        Grid.SetColumn(work, 1);
        body.Children.Add(work);
        return root;
    }
    private void RebuildNav()
    {
        _nav.Children.Clear();
        foreach (var name in new[] { "自动测试", "工程资源", "生产扫码", "报告与历史", "工作台概览" })
        {
            var key = name;
            var active = _active == key;
            var b = UiKit.Button("  " + name, () => ShowWorkspace(key));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Background = active ? UiKit.Soft : UiKit.White;
            b.Foreground = active ? UiKit.Accent : UiKit.Ink;
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            b.BorderThickness = new Thickness(0);
            b.MinHeight = 40;
            b.Margin = new Thickness(0, 0, 0, 4);
            _nav.Children.Add(b);
        }
    }
    private void ShowWorkspace(string name)
    {
        _active = name;
        _heading.Text = name;
        _contextName.Text = _session.ProjectName + (_modified ? " ●" : "");
        RebuildNav();
        _center.Child = name switch
        {
            "自动测试" => new TestWorkspace(_session, Changed, SetStatus),
            "工程资源" => new ProjectWorkspace(_session, Changed, SetStatus),
            "生产扫码" => new ProductionWorkspace(_session, Changed, SetStatus),
            "报告与历史" => new HistoryWorkspace(_session, Changed, SetStatus),
            _ => new StartWorkspace(_session, Changed, SetStatus, ShowWorkspace, OpenTool)
        };
    }
    private void OpenTool(string name)
    {
        if (_tools.TryGetValue(name, out var already))
        {
            already.Activate();
            if (already.WindowState == WindowState.Minimized) already.WindowState = WindowState.Normal;
            return;
        }
        var window = new Window
        {
            Title = "AutoTest · " + name + " · UI 设计体验",
            Owner = this, Width = 1220, Height = 770, MinWidth = 840, MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily, FontSize = 13,
            Foreground = UiKit.Ink, Background = UiKit.Canvas,
            Content = ToolWorkspaces.Build(name, _session, Changed, SetStatus)
        };
        window.Closed += (_, _) => _tools.Remove(name);
        _tools[name] = window;
        window.Show();
    }
    private void Changed()
    {
        _modified = true;
        _contextName.Text = _session.ProjectName + " ●";
    }
    private void SetStatus(string message)
    {
        _status.Text = message;
        _status.ToolTip = message;
    }
    private bool ConfirmDiscard()
    {
        if (!_modified) return true;
        return MessageBox.Show(this,
            "当前 UI 交互会话还有未保存修改。放弃这些修改吗？\n（这不是正式 ECU 工程数据。）",
            "未保存的 UI 会话", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
    private void NewSession()
    {
        if (!ConfirmDiscard()) return;
        foreach (var w in _tools.Values.ToArray()) w.Close();
        _session = UiSession.New();
        _modified = false;
        ShowWorkspace("自动测试");
        SetStatus("已新建独立 UI 会话，不代表已创建正式 TEST 工程。");
    }
    private void OpenSession()
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "打开 AutoTest UI 体验会话",
            Filter = "UI 会话 (*.autotest-ui.json)|*.autotest-ui.json|JSON 文件 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var restored = UiSession.Load(dialog.FileName);
            foreach (var w in _tools.Values.ToArray()) w.Close();
            _session = restored;
            _modified = false;
            ShowWorkspace("自动测试");
            SetStatus("已打开 UI 会话：" + dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void SaveSession()
    {
        string? path = _session.SessionFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存纯 UI 体验会话（不保存正式 TEST/PROJECT）",
                Filter = "UI 会话 (*.autotest-ui.json)|*.autotest-ui.json",
                FileName = "autotest-session.autotest-ui.json", AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }
        try
        {
            _session.Save(path!);
            _modified = false;
            _contextName.Text = _session.ProjectName;
            SetStatus("UI 会话已保存。这不是正式工程或 ECU 测试结果。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S) { SaveSession(); e.Handled = true; }
        if (e.Key == Key.O) { OpenSession(); e.Handled = true; }
        if (e.Key == Key.N) { NewSession(); e.Handled = true; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        _closing = true;
        foreach (var window in _tools.Values.ToArray()) window.Close();
    }
}
