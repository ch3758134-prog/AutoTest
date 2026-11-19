using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Core;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Unbound TEST authoring session. Uses the production TestDocument/StepEditTransaction
/// and never fabricates a PROJECT asset, hardware connection or runnable case.
/// Export is explicitly an unregistered draft, NOT ProjectWorkspaceService.Save.
/// </summary>
public sealed class UnboundTestEditorWindow : Window
{
    private StepEditTransaction _session;
    private TestDocument _exported;
    private readonly TreeView _tree = new();
    private readonly TextBox _name = new();
    private readonly StackPanel _fields = new();
    private readonly ComboBox _types = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _heading = new();
    private readonly TextBlock _dirty = new();
    private readonly List<(string Key, TextBox Input)> _inputs = [];
    private Selection? _selected;
    private bool _refreshing;

    private sealed record Selection(TestPhase Phase, StepId? Parent, string? Slot, StepId? Node, int Index);
    public UnboundTestEditorWindow()
    {
        _session = new StepEditTransaction(TestDocument.Create("未归档测试用例"));
        _exported = _session.Current;
        Title = "测试用例编辑 · 未归档 | AutoTest";
        Width = 1120;
        Height = 740;
        MinWidth = 800;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ColorBrush("#F5F8FC");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;

        var layout = new DockPanel();
        Content = layout;
        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 10) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "未归档测试用例 · 正式 TEST 编辑器", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = ColorBrush("#203A5C") });
        header.Children.Add(new TextBlock
        {
            Text = "使用正式 TestDocument / StepEditTransaction。可离线编辑与导出草稿；未加入工程前禁止实体运行。",
            Foreground = ColorBrush("#657A92"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
        });

        var toolbar = new WrapPanel { Margin = new Thickness(18, 0, 18, 10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        AddButton(toolbar, "新建草稿", (_, _) => NewDraft());
        AddButton(toolbar, "打开草稿", (_, _) => OpenDraft());
        AddButton(toolbar, "导出草稿 JSON", (_, _) => ExportDraft());
        AddButton(toolbar, "撤销 Ctrl+Z", (_, _) => ChangeHistory(true));
        AddButton(toolbar, "重做 Ctrl+Y", (_, _) => ChangeHistory(false));
        _dirty.Margin = new Thickness(14, 8, 0, 0);
        _dirty.Foreground = ColorBrush("#9B6424");
        toolbar.Children.Add(_dirty);

        var body = new Grid { Margin = new Thickness(18, 0, 18, 12) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360), MinWidth = 260 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(body);
        var splitter = new GridSplitter { Width = 5, Background = ColorBrush("#E3EAF3"), HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        var left = new DockPanel { Background = Brushes.White };
        Grid.SetColumn(left, 0);
        body.Children.Add(left);
        var tools = new StackPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(tools, Dock.Top);
        left.Children.Add(tools);
        tools.Children.Add(new TextBlock { Text = "步骤结构", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _types.ItemsSource = StepUiCatalog.Choices;
        _types.SelectedIndex = 0;
        _types.Margin = new Thickness(0, 0, 0, 8);
        tools.Children.Add(_types);
        var editTools = new WrapPanel();
        tools.Children.Add(editTools);
        AddButton(editTools, "＋ 添加", (_, _) => Insert());
        AddButton(editTools, "复制", (_, _) => Duplicate());
        AddButton(editTools, "删除", (_, _) => Delete());
        _tree.Margin = new Thickness(8);
        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_refreshing) return;
            if (!CommitSelected()) { RestoreSelection(); return; }
            _selected = (_tree.SelectedItem as TreeViewItem)?.Tag as Selection;
            ShowSelection();
        };
        left.Children.Add(_tree);

        var right = new DockPanel { Background = Brushes.White, Margin = new Thickness(9, 0, 0, 0) };
        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        var rightTop = new StackPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(rightTop, Dock.Top);
        right.Children.Add(rightTop);
        _heading.FontSize = 17;
        _heading.FontWeight = FontWeights.SemiBold;
        rightTop.Children.Add(_heading);
        rightTop.Children.Add(new TextBlock { Text = "步骤名称", Margin = new Thickness(0, 18, 0, 5), Foreground = ColorBrush("#617790") });
        _name.Padding = new Thickness(8);
        rightTop.Children.Add(_name);
        var rightActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        rightTop.Children.Add(rightActions);
        AddButton(rightActions, "应用本步骤修改", (_, _) => CommitSelected(true));
        AddButton(rightActions, "添加到当前分支", (_, _) => Insert(true));
        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = ColorBrush("#617790");
        rightTop.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        right.Children.Add(scroll);
        _fields.Margin = new Thickness(18, 0, 18, 14);
        scroll.Content = _fields;

        PreviewKeyDown += OnEditorKeyDown;
        Closing += OnEditorClosing;
        RefreshTree(null);
        SetStatus("选择准备 / 测试 / 清理阶段，选择操作类型后点击添加。");
    }

    private static Brush ColorBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static void AddButton(Panel parent, string caption, RoutedEventHandler handler)
    {
        var button = new Button { Content = caption, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 7, 6), MinHeight = 32 };
        button.Click += handler;
        parent.Children.Add(button);
    }
    private static string Label(TestPhase phase) => StepUiCatalog.PhaseLabel(phase);
    private void SetStatus(string value) => _status.Text = value;
    private StepNode? SelectedNode => _selected?.Node is { } id ? _session.Current.Walk().FirstOrDefault(n => n.Id == id) : null;
    private bool Dirty => TestDocumentCodec.Serialize(_session.Current) != TestDocumentCodec.Serialize(_exported);

    private void RefreshTree(StepId? prefer)
    {
        _refreshing = true;
        try
        {
            _tree.Items.Clear();
            foreach (var phase in Enum.GetValues<TestPhase>())
            {
                var root = new TreeViewItem
                {
                    Header = Label(phase),
                    Tag = new Selection(phase, null, null, null, 0),
                    IsExpanded = true
                };
                _tree.Items.Add(root);
                Populate(root, phase, null, null, _session.Current.Get(phase));
            }
            _dirty.Text = Dirty ? "● 有未导出的修改" : "草稿已导出 / 无修改";
            if (prefer is { } id) SelectById(_tree.Items.OfType<TreeViewItem>(), id);
        }
        finally { _refreshing = false; }
        ShowSelection();
    }
    private void Populate(ItemsControl parent, TestPhase phase, StepId? parentId, string? slot,
        IEnumerable<StepNode> nodes)
    {
        var index = 0;
        foreach (var node in nodes)
        {
            var item = new TreeViewItem
            {
                Header = $"{StepUiCatalog.Label(node.Type)} · {node.Name}",
                Tag = new Selection(phase, parentId, slot, node.Id, index++),
                IsExpanded = true
            };
            parent.Items.Add(item);
            foreach (var pair in node.Slots)
            {
                var branch = new TreeViewItem
                {
                    Header = StepUiCatalog.SlotLabel(pair.Key),
                    Tag = new Selection(phase, node.Id, pair.Key, null, 0),
                    Foreground = ColorBrush("#54759B"),
                    IsExpanded = true
                };
                item.Items.Add(branch);
                Populate(branch, phase, node.Id, pair.Key, pair.Value);
            }
        }
    }
    private bool SelectById(IEnumerable<TreeViewItem> items, StepId id)
    {
        foreach (var item in items)
        {
            if (item.Tag is Selection { Node: { } node } && node == id)
            {
                _selected = item.Tag as Selection;
                item.IsSelected = true;
                item.BringIntoView();
                return true;
            }
            if (SelectById(item.Items.OfType<TreeViewItem>(), id)) return true;
        }
        return false;
    }
    private void RestoreSelection()
    {
        if (_selected?.Node is { } id) RefreshTree(id);
    }
    private void ShowSelection()
    {
        _inputs.Clear();
        _fields.Children.Clear();
        var node = SelectedNode;
        _name.IsEnabled = node is not null;
        if (node is null)
        {
            _name.Text = "";
            _heading.Text = _selected is null ? "选择一个阶段或步骤" : "当前步骤容器";
            SetStatus("选择操作类型后点击“添加”。选择步骤可在此连续编辑参数。");
            return;
        }
        _heading.Text = StepUiCatalog.Label(node.Type);
        _name.Text = node.Name;
        foreach (var prop in node.Properties.OrderBy(p => p.Key))
        {
            var label = new TextBlock { Text = StepUiCatalog.IsOperatorProperty(prop.Key) ? StepUiCatalog.PropertyLabel(prop.Key) : prop.Key,
                Foreground = ColorBrush("#617790"), Margin = new Thickness(0, 11, 0, 5) };
            var input = new TextBox { Text = prop.Value, Padding = new Thickness(8), Tag = prop.Key };
            _fields.Children.Add(label);
            _fields.Children.Add(input);
            _inputs.Add((prop.Key, input));
        }
        if (node.Properties.Count == 0)
            _fields.Children.Add(new TextBlock { Text = "此步骤暂无参数。", Foreground = ColorBrush("#71839A"), Margin = new Thickness(0, 14, 0, 0) });
        SetStatus("编辑名称和参数后点击“应用”；修改属于正式 TEST 事务，可撤销。");
    }
    private bool CommitSelected(bool showMessage = false)
    {
        if (SelectedNode is not { } node) return true;
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            SetStatus("步骤名称不能为空。请完成输入再切换。");
            return false;
        }
        var updated = _inputs.ToDictionary(p => p.Key, p => p.Input.Text, StringComparer.Ordinal);
        if (node.Name == _name.Text && node.Properties.Count == updated.Count &&
            updated.All(p => node.Properties.TryGetValue(p.Key, out var value) && value == p.Value))
            return true;
        try
        {
            _session.ApplyProperties(node.Id, _name.Text, updated);
            _dirty.Text = "● 有未导出的修改";
            if (showMessage) SetStatus("已应用到正式 TEST 文档草稿。");
            // Rebuild only when explicitly applied: never erase a half-typed input on each keystroke.
            if (showMessage) RefreshTree(node.Id);
            return true;
        }
        catch (Exception error)
        {
            SetStatus("修改未应用：" + error.Message);
            return false;
        }
    }
    private void Insert(bool asChild = false)
    {
        if (!CommitSelected()) return;
        if (_selected is not { } target)
        {
            SetStatus("请先选择准备、测试、清理或一个子分支。");
            return;
        }
        if (_types.SelectedItem is not StepUiChoice type) return;
        var parent = target.Parent;
        var slot = target.Slot;
        var count = target.Parent is null ? _session.Current.Get(target.Phase).Length
            : _session.Current.Walk().First(n => n.Id == target.Parent).Slots[target.Slot!].Length;
        if (target.Node is { } node)
        {
            if (asChild)
            {
                var current = _session.Current.Walk().First(n => n.Id == node);
                var firstSlot = current.Slots.Keys.FirstOrDefault();
                if (firstSlot is null) { SetStatus("这个步骤没有子分支，请选中它后使用“添加”插入同级步骤。"); return; }
                parent = node;
                slot = firstSlot;
                count = current.Slots[firstSlot].Length;
            }
            else count = target.Index + 1;
        }
        try
        {
            var created = StepUiCatalog.NewNode(type.Type);
            _session.Insert(target.Phase, parent, slot, count, created);
            RefreshTree(created.Id);
            SetStatus("已添加步骤；请在右侧完成参数。未绑定资源的步骤不可真实运行。");
        }
        catch (Exception error) { SetStatus("无法添加：" + error.Message); }
    }
    private void Duplicate()
    {
        if (!CommitSelected() || _selected is not { Node: { } id } target) return;
        try
        {
            _session.Copy(id, target.Phase, target.Parent, target.Slot, target.Index + 1);
            RefreshTree(null);
            SetStatus("已复制步骤，生成新的 StepId；可撤销。");
        }
        catch (Exception error) { SetStatus("无法复制：" + error.Message); }
    }
    private void Delete()
    {
        if (_selected is not { Node: { } id }) return;
        try
        {
            _session.Delete(id);
            _selected = null;
            RefreshTree(null);
            SetStatus("步骤已删除，可撤销。");
        }
        catch (Exception error) { SetStatus("无法删除：" + error.Message); }
    }
    private void ChangeHistory(bool undo)
    {
        if (!CommitSelected()) return;
        if (undo ? !_session.CanUndo : !_session.CanRedo) return;
        if (undo) _session.Undo(); else _session.Redo();
        _selected = null;
        RefreshTree(null);
        SetStatus(undo ? "已撤销。" : "已重做。");
    }
    private void NewDraft()
    {
        if (!CommitSelected() || !ConfirmDiscard()) return;
        _session = new StepEditTransaction(TestDocument.Create("未归档测试用例"));
        _exported = _session.Current;
        _selected = null;
        RefreshTree(null);
    }
    private bool ConfirmDiscard() => !Dirty || MessageBox.Show(this,
        "当前有未导出的草稿修改。确实要放弃这些修改吗？",
        "未归档测试用例", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private void OpenDraft()
    {
        if (!CommitSelected() || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "TEST 草稿 JSON|*.json", Title = "打开未归档 TEST 草稿" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var doc = TestDocumentCodec.Deserialize(File.ReadAllText(dialog.FileName));
            _session = new StepEditTransaction(doc);
            _exported = doc;
            _selected = null;
            RefreshTree(null);
            SetStatus("已加载 TEST 文档草稿；不代表已经导入正式工程。");
        }
        catch (Exception error) { SetStatus("草稿无效：" + error.Message); }
    }
    private void ExportDraft()
    {
        if (!CommitSelected()) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出未归档 TEST 草稿（不是正式工程保存）",
            Filter = "TEST 草稿 JSON|*.json",
            FileName = "test-draft.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var json = TestDocumentCodec.Serialize(_session.Current);
            // Round trip through the actual codec before writing, never falsely mark invalid data saved.
            TestDocumentCodec.Deserialize(json);
            File.WriteAllText(dialog.FileName, json);
            _exported = _session.Current;
            _dirty.Text = "草稿已导出 / 无修改";
            SetStatus("已导出合法 TEST 草稿。正式工程归档仍须通过 PROJECT 的资源服务。");
        }
        catch (Exception error) { SetStatus("导出失败，修改仍保留：" + error.Message); }
    }
    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.Z) { ChangeHistory(true); e.Handled = true; }
        else if (e.Key == Key.Y) { ChangeHistory(false); e.Handled = true; }
        else if (e.Key == Key.S) { ExportDraft(); e.Handled = true; }
    }
    private void OnEditorClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!CommitSelected()) { e.Cancel = true; return; }
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
