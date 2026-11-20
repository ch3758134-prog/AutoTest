using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>
/// Editable TEST interaction prototype. The UI document intentionally does not implement
/// production TestDocument, StepId, operation authorization, or a shadow TEST executor.
/// </summary>
public sealed class TestWorkspace : Grid
{
    private readonly UiSession _state;
    private readonly Action _changed;
    private readonly Action<string> _status;
    private readonly ListBox _cases = new();
    private readonly TreeView _steps = new();
    private readonly StackPanel _inspector = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly ComboBox _type = new();
    private readonly TextBox _caseName = UiKit.Input();
    private TextBox _stepName = UiKit.Input();
    private TextBox _target = UiKit.Input();
    private TextBox _value = UiKit.Input();
    private TextBox _condition = UiKit.Input();
    private TextBox _note = UiKit.Input("", true);
    private readonly TextBlock _count = UiKit.Text("0 步", 12, false, UiKit.Muted);
    private UiTestCase? _currentCase;
    private UiStep? _selected;
    private bool _rebinding;
    private readonly string[] _types =
    [
        "等待", "CAN 单次发送", "CAN 周期发送", "停止周期发送", "CAN 信号读取",
        "CAN 信号判定", "等待响应 WHEN", "条件 IF / ELSE", "SWITCH", "CASE",
        "循环", "TRY / CATCH", "设置变量", "计算", "期望判定", "步骤分组",
        "调用公共流程", "模拟外部输入", "IGN 控制", "夹具控制", "UDS 请求",
        "ECU 烧录", "波形开始", "波形停止", "波形分析", "波形判定",
        "日志记录", "中断 / 跳过"
    ];
    public TestWorkspace(UiSession state, Action changed, Action<string> status)
    {
        _state = state; _changed = changed; _status = status;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(UiKit.Header("自动测试", "测试项目 · 测试用例 · 步骤设计 · 流程控制 · 运行准备"));
        var tabs = new TabControl { Background = UiKit.White, BorderBrush = UiKit.Outline };
        Grid.SetRow(tabs, 1);
        Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header = "  测试程序编辑  ", Content = BuildEditor() });
        tabs.Items.Add(new TabItem { Header = "  当次运行与调试  ", Content = BuildRun() });
        tabs.Items.Add(new TabItem { Header = "  公共流程  ", Content = BuildFlows() });
        _cases.ItemsSource = _state.Cases;
        _cases.DisplayMemberPath = nameof(UiTestCase.Name);
        _cases.SelectionChanged += (_, _) =>
        {
            if (_rebinding) return;
            ApplyStep(false);
            _currentCase = _cases.SelectedItem as UiTestCase;
            _selected = null;
            _caseName.Text = _currentCase?.Name ?? "";
            RebuildSteps();
        };
        if (_state.Cases.Count > 0) _cases.SelectedIndex = 0;
        _steps.SelectedItemChanged += (_, _) =>
        {
            if (_rebinding) return;
            ApplyStep(false);
            _selected = (_steps.SelectedItem as TreeViewItem)?.Tag as UiStep;
            ShowInspector();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
    }

    private UIElement BuildEditor()
    {
        var g = UiKit.Columns(230, 0, 314);
        g.Margin = new Thickness(12);
        var left = new DockPanel { Margin = new Thickness(0, 0, 9, 0) };
        g.Children.Add(UiKit.Card(left, new Thickness(12)));
        var leftTop = new StackPanel();
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        leftTop.Children.Add(UiKit.Text("测试用例", 16, true));
        leftTop.Children.Add(UiKit.Actions(
            UiKit.Button("新建", () =>
            {
                Snapshot();
                var item = new UiTestCase { Name = "新建测试用例" };
                _state.Cases.Add(item); _cases.SelectedItem = item; _changed();
            }, true),
            UiKit.Button("复制", DuplicateCase),
            UiKit.Button("删除", DeleteCase)));
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        left.Children.Add(bottom);
        bottom.Children.Add(UiKit.Text("选中用例名称", 12, false, UiKit.Muted));
        bottom.Children.Add(_caseName);
        bottom.Children.Add(UiKit.Actions(UiKit.Button("应用名称", () =>
        {
            if (_currentCase is null || string.IsNullOrWhiteSpace(_caseName.Text)) return;
            Snapshot(); _currentCase.Name = _caseName.Text.Trim();
            _cases.Items.Refresh(); _changed(); _status("用例名称已更新（UI 会话）。");
        })));
        _cases.BorderThickness = new Thickness(0);
        _cases.Margin = new Thickness(0, 8, 0, 10);
        left.Children.Add(_cases);

        var middle = new DockPanel { Margin = new Thickness(0, 0, 9, 0) };
        Grid.SetColumn(middle, 1);
        g.Children.Add(UiKit.Card(middle, new Thickness(12)));
        var midTop = new StackPanel();
        DockPanel.SetDock(midTop, Dock.Top);
        middle.Children.Add(midTop);
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        titleRow.Children.Add(UiKit.Text("步骤流程", 16, true));
        DockPanel.SetDock(_count, Dock.Right);
        titleRow.Children.Add(_count);
        midTop.Children.Add(titleRow);
        _type.ItemsSource = _types;
        _type.SelectedIndex = 0;
        _type.MinWidth = 190;
        _type.Margin = new Thickness(0, 0, 0, 9);
        midTop.Children.Add(_type);
        midTop.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 插入步骤", AddStep, true),
            UiKit.Button("插入子步骤", AddChild),
            UiKit.Button("复制", DuplicateStep),
            UiKit.Button("删除", DeleteStep)));
        midTop.Children.Add(UiKit.Actions(
            UiKit.Button("上移", () => MoveStep(-1)),
            UiKit.Button("下移", () => MoveStep(+1)),
            UiKit.Button("撤销", Undo),
            UiKit.Button("重做", Redo)));
        middle.Children.Add(_steps);
        _steps.BorderThickness = new Thickness(0);
        _steps.Background = UiKit.White;
        _steps.Margin = new Thickness(0, 5, 0, 0);
        _steps.ContextMenu = new ContextMenu();
        foreach (var (caption, action) in new (string, Action)[]
        { ("插入步骤", AddStep), ("插入子步骤", AddChild), ("复制步骤", DuplicateStep),
          ("删除步骤", DeleteStep), ("上移", () => MoveStep(-1)),
          ("下移", () => MoveStep(1)) })
        {
            var menu = new MenuItem { Header = caption };
            menu.Click += (_, _) => action();
            _steps.ContextMenu.Items.Add(menu);
        }
        var right = UiKit.Card(UiKit.Scroll(_inspector), new Thickness(14));
        Grid.SetColumn(right, 2); g.Children.Add(right);
        ShowInspector();
        return g;
    }
    private void ShowInspector()
    {
        _inspector.Children.Clear();
        // A WPF input can belong to only one logical parent. Fresh controls preserve
        // each editor's focus/selection lifecycle without stale parent references.
        _stepName = UiKit.Input();
        _target = UiKit.Input();
        _value = UiKit.Input();
        _condition = UiKit.Input();
        _note = UiKit.Input("", true);
        _inspector.Children.Add(UiKit.Text("步骤详情", 17, true));
        _inspector.Children.Add(UiKit.Note("此处修改属于 UI 体验草稿；不会更改真实 TEST 程序或触发设备动作。"));
        if (_selected is null)
        {
            _inspector.Children.Add(UiKit.Text("请选择步骤。也可以先选准备 / 测试 / 清理阶段，然后选择操作类型添加。", 13, false, UiKit.Muted));
            return;
        }
        _stepName.Text = _selected.Name;
        _target.Text = _selected.Target;
        _value.Text = _selected.Value;
        _condition.Text = _selected.Condition;
        _note.Text = _selected.Note;
        var definition = StepInspectorCatalog.For(_selected.Type);
        _inspector.Children.Add(UiKit.Text(definition.Title, 14, true, UiKit.Accent));
        _inspector.Children.Add(UiKit.Text(definition.Help, 12, false, UiKit.Muted));
        _inspector.Children.Add(UiKit.Field("步骤名称", _stepName));
        if (definition.TargetLabel != "—")
            _inspector.Children.Add(UiKit.Field(definition.TargetLabel, _target));
        if (definition.ValueLabel != "—")
            _inspector.Children.Add(UiKit.Field(definition.ValueLabel, _value));
        if (definition.ConditionLabel != "—")
            _inspector.Children.Add(UiKit.Field(definition.ConditionLabel, _condition));
        _inspector.Children.Add(UiKit.Field("备注", _note));
        var enabled = new CheckBox { Content = "此步骤启用", IsChecked = _selected.Enabled,
            Margin = new Thickness(0, 0, 0, 12) };
        var currentStep = _selected;
        enabled.Checked += (_, _) => { currentStep.Enabled = true; _changed(); };
        enabled.Unchecked += (_, _) => { currentStep.Enabled = false; _changed(); };
        _inspector.Children.Add(enabled);
        if (definition.Branches is { Length: > 0 } branches)
        {
            _inspector.Children.Add(UiKit.Text("子分支编辑", 13, true));
            var branchPicker = new ComboBox { ItemsSource = branches, SelectedIndex = 0,
                MinHeight = 32, Margin = new Thickness(0, 8, 0, 8) };
            _inspector.Children.Add(branchPicker);
            _inspector.Children.Add(UiKit.Button("加入选中分支", () =>
            {
                if (branchPicker.SelectedItem is string selectedBranch)
                    InsertIntoBranch(currentStep, selectedBranch);
            }));
        }
        var references = new ComboBox { ItemsSource = _state.Messages.Select(x => x.Name).ToArray(),
            Margin = new Thickness(0, 0, 0, 9), MinHeight = 32 };
        references.SelectionChanged += (_, _) =>
        {
            if (references.SelectedItem is string name) _target.Text = name;
        };
        _inspector.Children.Add(UiKit.Field("从本 UI DBC 报文列表选择", references));
        _inspector.Children.Add(UiKit.Actions(
            UiKit.Button("应用修改", () => ApplyStep(true), true),
            UiKit.Button("复制步骤", DuplicateStep)));
        _inspector.Children.Add(UiKit.Text("编辑后点击应用；选中其他步骤时自动保留本轮输入。", 11, false, UiKit.Muted));
    }
    private void ApplyStep(bool announce)
    {
        if (_selected is null) return;
        // Incomplete input is retained in UI draft state instead of silently lost on navigation.
        if (string.IsNullOrWhiteSpace(_stepName.Text))
            _status("步骤名称尚未填写（仅 UI 草稿）；后续正式资源保存应进行合法性校验。");
        if (_selected.Name == _stepName.Text && _selected.Target == _target.Text &&
            _selected.Value == _value.Text && _selected.Condition == _condition.Text &&
            _selected.Note == _note.Text) return;
        Snapshot();
        _selected.Name = _stepName.Text.Trim();
        _selected.Target = _target.Text;
        _selected.Value = _value.Text;
        _selected.Condition = _condition.Text;
        _selected.Note = _note.Text;
        _changed();
        if (announce) { RebuildSteps(_selected.Id); _status("步骤参数已保存至本地 UI 会话。"); }
    }
    private void Snapshot()
    {
        _undo.Push(JsonSerializer.Serialize(_state.Cases.ToArray()));
        if (_undo.Count > 100)
        {
            var mostRecent = _undo.ToArray().Take(90).Reverse().ToArray();
            _undo.Clear();
            foreach (var entry in mostRecent) _undo.Push(entry);
        }
        _redo.Clear();
    }
    private void Restore(string json)
    {
        var data = JsonSerializer.Deserialize<List<UiTestCase>>(json) ?? [];
        _rebinding = true;
        try
        {
            _state.Cases.Clear();
            foreach (var item in data) _state.Cases.Add(item);
            _currentCase = _state.Cases.FirstOrDefault();
            _selected = null;
            _cases.SelectedItem = _currentCase;
            _caseName.Text = _currentCase?.Name ?? "";
        }
        finally { _rebinding = false; }
        RebuildSteps();
        _changed();
    }
    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(JsonSerializer.Serialize(_state.Cases.ToArray()));
        Restore(_undo.Pop()); _status("已撤销 UI 草稿编辑。");
    }
    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(JsonSerializer.Serialize(_state.Cases.ToArray()));
        Restore(_redo.Pop()); _status("已重做 UI 草稿编辑。");
    }
    private void RebuildSteps(string? preferId = null)
    {
        _rebinding = true;
        try
        {
            _steps.Items.Clear();
            foreach (var phase in new[] { "准备", "测试", "清理" })
            {
                var node = new TreeViewItem
                {
                    Header = phase + " 阶段", Tag = phase, IsExpanded = true,
                    Foreground = UiKit.Accent, FontWeight = FontWeights.SemiBold
                };
                _steps.Items.Add(node);
                if (_currentCase is not null)
                    BuildNodes(node, _currentCase.Steps.Where(s => s.Phase == phase), preferId);
            }
            _count.Text = (_currentCase is null ? 0 : CountSteps(_currentCase.Steps)) + " 步";
        }
        finally { _rebinding = false; }
        ShowInspector();
    }
    private static int CountSteps(IEnumerable<UiStep> steps) =>
        steps.Sum(s => 1 + CountSteps(s.Children));
    private void BuildNodes(ItemsControl parent, IEnumerable<UiStep> steps, string? preferId)
    {
        foreach (var step in steps)
        {
            var line = new TreeViewItem { Tag = step, IsExpanded = true,
                Header = (step.Enabled ? "" : "〔停用〕") +
                    (string.IsNullOrWhiteSpace(step.Branch) ? "" : "〔" + step.Branch + "〕 ") +
                    step.Type + " · " + step.Name };
            parent.Items.Add(line);
            if (step.Id == preferId) { _selected = step; line.IsSelected = true; }
            if (step.Children.Count > 0) BuildNodes(line, step.Children, preferId);
        }
    }
    private ObservableCollection<UiStep>? FindCollection(UiStep target, ObservableCollection<UiStep> items)
    {
        if (items.Contains(target)) return items;
        foreach (var step in items)
        {
            var result = FindCollection(target, step.Children);
            if (result is not null) return result;
        }
        return null;
    }
    private void AddStep() => Insert(false);
    private void AddChild() => Insert(true);
    private void Insert(bool child)
    {
        if (_currentCase is null) { _status("先新建一个测试用例。"); return; }
        ApplyStep(false);
        var kind = _type.SelectedItem as string ?? "等待";
        var selected = (_steps.SelectedItem as TreeViewItem)?.Tag;
        var phase = selected is string p ? p : _selected?.Phase ?? "测试";
        var list = _currentCase.Steps;
        var pos = list.Count;
        if (selected is UiStep parent)
        {
            if (child)
            {
                list = parent.Children;
                pos = list.Count;
            }
            else
            {
                list = FindCollection(parent, _currentCase.Steps) ?? _currentCase.Steps;
                pos = list.IndexOf(parent) + 1;
            }
        }
        else if (selected is string phaseLabel)
        {
            pos = list.TakeWhile(s => s.Phase != phaseLabel).Count();
            pos = list.Where(s => s.Phase == phaseLabel).LastOrDefault() is { } last
                ? list.IndexOf(last) + 1 : pos;
        }
        Snapshot();
        var node = StepInspectorCatalog.New(kind, phase, child ? "BODY" : "");
        list.Insert(Math.Clamp(pos, 0, list.Count), node);
        _selected = node;
        RebuildSteps(node.Id);
        _changed();
        _status("已插入 " + kind + "，现在可在右侧直接编辑参数。");
    }
    private void InsertIntoBranch(UiStep parent, string branch)
    {
        if (_currentCase is null) return;
        ApplyStep(false);
        var kind = _type.SelectedItem as string ?? "等待";
        Snapshot();
        var node = StepInspectorCatalog.New(kind, parent.Phase, branch);
        // Keep each branch's children together without changing the parent node identity.
        var last = parent.Children.Select((step, index) => (step, index))
            .Where(x => x.step.Branch == branch).Select(x => x.index).LastOrDefault(-1);
        parent.Children.Insert(last < 0 ? parent.Children.Count : last + 1, node);
        _selected = node;
        RebuildSteps(node.Id);
        _changed();
        _status("已将步骤加入 " + branch + " 分支，可在右侧继续编辑。");
    }

    private void DuplicateStep()
    {
        if (_selected is null || _currentCase is null) return;
        ApplyStep(false);
        var list = FindCollection(_selected, _currentCase.Steps);
        if (list is null) return;
        Snapshot();
        var clone = JsonSerializer.Deserialize<UiStep>(JsonSerializer.Serialize(_selected))!;
        RenewIds(clone); clone.Name += " - 副本";
        list.Insert(list.IndexOf(_selected) + 1, clone);
        _selected = clone;
        RebuildSteps(clone.Id); _changed();
    }
    private static void RenewIds(UiStep node)
    {
        node.Id = Guid.NewGuid().ToString("N");
        foreach (var child in node.Children) RenewIds(child);
    }
    private void DeleteStep()
    {
        if (_selected is null || _currentCase is null) return;
        var list = FindCollection(_selected, _currentCase.Steps);
        if (list is null) return;
        Snapshot(); list.Remove(_selected);
        _selected = null; RebuildSteps(); _changed();
    }
    private void MoveStep(int direction)
    {
        if (_selected is null || _currentCase is null) return;
        ApplyStep(false);
        var list = FindCollection(_selected, _currentCase.Steps);
        if (list is null) return;
        var i = list.IndexOf(_selected);
        var next = i + direction;
        if (next < 0 || next >= list.Count) return;
        Snapshot(); list.Move(i, next);
        RebuildSteps(_selected.Id); _changed();
    }
    private void DuplicateCase()
    {
        if (_currentCase is null) return;
        Snapshot();
        var clone = JsonSerializer.Deserialize<UiTestCase>(JsonSerializer.Serialize(_currentCase))!;
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name += " - 副本";
        foreach (var node in clone.Steps) RenewIds(node);
        _state.Cases.Insert(_state.Cases.IndexOf(_currentCase) + 1, clone);
        _cases.SelectedItem = clone; _changed();
    }
    private void DeleteCase()
    {
        if (_currentCase is null) return;
        Snapshot();
        _state.Cases.Remove(_currentCase);
        _cases.SelectedIndex = _state.Cases.Count > 0 ? 0 : -1;
        _changed();
    }
    private UIElement BuildRun()
    {
        var g = UiKit.Columns(360, 0);
        g.Margin = new Thickness(15);
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        g.Children.Add(UiKit.Card(left));
        var text = UiKit.Text("本次运行选择", 17, true);
        DockPanel.SetDock(text, Dock.Top);
        left.Children.Add(text);
        var info = UiKit.Note("勾选只影响 UI 中的当次选择，不会修改真实 TEST 用例或执行配置。");
        DockPanel.SetDock(info, Dock.Top);
        left.Children.Add(info);
        var cases = new DataGrid
        {
            ItemsSource = _state.Cases, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, BorderBrush = UiKit.Outline, Background = UiKit.White,
            RowHeight = 38
        };
        cases.Columns.Add(new DataGridCheckBoxColumn { Header = "选择", Binding = new System.Windows.Data.Binding("RunSelected") });
        cases.Columns.Add(new DataGridTextColumn { Header = "测试用例", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        cases.CellEditEnding += (_, _) => _changed();
        left.Children.Add(cases);
        var right = new StackPanel { Margin = new Thickness(5, 3, 0, 0) };
        Grid.SetColumn(right, 1);
        g.Children.Add(UiKit.Card(UiKit.Scroll(right)));
        right.Children.Add(UiKit.Text("执行监视", 19, true));
        right.Children.Add(UiKit.Note("此版本只验证运行界面的布局、选择和操作流。真实工位预检、执行及报告服务尚未接线。", true));
        var buttons = UiKit.Actions(
            UiKit.Button("查看本次选择", () =>
            {
                var count = _state.Cases.Count(x => x.RunSelected);
                _status("本次 UI 选择：" + count + " 个用例；未产生正式测试任务。");
            }),
            UiKit.Button("运行测试", () => { }, true),
            UiKit.Button("停止执行", () => { }));
        foreach (var b in buttons.Children.OfType<Button>().Skip(1))
            UiKit.Disabled(b, "纯 UI 阶段未连接真实 TEST/DEVICE 执行器");
        right.Children.Add(buttons);
        right.Children.Add(UiKit.Text("当前步骤", 14, true));
        right.Children.Add(UiKit.Empty("尚无执行数据", "接线后在此显示真实 StepOccurrence、阶段与对应证据。"));
        right.Children.Add(UiKit.Text("执行事件", 14, true));
        right.Children.Add(UiKit.Empty("暂无真实运行事件", "UI 不会产生虚假的测试成功、失败、设备响应或报告。"));
        return g;
    }
    private UIElement BuildFlows()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(UiKit.Text("公共流程库", 18, true));
        top.Children.Add(UiKit.Note("公共流程是独立 UI 设计对象，正式 TEST 依赖和版本调用将在后续集成，不在本版伪造。"));
        var table = UiKit.Table(_state.CommonFlows,
            ("名称", "Name", 230), ("类型", "Type", 160), ("描述", "Note", 0));
        table.CellEditEnding += (_, _) => _changed();
        top.Children.Add(UiKit.Actions(
            UiKit.Button("新建公共流程", () =>
            {
                _state.CommonFlows.Add(new UiStep { Type = "公共流程", Name = "新建公共流程" });
                _changed();
            }, true),
            UiKit.Button("复制流程", () =>
            {
                if (table.SelectedItem is not UiStep item) return;
                var clone = JsonSerializer.Deserialize<UiStep>(JsonSerializer.Serialize(item))!;
                RenewIds(clone); clone.Name += " - 副本";
                _state.CommonFlows.Add(clone); _changed();
            }),
            UiKit.Button("删除流程", () =>
            {
                if (table.SelectedItem is UiStep item) { _state.CommonFlows.Remove(item); _changed(); }
            })));
        root.Children.Add(table);
        return root;
    }
}
