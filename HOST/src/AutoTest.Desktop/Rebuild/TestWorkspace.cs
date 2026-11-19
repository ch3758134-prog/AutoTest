using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Rebuild;

/// <summary>Self-contained visual authoring. No TEST runtime, ProjectId, USB, or fake execution facts.</summary>
public sealed class TestWorkspace : UserControl
{
    private readonly UiStudyStore _store;
    private readonly Action<string> _status;
    private readonly UiEditHistory<UiStudy> _history = new();
    private readonly StackPanel _objects = UiVisual.Stack();
    private readonly TreeView _steps = new();
    private readonly StackPanel _inspector = UiVisual.Stack();
    private readonly ContentControl _editorContent = new();
    private readonly ComboBox _palette = UiVisual.Combo(210);
    private readonly ComboBox _mode = UiVisual.Combo(140);
    private readonly TextBlock _editorTitle = UiVisual.Text("", 16, true);
    private readonly TextBlock _count = UiVisual.Text("", 12, false, UiVisual.Muted);
    private readonly TextBlock _localStatus = UiVisual.Note("");
    private readonly List<(string Key, TextBox Field)> _parameterInputs = [];
    private TextBox? _nameField;
    private CheckBox? _enabledField;
    private Guid? _caseId;
    private Guid? _flowId;
    private Guid? _stepId;
    private StepLocation? _selectedLocation;
    private bool _refreshing;

    private sealed record StepLocation(string Phase, UiStep? Step, List<UiStep> Container, int Index);
    private UiTestCase? CurrentCase => _store.Current.TestCases.FirstOrDefault(x => x.Id == _caseId);
    private UiPublicFlow? CurrentFlow => _store.Current.PublicFlows.FirstOrDefault(x => x.Id == _flowId);
    private bool IsFlowMode => _mode.SelectedIndex == 1;

