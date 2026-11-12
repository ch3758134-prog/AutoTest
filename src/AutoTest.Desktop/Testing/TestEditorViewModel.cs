using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

/// <summary>One StepEditTransaction per TEST document; no alternate authoring model.</summary>
public sealed class TestEditorViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly TestEditorDialogs _dialogs;
    private readonly StepDescriptorCatalog _catalog = StepDescriptorCatalog.Builtins();
    private TestEditorSession _session;
    private StepEditTransaction _transaction;
    private StepTreeItem? _selectedItem;
    private StepUiChoice _selectedType = StepUiCatalog.Choices[0];
    private bool _isBusy;
    private string _status = "请选择阶段或步骤进行编辑。";

    public TestEditorViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestEditorSession session)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _session = session;
        _transaction = new StepEditTransaction(session.Document);

        AddChildCommand = new DelegateCommand(AddChild, () => CanAddChild);
        AddAfterCommand = new DelegateCommand(AddAfter, () => CanEditSelectedStep);
        EditParametersCommand = new DelegateCommand(EditParameters, () => CanEditSelectedStep);
        DuplicateCommand = new DelegateCommand(Duplicate, () => CanEditSelectedStep);
        DeleteCommand = new DelegateCommand(Delete, () => CanEditSelectedStep);
        MoveUpCommand = new DelegateCommand(() => Move(-1), () => CanMoveUp);
        MoveDownCommand = new DelegateCommand(() => Move(1), () => CanMoveDown);
        UndoCommand = new DelegateCommand(Undo, () => _transaction.CanUndo && !IsBusy);
        RedoCommand = new DelegateCommand(Redo, () => _transaction.CanRedo && !IsBusy);
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => IsDirty && !IsBusy);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        RefreshTree(null);
    }

    public ObservableCollection<StepTreeItem> StepRoots { get; } = new();
    public IReadOnlyList<StepUiChoice> StepTypes => StepUiCatalog.Choices;
    public ObservableCollection<KeyValuePair<string, string>> InspectorProperties { get; } = new();
    public StepUiChoice SelectedStepType
    {
        get => _selectedType;
        set => SetProperty(ref _selectedType, value);
    }
    public string DocumentName => _session.AssetName;
    public string DocumentPath => _session.FilePath;
    public int AssetRevision => _session.AssetRevision;
    public bool IsDirty => !string.Equals(
        TestDocumentCodec.Serialize(_transaction.Current),
        TestDocumentCodec.Serialize(_session.Document), StringComparison.Ordinal);
    public bool CanAddChild => !IsBusy && SelectedItem?.Kind is
        StepTreeItemKind.Phase or StepTreeItemKind.Slot;
    public bool CanEditSelectedStep => !IsBusy &&
        SelectedItem?.Kind == StepTreeItemKind.Step;
    public bool CanMoveUp => CanEditSelectedStep && SelectedItem!.Index > 0;
    public bool CanMoveDown => CanEditSelectedStep &&
        SelectedItem?.Parent is { } p && SelectedItem.Index + 1 < p.Children.Count;
    public string SelectedTitle => _selectedItem?.Label ?? "选择一个阶段或步骤";
    public string SelectedTypeLabel => _selectedItem?.Step is { } n
        ? StepUiCatalog.Label(n.Type) + " · " + n.Type
        : _selectedItem?.Kind == StepTreeItemKind.Slot ? "子步骤容器" : "测试阶段";
    public string SelectedId => _selectedItem?.Step?.Id.ToString() ?? "";
    public string SelectedDescription => _selectedItem switch
    {
        { Kind: StepTreeItemKind.Phase } => "阶段内的步骤按顺序执行。选择步骤类型后点击“添加到阶段”。",
        { Kind: StepTreeItemKind.Slot } => "这是条件或循环的真实子步骤列表。点击“添加到此分支”。",
        { Kind: StepTreeItemKind.Step, Step: { } n } =>
            StepUiCatalog.Choices.FirstOrDefault(x => x.Type == n.Type)?.Summary
            ?? "当前步骤的参数和子分支由正式数据模型管理。",
        _ => "在左侧选择“准备 / 测试 / 清理”，或已有步骤。"
    };
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); }
    }
    public StepTreeItem? SelectedItem => _selectedItem;

    public DelegateCommand AddChildCommand { get; }
    public DelegateCommand AddAfterCommand { get; }
    public DelegateCommand EditParametersCommand { get; }
    public DelegateCommand DuplicateCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand MoveUpCommand { get; }
    public DelegateCommand MoveDownCommand { get; }
    public DelegateCommand UndoCommand { get; }
    public DelegateCommand RedoCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }

    public void SelectItem(StepTreeItem? item)
    {
        if (ReferenceEquals(_selectedItem, item)) return;
        if (_selectedItem is not null) _selectedItem.IsSelected = false;
        _selectedItem = item;
        if (item is not null) item.IsSelected = true;
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        InspectorProperties.Clear();
        if (_selectedItem?.Step is { } step)
        {
            foreach (var property in step.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
                InspectorProperties.Add(new KeyValuePair<string, string>(
                    StepUiCatalog.PropertyLabel(property.Key), property.Value));
        }
        RaisePropertyChanged(nameof(SelectedTitle));
        RaisePropertyChanged(nameof(SelectedTypeLabel));
        RaisePropertyChanged(nameof(SelectedId));
        RaisePropertyChanged(nameof(SelectedDescription));
        RefreshCommands();
    }

    private void AddChild()
    {
        if (!CanAddChild || _selectedItem is null) return;
        var target = _selectedItem;
        var next = StepUiCatalog.NewNode(SelectedStepType.Type);
        Mutate(() =>
        {
            var coordinates = target.CoordinatesForAppend();
            _transaction.Insert(target.Phase, coordinates.ParentId, coordinates.Slot,
                coordinates.Index, next);
        }, next.Id, "已添加 " + next.Name);
    }

    private void AddAfter()
    {
        if (!CanEditSelectedStep || _selectedItem is null) return;
        var target = _selectedItem;
        var node = StepUiCatalog.NewNode(SelectedStepType.Type);
        Mutate(() =>
        {
            var coordinates = target.CoordinatesForSibling();
            _transaction.Insert(target.Phase, coordinates.ParentId, coordinates.Slot,
                coordinates.Index, node);
        }, node.Id, "已在选中步骤后插入 " + node.Name);
    }

    private void EditParameters()
    {
        var node = _selectedItem?.Step;
        if (!CanEditSelectedStep || node is null) return;
        var result = _dialogs.EditParameters(node);
        if (result is null) return;
        Mutate(() =>
        {
            var preview = new StepEditTransaction(_transaction.Current);
            preview.ApplyProperties(node.Id, result.Value.Name, result.Value.Properties);
            var issues = StepTreeValidator.ValidateTree(preview.Current, _catalog)
                .Where(x => !x.Contains("unresolved asset", StringComparison.Ordinal)).ToArray();
            if (issues.Length > 0) throw new ArgumentException(string.Join("\n", issues.Take(8)));
            _transaction.ApplyProperties(node.Id, result.Value.Name, result.Value.Properties);
        }, node.Id, "已应用步骤参数（尚未保存）");
    }

    private void Duplicate()
    {
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node) return;
        var selected = _selectedItem;
        var before = _transaction.Current.Walk().Select(x => x.Id).ToHashSet();
        try
        {
            var location = selected.CoordinatesForSibling();
            _transaction.Copy(node.Id, selected.Phase, location.ParentId, location.Slot, location.Index);
            var clonedId = _transaction.Current.Walk().First(x => !before.Contains(x.Id)).Id;
            RefreshTree(clonedId);
            StatusMessage = "步骤已复制，新节点分配独立 StepId。";
        }
        catch (Exception e) when (IsEditorError(e)) { _dialogs.ShowError(e.Message); }
    }

    private void Delete()
    {
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node) return;
        if (!_dialogs.Confirm("删除此步骤及其全部子步骤？可通过撤销恢复。")) return;
        Mutate(() => _transaction.Delete(node.Id), null, "步骤已删除（可撤销）");
    }

    private void Move(int direction)
    {
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node) return;
        if (direction == -1 && !CanMoveUp || direction == 1 && !CanMoveDown) return;
        var selected = _selectedItem;
        var sibling = selected.CoordinatesForSibling(0);
        var destination = direction < 0 ? selected.Index - 1 : selected.Index + 2;
        Mutate(() => _transaction.Move(node.Id, selected.Phase, sibling.ParentId,
            sibling.Slot, destination), node.Id, "步骤顺序已调整（可撤销）");
    }

    private void Undo()
    {
        if (!_transaction.CanUndo || IsBusy) return;
        _transaction.Undo();
        RefreshTree(_selectedItem?.Step?.Id);
        StatusMessage = "已撤销";
    }
    private void Redo()
    {
        if (!_transaction.CanRedo || IsBusy) return;
        _transaction.Redo();
        RefreshTree(_selectedItem?.Step?.Id);
        StatusMessage = "已重做";
    }

    private void Mutate(Action action, StepId? select, string status)
    {
        try
        {
            action();
            RefreshTree(select);
            StatusMessage = status;
        }
        catch (Exception error) when (IsEditorError(error)) { _dialogs.ShowError(error.Message); }
    }

    private void RefreshTree(StepId? preferred)
    {
        var previous = _selectedItem;
        StepRoots.Clear();
        _selectedItem = null;
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            var root = new StepTreeItem(StepTreeItemKind.Phase, phase,
                StepUiCatalog.PhaseLabel(phase) + " (" + _transaction.Current.Get(phase).Length + ")");
            StepRoots.Add(root);
            AddNodes(root, _transaction.Current.Get(phase));
        }
        var all = StepTreeItem.Walk(StepRoots).ToArray();
        StepTreeItem? selected = null;
        if (preferred is { } id)
            selected = all.FirstOrDefault(x => x.Step?.Id == id);
        if (selected is null && previous is { Kind: StepTreeItemKind.Slot, Parent.Step: { } parent })
            selected = all.FirstOrDefault(x => x.Kind == StepTreeItemKind.Slot &&
                x.Parent?.Step?.Id == parent.Id && x.SlotName == previous.SlotName);
        if (selected is null && previous is { Kind: StepTreeItemKind.Phase })
            selected = StepRoots.First(x => x.Phase == previous.Phase);
        SelectItem(selected ?? StepRoots[1]);
        RefreshCommands();
    }

    private static void AddNodes(StepTreeItem parent, System.Collections.Immutable.ImmutableArray<StepNode> nodes)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            var step = nodes[i];
            var summary = step.Type switch
            {
                "If" => step.Properties.GetValueOrDefault("variable", "") + " = " +
                    step.Properties.GetValueOrDefault("equals", ""),
                "Loop" => step.Properties.GetValueOrDefault("count", "") + " 次",
                "Wait" => step.Properties.GetValueOrDefault("milliseconds", "") + " ms",
                _ => ""
            };
            var caption = step.Name + (summary.Length > 0 ? " · " + summary : "");
            var current = new StepTreeItem(StepTreeItemKind.Step, parent.Phase, caption,
                parent, step, index: i);
            parent.Children.Add(current);
            foreach (var slot in step.Slots.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var branch = new StepTreeItem(StepTreeItemKind.Slot, parent.Phase,
                    StepUiCatalog.SlotLabel(slot.Key) + " (" + slot.Value.Length + ")",
                    current, slot: slot.Key);
                current.Children.Add(branch);
                AddNodes(branch, slot.Value);
            }
        }
    }

    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.Confirm("测试程序有未保存修改，确定放弃并关闭吗？"));

    private async Task SaveAsync()
    {
        if (!IsDirty || IsBusy) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.SaveTestEditorAsync(_session, _transaction.Current);
            _transaction = new StepEditTransaction(_session.Document);
            RefreshTree(_selectedItem?.Step?.Id);
            RaisePropertyChanged(nameof(AssetRevision));
            StatusMessage = "测试程序已保存并重新读取；工程资源修订 " + AssetRevision;
        }
        catch (Exception e) when (IsEditorError(e)) { StatusMessage = "保存失败：" + e.Message; _dialogs.ShowError(StatusMessage); }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync()
    {
        if (IsBusy) return;
        if (IsDirty && !_dialogs.Confirm("重新加载会放弃尚未保存的步骤修改，确定继续？")) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.OpenTestEditorAsync(_session.AssetId);
            _transaction = new StepEditTransaction(_session.Document);
            RefreshTree(null);
            RaisePropertyChanged(nameof(AssetRevision));
            StatusMessage = "测试程序已从磁盘重新加载。";
        }
        catch (Exception e) when (IsEditorError(e)) { StatusMessage = "重新加载失败：" + e.Message; _dialogs.ShowError(StatusMessage); }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(IsDirty));
        AddChildCommand?.RaiseCanExecuteChanged();
        AddAfterCommand?.RaiseCanExecuteChanged();
        EditParametersCommand?.RaiseCanExecuteChanged();
        DuplicateCommand?.RaiseCanExecuteChanged();
        DeleteCommand?.RaiseCanExecuteChanged();
        MoveUpCommand?.RaiseCanExecuteChanged();
        MoveDownCommand?.RaiseCanExecuteChanged();
        UndoCommand?.RaiseCanExecuteChanged();
        RedoCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
        ReloadCommand?.RaiseCanExecuteChanged();
    }

    private static bool IsEditorError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ProjectDataException or
        InvalidOperationException or InvalidDataException or FormatException or
        ArgumentException or KeyNotFoundException or NotSupportedException;
}
