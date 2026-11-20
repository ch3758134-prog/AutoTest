using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoTest.TestEditorPreview;

/// <summary>
/// Single-window editing experience. There is deliberately no DEVICE, TEST
/// execution or PROJECT service reference; all state is user-visible draft data.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public EditorDocument Document { get; private set; } = new();
    public ObservableCollection<StepRow> Rows { get; } = [];
    public ObservableCollection<EditorProblem> Problems { get; } = [];
    private EditorCase? _selectedCase;
    public EditorCase? SelectedCase
    {
        get => _selectedCase;
        private set { _selectedCase = value; Notify(); }
    }
    private EditorNode? _selectedNode;
    public EditorNode? SelectedNode
    {
        get => _selectedNode;
        private set { _selectedNode = value; Notify(); }
    }
    private string _status = "选中步骤即可在详情区直接编辑；实体执行功能未接线。";
    public string Status { get => _status; private set { _status = value; Notify(); } }
    private bool _modified;
    public string SaveStateText => _modified ? "● 未保存 UI 草稿" :
        _currentFile is null ? "研究样本 · 尚未保存到文件" : "研究草稿已保存";
    public int ProblemCount => Problems.Count;
    private bool _uiReady;
    private bool _syncing;
    private bool _compactMode;
    private bool _compactDetails;
    private bool _wasCompact;
    private double _lastCaseWidth = 255;
    private double _lastInspectorWidth = 380;
    private string? _fieldSnapshot;
    private EditorNode? _editingNodeAtFocus;
    private string? _currentFile;
    private string? _copiedStep;
    private string? _editCaseBeforeRunToggle;
    private string? _editNodeBeforeRunToggle;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        PaletteTypes.ItemsSource = StepCatalog.Types;
        _uiReady = true;
        var fixture = SampleData.Create();
        PreviewSelfCheck.Verify(fixture); // explicit model integrity check, NOT a Windows UX test.
        ReplaceDocument(fixture, clearHistory: true, markSaved: true);
        UpdateResponsive();
    }

    private void SetStatus(string message) => Status = message;
    private void Dirty(string? message = null)
    {
        _modified = true;
        Notify(nameof(SaveStateText));
        if (message is not null) SetStatus(message);
    }
    private void Checkpoint()
    {
        _undo.Push(Document.Serialize());
        if (_undo.Count > 100)
        {
            var recent = _undo.ToArray().Take(100).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in recent) _undo.Push(item);
        }
        _redo.Clear();
    }
    private void Restore(string json)
    {
        var previousFile = _currentFile;
        ReplaceDocument(EditorDocument.Load(json), clearHistory: false, markSaved: false);
        _currentFile = previousFile;
        Dirty("已恢复先前的编辑状态（不包含任何真实 ECU 操作）。");
    }
    private void ReplaceDocument(EditorDocument doc, bool clearHistory, bool markSaved)
    {
        _syncing = true;
        try
        {
            Document = doc;
            Document.RepairParents();
            Notify(nameof(Document));
            if (clearHistory) { _undo.Clear(); _redo.Clear(); _currentFile = null; }
            CaseList.ItemsSource = Document.Cases;
            CompactCasePicker.ItemsSource = Document.Cases;
            var chosen = Document.Cases.FirstOrDefault(c => c.Id == Document.ActiveCaseId) ??
                         Document.Cases.FirstOrDefault();
            SelectedCase = chosen;
            CaseList.SelectedItem = chosen;
            CompactCasePicker.SelectedItem = chosen;
            SelectedNode = chosen?.Find(Document.ActiveNodeId) ??
                           chosen?.Phases.ElementAtOrDefault(1);
            UpdateRows(scroll: false);
            _modified = !markSaved;
            Notify(nameof(SaveStateText));
            CaseSearch.Text = "";
            StepSearch.Text = "";
            _fieldSnapshot = null;
        }
        finally { _syncing = false; }
        if (SelectedNode is not null)
            SelectNode(SelectedNode, scroll: true);
        RefreshProblems();
        SetStatus(markSaved
            ? "已打开研究样本：12 个用例，当前用例 40 步；不是正式 TEST 工程。"
            : "编辑状态已恢复。");
    }

    private IEnumerable<EditorNode> AllNodes(EditorNode root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var node in AllNodes(child))
                yield return node;
    }
    private void RefreshProblems()
    {
        Problems.Clear();
        if (SelectedCase is not null)
            foreach (var phase in SelectedCase.Phases)
                foreach (var node in AllNodes(phase))
                    foreach (var field in node.Fields.Where(f => f.IsInvalid))
                        Problems.Add(new EditorProblem
                        {
                            NodeId = node.Id, FieldKey = field.Key,
                            NodeName = node.DisplayTitle, Message = field.Label + "：" + field.Error
                        });
        Notify(nameof(ProblemCount));
        SelectedNode?.RefreshValidation();
    }
    private void UpdateRows(bool scroll = false)
    {
        var current = SelectedNode?.Id;
        _syncing = true;
        try
        {
            Rows.Clear();
            var query = StepSearch.Text.Trim();
            if (SelectedCase is not null)
            {
                foreach (var phase in SelectedCase.Phases)
                    AddVisible(phase, 0, query);
                var selected = Rows.FirstOrDefault(r => r.Node.Id == current);
                StepList.SelectedItem = selected;
                if (scroll && selected is not null)
                    StepList.Dispatcher.BeginInvoke(new Action(() => StepList.ScrollIntoView(selected)));
            }
        }
        finally { _syncing = false; }
        SelectedCase?.UpdateCount();
        RefreshProblems();
    }
    private bool HasQuery(EditorNode node, string query)
    {
        if (query.Length == 0) return true;
        if (node.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            node.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            node.Kind.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            node.Fields.Any(f => f.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return true;
        return node.Children.Any(child => HasQuery(child, query));
    }
    private void AddVisible(EditorNode node, int depth, string query)
    {
        if (!HasQuery(node, query)) return;
        node.HasSelectedChild = SelectedNode is not null && IsAncestor(node, SelectedNode);
        Rows.Add(new StepRow { Node = node, Depth = depth });
        if (node.IsExpanded || query.Length > 0)
            foreach (var child in node.Children)
                AddVisible(child, depth + 1, query);
    }
    private static bool IsAncestor(EditorNode ancestor, EditorNode descendant)
    {
        for (var current = descendant.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private void SelectCase(EditorCase? value)
    {
        if (value is null || ReferenceEquals(value, SelectedCase)) return;
        SelectedCase = value;
        Document.ActiveCaseId = value.Id;
        var inCase = value.Find(Document.ActiveNodeId);
        SelectedNode = inCase ?? value.Phases.ElementAtOrDefault(1);
        Document.ActiveNodeId = SelectedNode?.Id ?? "";
        _syncing = true;
        try { CaseList.SelectedItem = value; CompactCasePicker.SelectedItem = value; }
        finally { _syncing = false; }
        StepSearch.Text = "";
        UpdateRows(scroll: true);
        SetStatus("当前编辑用例已切换；不会更改其本次运行勾选。");
    }
    private void SelectNode(EditorNode? node, bool scroll)
    {
        if (node is null) return;
        SelectedNode = node;
        Document.ActiveNodeId = node.Id;
        if (scroll)
        {
            for (var p = node.Parent; p is not null; p = p.Parent)
                p.IsExpanded = true;
        }
        UpdateRows(scroll);
        SetStatus("当前编辑：" + node.DisplayTitle + "；参数区与当前步骤同步。");
    }
    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !_uiReady) return;
        var row = StepList.SelectedItem as StepRow;
        if (row is null) return;
        if (!ReferenceEquals(SelectedNode, row.Node))
        {
            SelectedNode = row.Node;
            Document.ActiveNodeId = row.Node.Id;
            SetStatus("选中 " + row.Node.DisplayTitle + "；单击行不会启动任何设备操作。");
        }
    }
    private void CaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !_uiReady) return;
        SelectCase(CaseList.SelectedItem as EditorCase);
    }
    private void CompactCasePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !_uiReady) return;
        SelectCase(CompactCasePicker.SelectedItem as EditorCase);
    }
    private void RunSelect_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _editCaseBeforeRunToggle = SelectedCase?.Id;
        _editNodeBeforeRunToggle = SelectedNode?.Id;
        Checkpoint();
    }
    private void RunSelect_PreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _editCaseBeforeRunToggle = SelectedCase?.Id;
            _editNodeBeforeRunToggle = SelectedNode?.Id;
            Checkpoint();
        }
    }
    private void RunSelect_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _syncing) return;
        // Clicking the checkbox must never change the current *editor* case.
        var keep = Document.Cases.FirstOrDefault(c => c.Id == _editCaseBeforeRunToggle);
        _editCaseBeforeRunToggle = null;
        var savedEditorNodeId = _editNodeBeforeRunToggle;
        _editNodeBeforeRunToggle = null;
        if (keep is not null && !ReferenceEquals(keep, SelectedCase))
        {
            _syncing = true;
            SelectedCase = keep;
            Document.ActiveCaseId = keep.Id;
            try { CaseList.SelectedItem = keep; CompactCasePicker.SelectedItem = keep; }
            finally { _syncing = false; }
            SelectedNode = keep.Find(savedEditorNodeId ?? "") ?? keep.Phases.ElementAtOrDefault(1);
            Document.ActiveNodeId = SelectedNode?.Id ?? "";
            UpdateRows();
        }
        Dirty("本次运行勾选已变更；编辑中的用例和步骤保持不变。");
    }
    private void CaseSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || CaseList.ItemsSource is null) return;
        var query = CaseSearch.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(Document.Cases);
        view.Filter = obj => obj is EditorCase c &&
            (query.Length == 0 || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }
    private void StepSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_uiReady && !_syncing) UpdateRows();
    }
    private void ExpandNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditorNode node } || node.Children.Count == 0) return;
        node.IsExpanded = !node.IsExpanded;
        UpdateRows();
        e.Handled = true;
        SetStatus(node.IsExpanded ? "已展开：" + node.Title : "已折叠：" + node.Title +
            "；内部选中对象和问题仍被保留。");
    }

    private void NewCase_Click(object sender, RoutedEventArgs e)
    {
        Checkpoint();
        var created = new EditorCase { Name = "新建测试用例" };
        foreach (var phase in new[] { "准备", "测试", "清理" })
            created.Phases.Add(StepCatalog.Phase(phase));
        Document.Cases.Add(created);
        SelectCase(created);
        BeginCaseRename();
        Dirty("已新建空白测试用例；可直接重命名并向阶段添加步骤。");
    }
    private void DuplicateCase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null) return;
        Checkpoint();
        var clone = new EditorCase { Name = SelectedCase.Name + " 副本" };
        foreach (var phase in SelectedCase.Phases)
            clone.Phases.Add(phase.CloneWithFreshIds());
        clone.RepairParents();
        Document.Cases.Insert(Document.Cases.IndexOf(SelectedCase) + 1, clone);
        SelectCase(clone);
        Dirty("已复制用例，步骤对象获得新的 UI 身份。");
    }
    private void DeleteCase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null) return;
        if (Document.Cases.Count <= 1)
        {
            SetStatus("至少保留一个测试用例。");
            return;
        }
        Checkpoint();
        var index = Document.Cases.IndexOf(SelectedCase);
        Document.Cases.Remove(SelectedCase);
        SelectCase(Document.Cases[Math.Clamp(index, 0, Document.Cases.Count - 1)]);
        Dirty("已删除用例；Ctrl+Z 可以恢复。");
    }
    private void RenameCase_Click(object sender, RoutedEventArgs e) => BeginCaseRename();
    private void BeginCaseRename()
    {
        if (SelectedCase is null) return;
        CaseRenameBox.Visibility = Visibility.Visible;
        CaseRenameBox.Text = SelectedCase.Name;
        CaseRenameBox.SelectAll();
        CaseRenameBox.Focus();
    }
    private void CommitCaseRename()
    {
        if (CaseRenameBox.Visibility != Visibility.Visible || SelectedCase is null) return;
        var name = CaseRenameBox.Text.Trim();
        if (name.Length == 0) { SetStatus("用例名称不能留空。"); return; }
        if (!string.Equals(SelectedCase.Name, name, StringComparison.Ordinal))
        {
            Checkpoint(); SelectedCase.Name = name;
            Dirty("已重命名当前用例；变更可撤销。");
        }
        CaseRenameBox.Visibility = Visibility.Collapsed;
    }
    private void CaseRenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitCaseRename(); e.Handled = true; }
        if (e.Key == Key.Escape) { CaseRenameBox.Visibility = Visibility.Collapsed; e.Handled = true; }
    }
    private void CaseRenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (CaseRenameBox.Visibility == Visibility.Visible) CommitCaseRename();
    }

    private void OpenPalette_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null) return;
        PaletteSearch.Text = "";
        PaletteTypes.ItemsSource = StepCatalog.Types;
        PaletteTypes.SelectedIndex = 0;
        var n = SelectedNode;
        PaletteTargetLabel.Text = n is null ? "添加到当前用例的测试阶段" :
            n.Role == NodeRole.Container ? "请先选择明确的 THEN、ELSE、CASE 等分支，再插入。" :
            n.Role is NodeRole.Branch or NodeRole.Phase ? "目标：" + n.DisplayTitle + " 的步骤列表" :
            "目标：" + n.DisplayTitle + " 同级的指定位置";
        StepPalette.IsOpen = true;
        PaletteSearch.Dispatcher.BeginInvoke(new Action(() => PaletteSearch.Focus()));
    }
    private void ContextAdd_Click(object sender, RoutedEventArgs e) => OpenPalette_Click(sender, e);
    private void PaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || PaletteTypes.ItemsSource is null) return;
        var query = PaletteSearch.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(PaletteTypes.ItemsSource);
        view.Filter = obj => obj is string t && (query.Length == 0 || t.Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
        PaletteTypes.SelectedIndex = view.IsEmpty ? -1 : 0;
    }
    private void PaletteTypes_MouseDoubleClick(object sender, MouseButtonEventArgs e) => InsertStep(after: true);
    private void InsertBefore_Click(object sender, RoutedEventArgs e) => InsertStep(after: false);
    private void InsertAfter_Click(object sender, RoutedEventArgs e) => InsertStep(after: true);
    private void InsertStep(bool after)
    {
        if (SelectedCase is null || PaletteTypes.SelectedItem is not string kind) return;
        var target = SelectedNode ?? SelectedCase.Phases[1];
        if (target.Role == NodeRole.Container)
        {
            SetStatus("请选择明确的子分支（THEN/ELSE/CASE 等），不能隐式将步骤插入流程容器。");
            return;
        }
        EditorNode? parent = target.Role is NodeRole.Phase or NodeRole.Branch ? target : target.Parent;
        if (parent is null)
        {
            SetStatus("当前插入位置没有合法父容器。");
            return;
        }
        var index = target.Role is NodeRole.Phase or NodeRole.Branch
            ? after ? parent.Children.Count : 0
            : parent.Children.IndexOf(target) + (after ? 1 : 0);
        Checkpoint();
        var added = StepCatalog.New(kind);
        added.Parent = parent;
        parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), added);
        StepPalette.IsOpen = false;
        SelectNode(added, scroll: true);
        Dirty("已插入 " + kind + "；当前属性区可以直接编辑。");
        if (_compactMode) ShowCompactDetails();
    }
    private EditorNode? SelectedEditable() =>
        SelectedNode is { IsMovable: true } node ? node : null;

    private void DuplicateStep_Click(object sender, RoutedEventArgs e)
    {
        var node = SelectedEditable();
        if (node?.Parent is not EditorNode parent) { SetStatus("先选择一个可复制的步骤或流程节点。"); return; }
        Checkpoint();
        var copy = node.CloneWithFreshIds();
        copy.Title += " 副本"; copy.Parent = parent;
        parent.Children.Insert(parent.Children.IndexOf(node) + 1, copy);
        SelectNode(copy, scroll: true);
        Dirty("已复制当前步骤及其合法子结构；Ctrl+Z 可撤销。");
    }
    private void CopyStep_Click(object sender, RoutedEventArgs e) => CopyStep();
    private void PasteStep_Click(object sender, RoutedEventArgs e) => PasteStep();
    private void CopyStep()
    {
        var node = SelectedEditable();
        if (node is null) { SetStatus("请选择一个步骤或流程节点进行复制。"); return; }
        _copiedStep = JsonSerializer.Serialize(node);
        SetStatus("已复制 " + node.DisplayTitle + " 到本原型内部步骤缓冲（不包含硬件运行数据）。");
    }
    private void PasteStep()
    {
        if (_copiedStep is null || SelectedCase is null)
        { SetStatus("内部步骤缓冲为空，请先复制一个步骤。"); return; }
        var node = SelectedNode ?? SelectedCase.Phases[1];
        if (node.Role == NodeRole.Container)
        { SetStatus("请先选择具体分支后再粘贴，不能隐式插入 IF 或 SWITCH。"); return; }
        var parent = node.Role is NodeRole.Phase or NodeRole.Branch ? node : node.Parent;
        if (parent is null) return;
        var index = node.Role is NodeRole.Phase or NodeRole.Branch
            ? parent.Children.Count : parent.Children.IndexOf(node) + 1;
        var copy = JsonSerializer.Deserialize<EditorNode>(_copiedStep)!.CloneWithFreshIds();
        copy.Parent = parent; copy.Title += " 副本";
        Checkpoint(); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), copy);
        SelectNode(copy, scroll: true);
        Dirty("已粘贴一份新步骤/子树，实体身份不同于原步骤，可撤销。");
    }
    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        var node = SelectedEditable();
        if (node?.Parent is not EditorNode parent)
        {
            SetStatus("只能删除步骤或流程容器，不能删除固定阶段和分支槽。");
            return;
        }
        Checkpoint();
        parent.Children.Remove(node);
        SelectNode(parent, scroll: true);
        Dirty("已删除步骤（含子树）；Ctrl+Z 可恢复。");
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSibling(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSibling(1);
    private void MoveSibling(int offset)
    {
        var node = SelectedEditable();
        if (node?.Parent is not EditorNode parent) return;
        var before = parent.Children.IndexOf(node);
        var next = before + offset;
        if (next < 0 || next >= parent.Children.Count)
        {
            SetStatus("已到当前分支边界；跨分支移动请使用“移动到…”。");
            return;
        }
        Checkpoint();
        parent.Children.Move(before, next);
        SelectNode(node, scroll: true);
        Dirty("同级位置已修改；未改变步骤所属阶段/分支。");
    }
    private void MoveTo_Click(object sender, RoutedEventArgs e)
    {
        var node = SelectedEditable();
        if (node is null || SelectedCase is null)
        {
            SetStatus("先选择一个步骤，再指定移动目标。");
            return;
        }
        var anchor = sender as FrameworkElement ?? StepList;
        var menu = new ContextMenu();
        foreach (var phase in SelectedCase.Phases)
        {
            var phaseMenu = new MenuItem { Header = phase.Title };
            phaseMenu.Click += (_, _) => MoveTo(node, phase);
            menu.Items.Add(phaseMenu);
            foreach (var target in AllNodes(phase).Where(n => n.Role == NodeRole.Branch))
            {
                var branch = target;
                var item = new MenuItem { Header = "    " + ParentCaption(branch) + "  /  " + branch.Title };
                item.IsEnabled = !ReferenceEquals(branch, node) && !IsAncestor(node, branch);
                item.Click += (_, _) => MoveTo(node, branch);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
        }
        menu.PlacementTarget = anchor; menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private static string ParentCaption(EditorNode node) => node.Parent?.DisplayTitle ?? "未知父节点";
    private void MoveTo(EditorNode node, EditorNode target)
    {
        if (node.Parent is null || target.Role is not (NodeRole.Phase or NodeRole.Branch) ||
            ReferenceEquals(node, target) || IsAncestor(node, target))
        {
            SetStatus("不能移动至当前节点或其子树。"); return;
        }
        Checkpoint();
        node.Parent.Children.Remove(node);
        target.Children.Add(node); node.Parent = target;
        target.IsExpanded = true;
        SelectNode(node, scroll: true);
        Dirty("已显式移动至 " + target.DisplayTitle + "；Ctrl+Z 可撤销。");
    }
    private void WrapIf_Click(object sender, RoutedEventArgs e) => WrapSelected("IF / ELSE");
    private void WrapLoop_Click(object sender, RoutedEventArgs e) => WrapSelected("循环 LOOP");
    private void WrapSelected(string kind)
    {
        var selected = StepList.SelectedItems.OfType<StepRow>()
            .Select(x => x.Node).Distinct().ToArray();
        if (selected.Length == 0 && SelectedEditable() is { } single) selected = [single];
        if (selected.Length == 0 || selected.Any(x => !x.IsMovable || x.Parent is null))
        {
            SetStatus("请选择同一分支内连续的可移动步骤，再使用包裹操作。"); return;
        }
        var parent = selected[0].Parent!;
        var indexes = selected.Select(s => parent.Children.IndexOf(s)).OrderBy(i => i).ToArray();
        if (selected.Any(n => !ReferenceEquals(n.Parent, parent)) ||
            indexes.Any(i => i < 0) ||
            indexes[^1] - indexes[0] + 1 != indexes.Length)
        {
            SetStatus("包裹操作必须选择同一父级下连续的步骤，避免意外跨分支。"); return;
        }
        Checkpoint();
        var wrapper = StepCatalog.New(kind);
        var target = wrapper.Children[0];
        foreach (var node in selected.OrderBy(n => parent.Children.IndexOf(n)).ToArray())
        {
            parent.Children.Remove(node); target.Children.Add(node); node.Parent = target;
        }
        wrapper.Parent = parent;
        parent.Children.Insert(indexes[0], wrapper);
        SelectNode(wrapper, scroll: true);
        Dirty("已将 " + selected.Length + " 步包裹到 " + kind + "，一次 Ctrl+Z 可恢复。");
    }
    private void ToggleEnabled_Click(object sender, RoutedEventArgs e)
    {
        var node = SelectedEditable();
        if (node is null) return;
        Checkpoint();
        node.IsEnabled = !node.IsEnabled;
        UpdateRows(); Dirty(node.IsEnabled ? "步骤已启用。" : "步骤已停用；不是当次运行选择。");
    }
    private void ToggleBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        var node = SelectedEditable();
        if (node is null) return;
        Checkpoint();
        node.Breakpoint = !node.Breakpoint;
        UpdateRows(); Dirty(node.Breakpoint ? "已设置编辑器断点草稿（无真实单步执行）。" : "已清除断点。");
    }
    private void Enabled_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_uiReady && !_syncing) Checkpoint();
    }
    private void Enabled_PreviewKey(object sender, KeyEventArgs e)
    {
        if (_uiReady && !_syncing && e.Key == Key.Space) Checkpoint();
    }
    private void Enabled_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _syncing) return;
        foreach (var row in Rows) row.Update();
        Dirty("已更新当前步骤的启用状态；不会执行实体控制。");
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();
    private void Undo()
    {
        if (_undo.Count == 0) { SetStatus("没有可以撤销的文档事务。"); return; }
        _redo.Push(Document.Serialize());
        Restore(_undo.Pop());
        SetStatus("已撤销上一项文档编辑事务。");
    }
    private void Redo()
    {
        if (_redo.Count == 0) { SetStatus("没有可以重做的文档事务。"); return; }
        _undo.Push(Document.Serialize());
        Restore(_redo.Pop());
        SetStatus("已重做文档编辑事务。");
    }

    private void EditorText_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_uiReady || _syncing) return;
        _fieldSnapshot = Document.Serialize();
        _editingNodeAtFocus = SelectedNode;
    }
    private void EditorText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_uiReady && !_syncing && _fieldSnapshot is not null)
            Dirty(); // Prevent losing a mid-typing draft on Ctrl+S or window close.
    }
    private void CapturePendingEdit()
    {
        if (_fieldSnapshot is null) return;
        var before = _fieldSnapshot;
        _fieldSnapshot = null;
        if (before == Document.Serialize()) return;
        _undo.Push(before); _redo.Clear();
        _editingNodeAtFocus?.RefreshValidation();
        _editingNodeAtFocus = null;
        RefreshProblems();
        foreach (var row in Rows) row.Update();
        Dirty();
    }
    private void EditorText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_uiReady || _syncing || _fieldSnapshot is null) return;
        var before = _fieldSnapshot; _fieldSnapshot = null;
        var editedNode = _editingNodeAtFocus;
        _editingNodeAtFocus = null;
        if (before == Document.Serialize()) return;
        _undo.Push(before); _redo.Clear();
        if (editedNode is not null)
        {
            editedNode.RefreshValidation();
            if (sender is TextBox { DataContext: EditorField field })
                editedNode.Summary = SummaryFor(editedNode, field);
        }
        // Never rebuild the ListBox during LostKeyboardFocus: that would destroy the
        // row which the user's current mouse click is attempting to select.
        RefreshProblems();
        foreach (var row in Rows) row.Update();
        Dirty("已更新参数草稿；切换到其他步骤也不会丢失未完成输入。");
    }
    private static string SummaryFor(EditorNode node, EditorField changed)
    {
        if (node.Kind == "等待") return (node.Field("ms")?.Text ?? "") + " ms";
        if (node.Kind == "CAN 信号判定")
        {
            var signal = node.Field("signal")?.Text ?? "";
            return (signal.Length == 0 ? "DBC 信号未选择" : signal) + " " +
                (node.Field("operator")?.Text ?? "==") + " " +
                (node.Field("expected")?.Text ?? "");
        }
        if (node.Kind == "UDS 请求") return node.Field("request")?.Text ?? node.Summary;
        if (node.Kind == "循环 LOOP") return "循环 " + (node.Field("count")?.Text ?? "") + " 次";
        return changed.Text.Length <= 62 ? changed.Text : changed.Text[..59] + "…";
    }
    private void PickResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditorField field }) return;
        var list = field.Key switch
        {
            "signal" => new[] { "Demo_Status.Output", "Demo_Status.Ready", "Demo_Status.Counter" },
            "reference" => new[] { "Demo_CurrentReference", "Demo_CurrentReference_v2" },
            "action" => new[] { "Demo_InputAction", "Demo_NoInput" },
            "resource" => new[] { "Demo_BeforeTest", "Demo_Recovery" },
            "message" => new[] { "Demo_Command", "Demo_Alive", "Demo_NetworkConfig" },
            "channel" => new[] { "Demo_MotorCurrent", "Demo_Voltage" },
            _ => new[] { "Demo_Reference_01", "Demo_Reference_02" }
        };
        var menu = new ContextMenu();
        foreach (var value in list)
        {
            var v = value;
            var item = new MenuItem { Header = v };
            item.Click += (_, _) =>
            {
                Checkpoint();
                field.Text = v; SelectedNode?.RefreshValidation();
                UpdateRows();
                Dirty("已选择 UI 研究样本资源 " + v + "；未实际导入或验证 DBC/工程。");
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void Problems_Click(object sender, RoutedEventArgs e)
    {
        ProblemPane.Visibility = ProblemPane.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        SetStatus("问题可定位到当前用例的具体步骤字段。");
    }
    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        RefreshProblems();
        ProblemPane.Visibility = Visibility.Visible;
        SetStatus(Problems.Count == 0
            ? "本地 UI 草稿没有检查到简单字段错误；不代表正式 TEST 工程验证通过。"
            : "本地 UI 草稿发现 " + Problems.Count + " 处字段问题。双击问题定位。");
    }
    private void ProblemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _syncing || e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is EditorProblem problem) NavigateProblem(problem);
    }
    private void ProblemList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProblemList.SelectedItem is EditorProblem p) NavigateProblem(p);
    }
    private void NavigateProblem(EditorProblem p)
    {
        if (SelectedCase?.Find(p.NodeId) is not EditorNode target) return;
        SelectNode(target, scroll: true);
        if (_compactMode) ShowCompactDetails();
        SetStatus("已定位问题：" + p.Message + "；请修正对应字段。");
    }
    private void StepList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedNode is null) return;
        if (_compactMode) ShowCompactDetails();
        SetStatus("进入当前对象的持续参数编辑区：" + SelectedNode.DisplayTitle);
    }
    private void StepList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject child) return;
        var item = ItemsControl.ContainerFromElement(StepList, child) as ListBoxItem;
        if (item?.DataContext is not StepRow row) return;
        if (!item.IsSelected)
        {
            StepList.SelectedItems.Clear();
            StepList.SelectedItem = row;
        }
    }

    private void LayoutPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_uiReady) UpdateResponsive();
    }
    private void CompactDetails_Click(object sender, RoutedEventArgs e)
    {
        if (!_compactMode) return;
        _compactDetails = !_compactDetails;
        UpdateResponsive();
    }
    private void ShowCompactDetails()
    {
        if (!_compactMode) return;
        _compactDetails = true;
        UpdateResponsive();
    }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_uiReady) UpdateResponsive();
    }
    private void UpdateResponsive()
    {
        if (!_uiReady) return;
        _compactMode = ActualWidth < 1170;
        MainHeaderRow.Height = new GridLength(_compactMode ? 52 : 63);
        TaskHeaderRow.Height = new GridLength(_compactMode ? 69 : 87);
        FooterRow.Height = new GridLength(_compactMode ? 27 : 30);
        StepToolbarRow.Height = new GridLength(_compactMode ? 91 : 104);
        if (_compactMode)
        {
            if (!_wasCompact)
            {
                _lastCaseWidth = CaseColumn.ActualWidth > 80 ? CaseColumn.ActualWidth : 255;
                _lastInspectorWidth = InspectorColumn.ActualWidth > 80 ? InspectorColumn.ActualWidth : 380;
            }
            _wasCompact = true;
            CaseColumn.Width = new GridLength(0); CaseSplitColumn.Width = new GridLength(0);
            InspectorColumn.Width = new GridLength(0); InspectorSplitColumn.Width = new GridLength(0);
            CasePane.Visibility = Visibility.Collapsed; CaseSplitter.Visibility = Visibility.Collapsed;
            InspectorSplitter.Visibility = Visibility.Collapsed; RightInspector.Visibility = Visibility.Collapsed;
            BottomRow.Height = new GridLength(0); BottomInspector.Visibility = Visibility.Collapsed;
            StepsPane.Visibility = _compactDetails ? Visibility.Collapsed : Visibility.Visible;
            CompactInspector.Visibility = _compactDetails ? Visibility.Visible : Visibility.Collapsed;
            CompactDetailsButton.Content = _compactDetails ? "← 步骤结构" : "编辑详情 →";
            CompactDetailsButton.Visibility = Visibility.Visible;
            CompactCasePicker.Visibility = Visibility.Visible;
            LayoutPicker.Visibility = Visibility.Collapsed;
            LayoutLabel.Visibility = Visibility.Collapsed;
            ValidateButton.Visibility = Visibility.Collapsed;
            BreadcrumbBar.Visibility = Visibility.Collapsed;
            HeaderActions.Visibility = Visibility.Collapsed;
            CompactFileMenu.Visibility = Visibility.Visible;
        }
        else
        {
            _wasCompact = false;
            CaseColumn.Width = new GridLength(Math.Clamp(_lastCaseWidth, 195, 335));
            CaseSplitColumn.Width = new GridLength(7);
            CasePane.Visibility = Visibility.Visible; CaseSplitter.Visibility = Visibility.Visible;
            InspectorSplitter.Visibility = LayoutPicker.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactInspector.Visibility = Visibility.Collapsed;
            CompactDetailsButton.Visibility = Visibility.Collapsed;
            CompactCasePicker.Visibility = Visibility.Collapsed;
            LayoutPicker.Visibility = Visibility.Visible; LayoutLabel.Visibility = Visibility.Visible;
            ValidateButton.Visibility = Visibility.Visible;
            BreadcrumbBar.Visibility = Visibility.Visible;
            HeaderActions.Visibility = Visibility.Visible; CompactFileMenu.Visibility = Visibility.Collapsed;
            StepsPane.Visibility = Visibility.Visible;
            if (LayoutPicker.SelectedIndex == 1)
            {
                InspectorColumn.Width = new GridLength(0); InspectorSplitColumn.Width = new GridLength(0);
                RightInspector.Visibility = Visibility.Collapsed;
                BottomRow.Height = new GridLength(265);
                BottomInspector.Visibility = Visibility.Visible;
            }
            else
            {
                InspectorColumn.Width = new GridLength(Math.Clamp(_lastInspectorWidth, 315, 510));
                InspectorSplitColumn.Width = new GridLength(7);
                RightInspector.Visibility = Visibility.Visible;
                BottomRow.Height = new GridLength(0);
                BottomInspector.Visibility = Visibility.Collapsed;
            }
        }
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_uiReady) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S) { Save_Click(sender, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { Open_Click(sender, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { StepSearch.Focus(); StepSearch.SelectAll(); e.Handled = true; return; }
        // Native TextBox Undo and IME editing have priority over document-level history.
        if (Keyboard.FocusedElement is TextBox) return;
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopyStep(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { PasteStep(); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { CopyStep(); DeleteStep_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Insert) { OpenPalette_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F2) { RenameSelectedStep(); e.Handled = true; }
        else if (e.Key == Key.Delete && Keyboard.FocusedElement is ListBoxItem) { DeleteStep_Click(sender, e); e.Handled = true; }
    }
    private void RenameSelectedStep()
    {
        if (SelectedEditable() is not EditorNode node) return;
        if (_compactMode) ShowCompactDetails();
        var root = _compactMode ? (DependencyObject)CompactInspector :
            LayoutPicker.SelectedIndex == 1 ? BottomInspector : RightInspector;
        var field = FindNameEditor(root, node);
        if (field is not null) { field.Focus(); field.SelectAll(); }
        else SetStatus("步骤名称可在当前详情面板直接编辑。");
    }
    private static TextBox? FindNameEditor(DependencyObject root, EditorNode node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox tb && ReferenceEquals(tb.DataContext, node)) return tb;
            var found = FindNameEditor(child, node);
            if (found is not null) return found;
        }
        return null;
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_modified && MessageBox.Show("重新加载研究样本会丢弃当前未保存的 UI 草稿。是否继续？",
                "重置 UI 样本", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        ReplaceDocument(SampleData.Create(), clearHistory: true, markSaved: true);
        SetStatus("已重置 12 个研究用例与 40 步内容。");
    }
    private bool SaveDraft()
    {
        CommitCaseRename();
        CapturePendingEdit();
        var path = _currentFile;
        if (string.IsNullOrEmpty(path))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "AutoTest UI 研究草稿 (*.autotest-ui-preview.json)|*.autotest-ui-preview.json|JSON 文件 (*.json)|*.json",
                FileName = "TEST-视觉操作研究.autotest-ui-preview.json"
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try
        {
            // A preview JSON is never a formal TEST DSL or run evidence file.
            Document.ActiveCaseId = SelectedCase?.Id ?? "";
            Document.ActiveNodeId = SelectedNode?.Id ?? "";
            File.WriteAllText(path!, Document.Serialize());
            _currentFile = path;
            _modified = false; Notify(nameof(SaveStateText));
            SetStatus("已保存 UI 研究草稿：" + Path.GetFileName(path) + "（非正式工程）。");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "UI 草稿", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveDraft();
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_modified && MessageBox.Show(this, "当前 UI 草稿尚未保存。仍要打开其他草稿吗？",
            "打开草稿", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var dialog = new OpenFileDialog
        {
            Filter = "AutoTest UI 研究草稿 (*.autotest-ui-preview.json)|*.autotest-ui-preview.json|JSON 文件 (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = EditorDocument.Load(File.ReadAllText(dialog.FileName));
            ReplaceDocument(result, clearHistory: true, markSaved: true);
            _currentFile = dialog.FileName;
            SetStatus("已打开 UI 草稿：" + Path.GetFileName(dialog.FileName) + "。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, "无法打开 UI 研究草稿：" + ex.Message,
                "打开草稿", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitCaseRename();
        CapturePendingEdit();
        if (!_modified) return;
        var choice = MessageBox.Show(this, "当前有未保存的 UI 草稿。\n“是”保存，“否”放弃，“取消”返回编辑器。",
            "关闭研究原型", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (choice == MessageBoxResult.Yes && !SaveDraft()) e.Cancel = true;
    }
}