    public TestWorkspace(UiStudyStore store, Guid? preferredCase, Action<string> status)
    {
        _store = store; _status = status;
        _caseId = preferredCase ?? store.Current.TestCases.FirstOrDefault()?.Id;
        var layout = UiVisual.Columns(new GridLength(224), new GridLength(5), UiVisual.Star());
        Content = layout;
        var left = UiVisual.Stack();
        left.Children.Add(UiVisual.Text("测试项目", 16, true));
        var hint = UiVisual.Note("勾选仅表示这次准备运行；不会修改用例默认启用状态。");
        hint.Margin = new Thickness(0, 7, 0, 11);
        left.Children.Add(hint);
        _mode.Items.Add("测试用例");
        _mode.Items.Add("公共流程");
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _flowId = IsFlowMode ? _store.Current.PublicFlows.FirstOrDefault()?.Id : null;
            if (!IsFlowMode && _caseId is null) _caseId = _store.Current.TestCases.FirstOrDefault()?.Id;
            _stepId = null;
            RefreshObjects(); RefreshTree();
        };
        left.Children.Add(_mode);
        left.Children.Add(UiVisual.Toolbar(
            UiVisual.Button("＋ 新建", NewObject, true),
            UiVisual.Button("复制", DuplicateObject)
        ));
        left.Children.Add(UiVisual.Scroll(_objects));
        UiVisual.At(layout, UiVisual.Box(left, new Thickness(12)), 0);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, Background = UiVisual.Line };
        UiVisual.At(layout, splitter, 1);

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(59) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        UiVisual.At(layout, main, 2);
        var header = UiVisual.Stack(true);
        header.Margin = new Thickness(13, 10, 0, 6);
        header.Children.Add(_editorTitle);
        var counter = _count; counter.Margin = new Thickness(18, 0, 0, 0);
        header.Children.Add(counter);
        UiVisual.At(main, header, 0, 0);

        var tabs = new TabControl { Background = UiVisual.Back, BorderThickness = new Thickness(0),
            Margin = new Thickness(9, 0, 0, 0) };
        var editor = new TabItem { Header = "程序编辑", Padding = new Thickness(14, 6, 14, 6) };
        var run = new TabItem { Header = "运行准备 / 调试", Padding = new Thickness(14, 6, 14, 6) };
        tabs.Items.Add(editor); tabs.Items.Add(run); tabs.SelectedIndex = 0;
        tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, tabs) && ReferenceEquals(tabs.SelectedItem, run))
                run.Content = BuildRunView();
        };
        var editRoot = new DockPanel { LastChildFill = true };
        var actions = UiVisual.Stack(true);
        actions.Margin = new Thickness(5, 3, 0, 4);
        _palette.ItemsSource = UiStepTypes.All.Select(x => new StepChoice(x.Type, x.Label)).ToList();
        _palette.DisplayMemberPath = nameof(StepChoice.Label);
        _palette.SelectedIndex = 0;
        actions.Children.Add(_palette);
        actions.Children.Add(UiVisual.Button("＋ 添加步骤", () => Insert(false), true));
        actions.Children.Add(UiVisual.Button("↳ 添加子步骤", () => Insert(true)));
        actions.Children.Add(UiVisual.Button("复制", DuplicateStep));
        actions.Children.Add(UiVisual.Button("删除", DeleteStep));
        actions.Children.Add(UiVisual.Button("↑", () => MoveStep(-1)));
        actions.Children.Add(UiVisual.Button("↓", () => MoveStep(1)));
        actions.Children.Add(UiVisual.Button("撤销", Undo));
        actions.Children.Add(UiVisual.Button("重做", Redo));
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = actions };
        DockPanel.SetDock(scroller, Dock.Top);
        editRoot.Children.Add(scroller);

        var split = UiVisual.Columns(UiVisual.Star(), new GridLength(5), new GridLength(306));
        split.Margin = new Thickness(0, 5, 0, 5);
        var treeZone = new DockPanel { LastChildFill = true };
        var caption = UiVisual.Note("单击定位 · 右键操作 · 当前选中决定插入位置");
        caption.Margin = new Thickness(10, 6, 0, 8);
        DockPanel.SetDock(caption, Dock.Top);
        treeZone.Children.Add(caption);
        _steps.BorderThickness = new Thickness(0);
        _steps.Margin = new Thickness(7);
        _steps.SelectedItemChanged += (_, _) =>
        {
            if (_refreshing) return;
            CommitInspector(false);
            _selectedLocation = (_steps.SelectedItem as TreeViewItem)?.Tag as StepLocation;
            _stepId = _selectedLocation?.Step?.Id;
            ShowInspector();
        };
        treeZone.Children.Add(_steps);
        UiVisual.At(split, UiVisual.Box(treeZone, new Thickness(0)), 0);
        UiVisual.At(split, new GridSplitter { Width = 5, Background = UiVisual.Line,
            HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns }, 1);
        var inspectorSurface = UiVisual.Scroll(_inspector);
        UiVisual.At(split, UiVisual.Box(inspectorSurface, new Thickness(10)), 2);
        var statusText = UiVisual.Box(_localStatus, new Thickness(8), UiVisual.Pale);
        statusText.Margin = new Thickness(2, 6, 2, 2);
        DockPanel.SetDock(statusText, Dock.Bottom);
        editRoot.Children.Add(statusText);
        editRoot.Children.Add(split);
        editor.Content = editRoot;

        run.Content = BuildRunView();
        UiVisual.At(main, tabs, 0, 1);
        RefreshObjects();
        RefreshTree();
    }

    private sealed record StepChoice(string Type, string Label);
    private void Mutate(Action action, string message)
    {
        _history.Record(_store.Current);
        action();
        _store.Save();
        _status(message);
        _localStatus.Text = message;
        RefreshObjects();
        RefreshTree();
    }

    private void RefreshObjects()
    {
        _objects.Children.Clear();
        if (IsFlowMode)
        {
            foreach (var flow in _store.Current.PublicFlows.ToList())
            {
                var b = UiVisual.Button(flow.Name, () => { _flowId = flow.Id; _stepId = null; RefreshTree(); });
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Background = _flowId == flow.Id ? UiVisual.SoftAccent : UiVisual.Surface;
                _objects.Children.Add(b);
            }
            if (_store.Current.PublicFlows.Count == 0) _objects.Children.Add(UiVisual.Note("尚无公共流程"));
        }
        else
        {
            foreach (var item in _store.Current.TestCases.ToList())
            {
                var row = UiVisual.Stack(true);
                row.Margin = new Thickness(0, 0, 0, 4);
                var check = new CheckBox { IsChecked = item.RunSelected, ToolTip = "仅勾选本次运行范围",
                    Width = 25, VerticalAlignment = VerticalAlignment.Center };
                check.Checked += (_, _) => { item.RunSelected = true; _store.Save(); };
                check.Unchecked += (_, _) => { item.RunSelected = false; _store.Save(); };
                row.Children.Add(check);
                var b = UiVisual.Button(item.Name, () => { _caseId = item.Id; _stepId = null; RefreshTree(); });
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Background = _caseId == item.Id ? UiVisual.SoftAccent : UiVisual.Surface;
                b.Margin = new Thickness(0, 0, 0, 3);
                b.MinWidth = 0;
                row.Children.Add(b);
                _objects.Children.Add(row);
            }
            if (_store.Current.TestCases.Count == 0) _objects.Children.Add(UiVisual.Note("没有用例。点击“新建”开始。"));
        }
    }
    private void RefreshTree()
    {
        _refreshing = true;
        try
        {
            _steps.Items.Clear();
            var @case = CurrentCase;
            var flow = CurrentFlow;
            _editorTitle.Text = IsFlowMode ? flow?.Name ?? "选择公共流程" : @case?.Name ?? "选择测试用例";
            if (IsFlowMode)
            {
                if (flow is not null) BuildBranch("公共流程 · 顺序步骤", "flow", flow.Steps, _steps);
                _count.Text = flow is null ? "" : UiTestCase.Walk(flow.Steps).Count() + " 步";
            }
            else if (@case is not null)
            {
                BuildBranch("01  准备", "prepare", @case.Prepare, _steps);
                BuildBranch("02  测试", "test", @case.Test, _steps);
                BuildBranch("03  清理", "cleanup", @case.Cleanup, _steps);
                _count.Text = @case.AllSteps().Count() + " 步";
            }
            else _count.Text = "";
            if (_stepId is { } target) FindAndSelect(_steps.Items.OfType<TreeViewItem>(), target);
            if (_selectedLocation?.Step?.Id != _stepId) _selectedLocation = null;
        }
        finally { _refreshing = false; }
        ShowInspector();
    }
    private void BuildBranch(string title, string phase, List<UiStep> nodes, ItemsControl parent)
    {
        var root = new TreeViewItem
        {
            Header = title,
            Tag = new StepLocation(phase, null, nodes, -1),
            IsExpanded = true,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisual.Ink
        };
        parent.Items.Add(root);
        for (var i = 0; i < nodes.Count; i++) BuildNode(phase, nodes, i, root);
    }
    private void BuildNode(string phase, List<UiStep> list, int index, ItemsControl parent)
    {
        var step = list[index];
        var item = new TreeViewItem
        {
            Header = (step.Enabled ? "" : "○ ") + UiStepTypes.Label(step.Type) + "  ·  " + step.Name,
            Tag = new StepLocation(phase, step, list, index),
            IsExpanded = true
        };
        item.ContextMenu = new ContextMenu();
        void Menu(string label, Action action)
        {
            var m = new MenuItem { Header = label };
            m.Click += (_, _) => { _stepId = step.Id; _selectedLocation = item.Tag as StepLocation; action(); };
            item.ContextMenu.Items.Add(m);
        }
        Menu("编辑参数", ShowInspector);
        Menu("插入同级步骤", () => Insert(false));
        Menu("添加子步骤", () => Insert(true));
        Menu("复制步骤", DuplicateStep);
        Menu("删除步骤", DeleteStep);
        parent.Items.Add(item);
        if (step.Type is "If" or "When")
        {
            BuildBranch("条件成立 / THEN", phase, step.Then, item);
            BuildBranch("否则 / ELSE", phase, step.Else, item);
        }
        else if (step.Type is "Loop" or "Sequence" or "Switch")
            BuildBranch(step.Type == "Switch" ? "CASE / 分支" : "BODY / 子步骤", phase, step.Body, item);
    }
    private bool FindAndSelect(IEnumerable<TreeViewItem> items, Guid id)
    {
        foreach (var item in items)
        {
            if (item.Tag is StepLocation { Step: { } step } && step.Id == id)
            {
                _selectedLocation = item.Tag as StepLocation;
                item.IsSelected = true;
                item.BringIntoView();
                return true;
            }
            if (FindAndSelect(item.Items.OfType<TreeViewItem>(), id)) return true;
        }
        return false;
    }
    private StepLocation? ResolveCurrentLocation()
    {
        var id = _stepId;
        if (id is null) return _selectedLocation;
        StepLocation? Search(string phase, List<UiStep> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var step = nodes[i];
                if (step.Id == id) return new StepLocation(phase, step, nodes, i);
                foreach (var list in new[] { step.Then, step.Else, step.Body })
                {
                    var found = Search(phase, list);
                    if (found is not null) return found;
                }
            }
            return null;
        }
        var c = CurrentCase;
        if (c is not null) return Search("prepare", c.Prepare) ??
            Search("test", c.Test) ?? Search("cleanup", c.Cleanup);
        return CurrentFlow is { } f ? Search("flow", f.Steps) : null;
    }
    private void ShowInspector()
    {
        _inspector.Children.Clear();
        _parameterInputs.Clear();
        var step = ResolveCurrentLocation()?.Step;
        if (step is null)
        {
            _inspector.Children.Add(UiVisual.Text("步骤详情", 17, true));
            var hint = UiVisual.Note(CurrentCase is null && CurrentFlow is null
                ? "在左侧创建或选择用例 / 公共流程，再添加步骤。"
                : "选择一个步骤，名称和专业参数将在这里持续可编辑。");
            hint.Margin = new Thickness(0, 15, 0, 0);
            _inspector.Children.Add(hint);
            _nameField = null;
            return;
        }
        _inspector.Children.Add(UiVisual.Text(UiStepTypes.Label(step.Type), 17, true));
        _inspector.Children.Add(UiVisual.Note("仅 UI 草稿 · 不生成真实 TEST 执行事实"));
        _inspector.Children.Add(UiVisual.Rule());
        _nameField = UiVisual.Input(step.Name);
        _inspector.Children.Add(UiVisual.Field("步骤名称", _nameField));
        _enabledField = new CheckBox { Content = "默认启用此步骤", IsChecked = step.Enabled, Margin = new Thickness(0, 0, 0, 14) };
        _inspector.Children.Add(_enabledField);
        foreach (var key in UiStepTypes.Fields(step.Type).Concat(step.Properties.Keys).Distinct())
        {
            var field = UiVisual.Input(step.Properties.GetValueOrDefault(key, ""));
            if (key is "dbcMessage")
            {
                var names = string.Join("、", _store.Current.DbcMessages.Select(x => x.Name));
                field.ToolTip = names.Length == 0 ? "当前 UI 草稿中没有 DBC 报文，先到 DBC 工具创建" : "可引用的报文：" + names;
            }
            if (key == "flow") field.ToolTip = "公共流程：" +
                string.Join("、", _store.Current.PublicFlows.Select(x => x.Name));
            _parameterInputs.Add((key, field));
            _inspector.Children.Add(UiVisual.Field(UiStepTypes.FieldName(key), field));
        }
        _inspector.Children.Add(UiVisual.Button("应用步骤修改", () => CommitInspector(true), true));
        _inspector.Children.Add(UiVisual.Note("Ctrl+Z / Ctrl+Y 撤销或重做步骤事务。"));
    }
    private void CommitInspector(bool rebuild)
    {
        var step = ResolveCurrentLocation()?.Step;
        if (step is null || _nameField is null) return;
        var name = _nameField.Text.Trim();
        if (name.Length == 0) { _localStatus.Text = "步骤名称不能为空。"; return; }
        var map = _parameterInputs.ToDictionary(x => x.Key, x => x.Field.Text);
        bool enabled = _enabledField?.IsChecked == true;
        if (name == step.Name && enabled == step.Enabled &&
            map.Count == step.Properties.Count &&
            map.All(p => step.Properties.TryGetValue(p.Key, out var v) && v == p.Value)) return;
        var id = step.Id;
        _history.Record(_store.Current);
        step.Name = name; step.Enabled = enabled; step.Properties = map;
        _store.Save();
        _status("已应用 UI 步骤参数");
        _localStatus.Text = "步骤参数已保存至 UI 草稿，不代表正式 TEST 资源";
        if (rebuild) { _stepId = id; RefreshTree(); }
    }
    private void Insert(bool child)
    {
        CommitInspector(false);
        if (_palette.SelectedItem is not StepChoice type) return;
        var loc = ResolveCurrentLocation() ?? _selectedLocation;
        if (CurrentCase is null && CurrentFlow is null) { _localStatus.Text = "请先选择测试用例或公共流程。"; return; }
        var list = loc?.Container ??
            (IsFlowMode ? CurrentFlow!.Steps : CurrentCase!.Test);
        int at = list.Count;
        if (loc?.Step is { } parent)
        {
            if (child)
            {
                list = parent.Type is "If" or "When" ? parent.Then :
                    parent.Type is "Loop" or "Sequence" or "Switch" ? parent.Body : list;
                if (ReferenceEquals(list, loc.Container))
                {
                    _localStatus.Text = "当前步骤没有子分支；请使用同级插入。";
                    return;
                }
                at = list.Count;
            }
            else at = loc.Index + 1;
        }
        var node = UiStepTypes.New(type.Type);
        Mutate(() => list.Insert(Math.Clamp(at, 0, list.Count), node), "已添加 " + node.Name + " · 纯 UI 编辑");
        _stepId = node.Id;
        RefreshTree();
    }
    private void DuplicateStep()
    {
        CommitInspector(false);
        var loc = ResolveCurrentLocation();
        if (loc?.Step is null) return;
        var copy = loc.Step.CloneFresh();
        Mutate(() => loc.Container.Insert(loc.Index + 1, copy), "已复制步骤");
        _stepId = copy.Id; RefreshTree();
    }
    private void DeleteStep()
    {
        var loc = ResolveCurrentLocation();
        if (loc?.Step is null) return;
        Mutate(() => loc.Container.RemoveAt(loc.Index), "已删除 UI 步骤，可撤销");
        _stepId = null; RefreshTree();
    }
    private void MoveStep(int direction)
    {
        CommitInspector(false);
        var loc = ResolveCurrentLocation();
        if (loc?.Step is null) return;
        int index = loc.Index + direction;
        if (index < 0 || index >= loc.Container.Count) return;
        var id = loc.Step.Id;
        Mutate(() => (loc.Container[loc.Index], loc.Container[index]) =
            (loc.Container[index], loc.Container[loc.Index]), "已调整步骤顺序");
        _stepId = id; RefreshTree();
    }
    private void NewObject()
    {
        string? name = UiDialogs.Text(Window.GetWindow(this)!, IsFlowMode ? "新建公共流程" : "新建测试用例",
            "名称", IsFlowMode ? "新建公共流程" : "新建测试用例");
        if (name is null) return;
        if (IsFlowMode)
        {
            var flow = new UiPublicFlow { Name = name };
            Mutate(() => _store.Current.PublicFlows.Add(flow), "已创建 UI 公共流程");
            _flowId = flow.Id; _stepId = null;
        }
        else
        {
            var test = new UiTestCase { Name = name };
            Mutate(() => _store.Current.TestCases.Add(test), "已创建 UI 测试用例");
            _caseId = test.Id; _stepId = null;
        }
        RefreshObjects(); RefreshTree();
    }
    private void DuplicateObject()
    {
        if (IsFlowMode && CurrentFlow is { } flow)
        {
            var cloned = System.Text.Json.JsonSerializer.Deserialize<UiPublicFlow>(
                System.Text.Json.JsonSerializer.Serialize(flow))!;
            cloned.Id = Guid.NewGuid(); cloned.Name += " - 副本";
            foreach (var step in UiTestCase.Walk(cloned.Steps)) step.Id = Guid.NewGuid();
            Mutate(() => _store.Current.PublicFlows.Add(cloned), "已复制公共流程");
            _flowId = cloned.Id;
        }
        else if (CurrentCase is { } c)
        {
            var cloned = System.Text.Json.JsonSerializer.Deserialize<UiTestCase>(
                System.Text.Json.JsonSerializer.Serialize(c))!;
            cloned.Id = Guid.NewGuid(); cloned.Name += " - 副本";
            foreach (var step in cloned.AllSteps()) step.Id = Guid.NewGuid();
            Mutate(() => _store.Current.TestCases.Add(cloned), "已复制测试用例");
            _caseId = cloned.Id;
        }
        _stepId = null; RefreshObjects(); RefreshTree();
    }
    public void Undo()
    {
        if (!_history.CanUndo) return;
        _store.Replace(_history.Undo(_store.Current));
        _stepId = null;
        RefreshObjects(); RefreshTree(); _status("已撤销 UI 编辑");
    }
    public void Redo()
    {
        if (!_history.CanRedo) return;
        _store.Replace(_history.Redo(_store.Current));
        _stepId = null;
        RefreshObjects(); RefreshTree(); _status("已重做 UI 编辑");
    }
    private UIElement BuildRunView()
    {
        var stack = UiVisual.Stack();
        stack.Margin = new Thickness(13);
        stack.Children.Add(UiVisual.Section("本次用例选择", BuildSelectionList(),
            "勾选与步骤默认启用相互独立。当前没有实体执行接线，不能形成真实 PASS、FAIL 或 RunRecord。"));
        var info = UiVisual.Stack();
        info.Children.Add(UiVisual.Text("运行准备与状态", 15, true));
        info.Children.Add(UiVisual.Note("真实工位：未连接（UI 阶段）"));
        info.Children.Add(UiVisual.Note("资源预检、执行快照、单步、断点、运行日志：等待正式 TEST/DEVICE 接线。"));
        var actions = UiVisual.Toolbar();
        var run = UiVisual.Button("开始真实测试", () => { }, true,
            "纯 UI 阶段没有 TEST 执行器及实体运行授权");
        run.IsEnabled = false;
        actions.Children.Add(run);
        var stop = UiVisual.Button("停止实体运行", () => { }, false, "没有运行中的真实 Run");
        stop.IsEnabled = false;
        actions.Children.Add(stop);
        info.Children.Add(actions);
        stack.Children.Add(UiVisual.Box(info, new Thickness(18)));
        return UiVisual.Scroll(stack);
    }
    private UIElement BuildSelectionList()
    {
        var list = UiVisual.Stack();
        foreach (var c in _store.Current.TestCases)
        {
            var check = new CheckBox { Content = c.Name, IsChecked = c.RunSelected,
                Margin = new Thickness(0, 3, 0, 6), MinHeight = 26 };
            check.Checked += (_, _) => { c.RunSelected = true; _store.Save(); };
            check.Unchecked += (_, _) => { c.RunSelected = false; _store.Save(); };
            list.Children.Add(check);
        }
        if (_store.Current.TestCases.Count == 0) list.Children.Add(UiVisual.Note("暂无测试用例。"));
        return list;
    }
}
