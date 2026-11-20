using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.UI.Design;
using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>Reusable, nested UI-only common flow editor. Not a duplicate TEST runner.</summary>
public sealed class CommonFlowWorkspace : Grid
{
    private readonly UiSession _session;
    private readonly Action _changed;
    private readonly Action<string> _status;
    private readonly ListBox _flows = new();
    private readonly TreeView _steps = new();
    private readonly StackPanel _details = new();
    private readonly ComboBox _actionType = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private UiStep? _flow;
    private UiStep? _selectedStep;
    private bool _sync;

    public CommonFlowWorkspace(UiSession session, Action changed, Action<string> status)
    {
        _session = session; _changed = changed; _status = status;
        Margin = new Thickness(14);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(295) });

        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        Children.Add(UiKit.Card(left));
        var first = new StackPanel();
        DockPanel.SetDock(first, Dock.Top);
        left.Children.Add(first);
        first.Children.Add(UiKit.Text("公共流程", 16, true));
        first.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 新建", () =>
            {
                Before();
                var flow = new UiStep { Type = "公共流程", Name = "新建公共流程" };
                _session.CommonFlows.Add(flow); _flows.SelectedItem = flow; _changed();
            }, true),
            UiKit.Button("复制", DuplicateFlow),
            UiKit.Button("删除", DeleteFlow)));
        _flows.DisplayMemberPath = nameof(UiStep.Name);
        _flows.ItemsSource = _session.CommonFlows;
        _flows.SelectionChanged += (_, _) =>
        {
            if (_sync) return;
            _flow = _flows.SelectedItem as UiStep; _selectedStep = null;
            RefreshTree();
        };
        left.Children.Add(_flows);

        var middle = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(middle, 1);
        Children.Add(UiKit.Card(middle));
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        top.Children.Add(UiKit.Text("流程步骤", 16, true));
        top.Children.Add(UiKit.Note("公共流程是一条连续步骤树；点击类型并插入。可嵌套流程控制，也能被测试用例引用。"));
        _actionType.ItemsSource = new[] { "等待", "条件 IF / ELSE", "SWITCH", "循环",
            "TRY / CATCH", "CAN 单次发送", "CAN 信号判定",
            "UDS 请求", "模拟外部输入", "波形判定", "调用公共流程", "日志记录" };
        _actionType.SelectedIndex = 0;
        _actionType.MinHeight = 33;
        _actionType.Margin = new Thickness(0, 0, 0, 9);
        top.Children.Add(_actionType);
        top.Children.Add(UiKit.Actions(
            UiKit.Button("＋ 同级", () => Add(false), true),
            UiKit.Button("＋ 子步骤", () => Add(true)),
            UiKit.Button("复制", DuplicateStep),
            UiKit.Button("删除", DeleteStep)));
        top.Children.Add(UiKit.Actions(
            UiKit.Button("上移", () => Move(-1)),
            UiKit.Button("下移", () => Move(1)),
            UiKit.Button("撤销", Undo),
            UiKit.Button("重做", Redo)));
        _steps.BorderThickness = new Thickness(0);
        _steps.SelectedItemChanged += (_, _) =>
        {
            if (_sync) return;
            _selectedStep = (_steps.SelectedItem as TreeViewItem)?.Tag as UiStep;
            ShowDetails();
        };
        middle.Children.Add(_steps);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        Children.Add(UiKit.Card(UiKit.Scroll(right)));
        right.Children.Add(UiKit.Text("连续参数编辑", 17, true));
        right.Children.Add(UiKit.Note("编辑当前选中对象，无需额外打开属性窗口。"));
        right.Children.Add(_details);
        if (_session.CommonFlows.Count > 0) _flows.SelectedIndex = 0;
        ShowDetails();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
    }
    private void Before()
    {
        _undo.Push(JsonSerializer.Serialize(_session.CommonFlows.ToArray()));
        _redo.Clear();
    }
    private void Restore(string json)
    {
        _sync = true;
        try
        {
            _session.CommonFlows.Clear();
            foreach (var flow in JsonSerializer.Deserialize<List<UiStep>>(json) ?? [])
                _session.CommonFlows.Add(flow);
            _flow = _session.CommonFlows.FirstOrDefault();
            _flows.SelectedItem = _flow;
            _selectedStep = null;
        }
        finally { _sync = false; }
        RefreshTree(); _changed();
    }
    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(JsonSerializer.Serialize(_session.CommonFlows.ToArray()));
        Restore(_undo.Pop());
    }
    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(JsonSerializer.Serialize(_session.CommonFlows.ToArray()));
        Restore(_redo.Pop());
    }
    private void RefreshTree(string? id = null)
    {
        _sync = true;
        try
        {
            _steps.Items.Clear();
            if (_flow is null) return;
            Render(_steps, _flow.Children, id);
        }
        finally { _sync = false; }
        ShowDetails();
    }
    private void Render(ItemsControl parent, IEnumerable<UiStep> list, string? id)
    {
        foreach (var step in list)
        {
            var node = new TreeViewItem
            {
                Header = (step.Branch.Length == 0 ? "" : "〔" + step.Branch + "〕 ") +
                    step.Type + " · " + step.Name, Tag = step, IsExpanded = true
            };
            parent.Items.Add(node);
            if (step.Id == id) { _selectedStep = step; node.IsSelected = true; }
            Render(node, step.Children, id);
        }
    }
    private ObservableCollection<UiStep>? Find(UiStep target, ObservableCollection<UiStep> list)
    {
        if (list.Contains(target)) return list;
        foreach (var step in list)
        {
            var found = Find(target, step.Children);
            if (found is not null) return found;
        }
        return null;
    }
    private void Add(bool child)
    {
        if (_flow is null) { _status("请先创建公共流程。"); return; }
        Before();
        var kind = _actionType.SelectedItem as string ?? "等待";
        var list = _flow.Children;
        var index = list.Count;
        string branch = "";
        if (_selectedStep is { } selected)
        {
            if (child)
            {
                list = selected.Children; index = list.Count;
                branch = StepInspectorCatalog.For(selected.Type).Branches?.FirstOrDefault() ?? "BODY";
            }
            else
            {
                list = Find(selected, _flow.Children) ?? _flow.Children;
                index = list.IndexOf(selected) + 1;
                branch = selected.Branch;
            }
        }
        var added = StepInspectorCatalog.New(kind, "公共流程", branch);
        list.Insert(index, added);
        _selectedStep = added;
        RefreshTree(added.Id);
        _changed();
        _status("已插入公共流程子步骤；无真实执行器接线。");
    }
    private void DeleteStep()
    {
        if (_flow is null || _selectedStep is null) return;
        var list = Find(_selectedStep, _flow.Children);
        if (list is null) return;
        Before(); list.Remove(_selectedStep);
        _selectedStep = null; RefreshTree(); _changed();
    }
    private void DuplicateStep()
    {
        if (_flow is null || _selectedStep is null) return;
        var list = Find(_selectedStep, _flow.Children);
        if (list is null) return;
        Before();
        var clone = JsonSerializer.Deserialize<UiStep>(JsonSerializer.Serialize(_selectedStep))!;
        Renew(clone); clone.Name += " 副本";
        list.Insert(list.IndexOf(_selectedStep) + 1, clone);
        _selectedStep = clone; RefreshTree(clone.Id); _changed();
    }
    private static void Renew(UiStep item)
    {
        item.Id = Guid.NewGuid().ToString("N");
        foreach (var child in item.Children) Renew(child);
    }
    private void Move(int direction)
    {
        if (_flow is null || _selectedStep is null) return;
        var list = Find(_selectedStep, _flow.Children);
        if (list is null) return;
        var old = list.IndexOf(_selectedStep);
        var target = old + direction;
        if (target < 0 || target >= list.Count) return;
        Before(); list.Move(old, target); RefreshTree(_selectedStep.Id); _changed();
    }
    private void DuplicateFlow()
    {
        if (_flow is null) return;
        Before();
        var clone = JsonSerializer.Deserialize<UiStep>(JsonSerializer.Serialize(_flow))!;
        Renew(clone); clone.Name += " 副本";
        _session.CommonFlows.Insert(_session.CommonFlows.IndexOf(_flow) + 1, clone);
        _flows.SelectedItem = clone; _changed();
    }
    private void DeleteFlow()
    {
        if (_flow is null) return;
        Before();
        _session.CommonFlows.Remove(_flow);
        _flows.SelectedIndex = _session.CommonFlows.Count > 0 ? 0 : -1;
        _changed();
    }
    private void ShowDetails()
    {
        _details.Children.Clear();
        var objectToEdit = _selectedStep ?? _flow;
        if (objectToEdit is null)
        {
            _details.Children.Add(UiKit.Text("先新建公共流程，再添加步骤。", 13, false, UiKit.Muted));
            return;
        }
        var name = UiKit.Input(objectToEdit.Name);
        var target = UiKit.Input(objectToEdit.Target);
        var value = UiKit.Input(objectToEdit.Value);
        var condition = UiKit.Input(objectToEdit.Condition);
        var note = UiKit.Input(objectToEdit.Note, true);
        var spec = StepInspectorCatalog.For(objectToEdit.Type);
        _details.Children.Add(UiKit.Text(spec.Help, 12, false, UiKit.Muted));
        _details.Children.Add(UiKit.Field("对象名称", name));
        if (_selectedStep is not null)
        {
            if (spec.TargetLabel != "—") _details.Children.Add(UiKit.Field(spec.TargetLabel, target));
            if (spec.ValueLabel != "—") _details.Children.Add(UiKit.Field(spec.ValueLabel, value));
            if (spec.ConditionLabel != "—") _details.Children.Add(UiKit.Field(spec.ConditionLabel, condition));
        }
        _details.Children.Add(UiKit.Field("说明", note));
        _details.Children.Add(UiKit.Button("应用修改", () =>
        {
            Before();
            objectToEdit.Name = name.Text; objectToEdit.Target = target.Text;
            objectToEdit.Value = value.Text; objectToEdit.Condition = condition.Text; objectToEdit.Note = note.Text;
            _flows.Items.Refresh(); RefreshTree(_selectedStep?.Id); _changed();
        }, true));
        if (_selectedStep is { } current && spec.Branches is { Length: > 0 } branches)
        {
            var picker = new ComboBox { ItemsSource = branches, SelectedIndex = 0,
                MinHeight = 32, Margin = new Thickness(0, 10, 0, 8) };
            _details.Children.Add(UiKit.Field("子分支", picker));
            _details.Children.Add(UiKit.Button("向此分支添加", () =>
            {
                if (picker.SelectedItem is not string branch) return;
                Before();
                var newStep = StepInspectorCatalog.New(_actionType.SelectedItem as string ?? "等待", "公共流程", branch);
                current.Children.Add(newStep); _selectedStep = newStep;
                RefreshTree(newStep.Id); _changed();
            }));
        }
    }
}
