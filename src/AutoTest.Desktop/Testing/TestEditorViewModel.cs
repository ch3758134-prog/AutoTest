using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using AutoTest.Testing.Preparation;
using AutoTest.Specialists.Wave;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed record StepInspectorContext(
    StepNode? Original, string? InitialActionKey,
    IReadOnlyList<MappingChoice> Choices,
    IReadOnlyList<TestTargetMapping> ExistingMappings,
    bool CaseOnly = false);


/// <summary>One StepEditTransaction per TEST document; no alternate authoring model.</summary>
public sealed class TestEditorViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly TestEditorDialogs _dialogs;
    private readonly RealTestWorkbenchService _realTest;
    private TestTargetMapping? _selectedMapping;
    private string _approval = "";
    private string _runSummary = "未运行真实测试";
    private readonly StepDescriptorCatalog _catalog = StepDescriptorCatalog.Builtins();
    private OpenProjectContext? _choiceContext;
    private AssetId? _choiceTcf;
    private IReadOnlyList<MappingChoice>? _choiceCache;
    private Task<IReadOnlyList<MappingChoice>>? _choiceLoading;

    private TestEditorSession _session;
    private StepEditTransaction _transaction;
    private StepTreeItem? _selectedItem; // transient projection; stable identity lives in _interaction
    private readonly TestEditorInteractionState _interaction = new();
    private StepUiChoice _selectedType = StepUiCatalog.Choices[0];
    private bool _isBusy;
    private string _status = "请选择阶段或步骤进行编辑。";

    public TestEditorViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestEditorSession session,
        RealTestWorkbenchService realTest)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _realTest = realTest;
        _session = session;
        _transaction = new StepEditTransaction(session.Document);

        AddChildCommand = new DelegateCommand(() => BeginInsert(null),
            () => !IsBusy && StepInsertionTarget.CanAppend(SelectedItem));
        // Main action: phase/slot -> append; existing step -> insert below.
        AddStepCommand = new DelegateCommand(() => BeginInsertAtSelection(null),
            () => !IsBusy && SelectedItem is not null);
        QuickAddCommand = new DelegateCommand<string>(BeginInsertAtSelection,
            _ => !IsBusy && SelectedItem is not null);
        AddAfterCommand = new DelegateCommand(BeginInsertAfter,
            () => CanEditSelectedStep);
        EditParametersCommand = new DelegateCommand(() =>
        {
            if (_selectedItem?.Step is not { } original || !CanProceed()) return;
            if (_catalog.Lookup(original.Type) is null)
            {
                StatusMessage = "该未知步骤类型尚不能编辑：" + original.Type;
                return;
            }
            _inspectorAdding = false;
            _ = PublishInspectorAsync(original, null);
        }, () => CanEditSelectedStep);
        DuplicateCommand = new DelegateCommand(Duplicate, () => CanEditSelectedStep);
        DeleteCommand = new DelegateCommand(Delete, () => CanEditSelectedStep);
        MoveUpCommand = new DelegateCommand(() => Move(-1), () => CanMoveUp);
        MoveDownCommand = new DelegateCommand(() => Move(1), () => CanMoveDown);
        UndoCommand = new DelegateCommand(Undo, () => _transaction.CanUndo && !IsBusy);
        RedoCommand = new DelegateCommand(Redo, () => _transaction.CanRedo && !IsBusy);
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => IsDirty && !IsBusy);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        PreflightCommand = new DelegateCommand(PreflightOnly, () => !IsBusy);
        CopyStepCommand = new DelegateCommand(CopyToClipboard, () => CanEditSelectedStep);
        PasteStepCommand = new DelegateCommand(PasteFromClipboard,
            () => !IsBusy && SelectedItem is not null && _clipboardStepId is not null);
        AddMappingCommand = new DelegateCommand(async () => await AddMappingAsync(), () => !IsBusy);
        RemoveMappingCommand = new DelegateCommand(RemoveMapping, () => SelectedMapping is not null && !IsBusy);
        RunCommand = new DelegateCommand(async () => await RunRealAsync(), () => !IsBusy && !IsDirty);
        StopRunCommand = new DelegateCommand(() => _realTest.RequestStop(), () => IsBusy && _realTest.IsRunning);
        RefreshMappings();
        RefreshTree(null);
    }

    public ObservableCollection<StepTreeItem> StepRoots { get; } = new();
    public ObservableCollection<StepRowProjection> StepRows { get; } = new();
    public string StepCountLabel => _transaction.Current.Walk().Count() + " 步";
    private StepRowProjection? _selectedRow;
    private bool _syncingRowSelection;
    public StepRowProjection? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (_syncingRowSelection)
            {
                SetProperty(ref _selectedRow, value);
                return;
            }
            if (ReferenceEquals(_selectedRow, value)) return;
            // WPF temporarily clears selection while filtering/rebuilding rows.
            // Do not erase the canonical StepId or call an Inspector leave guard.
            if (value is null)
            {
                RaisePropertyChanged(nameof(SelectedRow));
                return;
            }
            SelectItem(value.Item);
        }
    }
    public string StepFind
    {
        get => _stepFind;
        set
        {
            if (SetProperty(ref _stepFind, value ?? ""))
            {
                RefreshRows();
                RaisePropertyChanged(nameof(SearchMatchCount));
            }
        }
    }
    public int SearchMatchCount => StepTreeItem.Walk(StepRoots).Count(MatchesSearch);
    private bool MatchesSearch(StepTreeItem item)
    {
        var query = _stepFind.Trim();
        if (query.Length == 0 || item.Step is not { } node) return false;
        return (node.Name + " " + StepUiCatalog.Label(node.Type) + " " +
            string.Join(" ", node.Properties.Select(x => x.Key + " " + x.Value)))
            .Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
    public bool NavigateSearch(bool reverse = false)
    {
        if (string.IsNullOrWhiteSpace(_stepFind)) return false;
        var matches = StepTreeItem.Walk(StepRoots).Where(MatchesSearch).ToArray();
        if (matches.Length == 0)
        {
            StatusMessage = "没有找到匹配的步骤。";
            return false;
        }
        var currentId = _selectedItem?.Step?.Id;
        var current = Array.FindIndex(matches, x => x.Step?.Id == currentId);
        var next = reverse
            ? (current < 0 ? matches.Length - 1 : (current - 1 + matches.Length) % matches.Length)
            : (current + 1) % matches.Length;
        if (!CanProceed()) return false;
        var target = matches[next];
        _interaction.ExpandAncestors(target);
        RefreshRows();
        SelectItem(target);
        StatusMessage = "已定位第 " + (next + 1) + "/" + matches.Length + " 条搜索结果。";
        return true;
    }
    private string _stepFind = "";
    private StepId? _clipboardStepId;
    public void ToggleExpansion(StepRowProjection row)
    {
        var wanted = StepSelectionKey.Of(row.Item);
        if (wanted is null) return;
        SelectItem(row.Item);
        if (_interaction.Selection != wanted) return; // rejected leave: do not fold
        var current = wanted.Value.FindIn(StepRoots);
        if (current is null) return;
        _interaction.ToggleCollapsed(current);
        RefreshRows();
    }
    public ObservableCollection<TestTargetMapping> Mappings { get; } = new();
    public ObservableCollection<RealStepFact> RunFacts { get; } = new();
    public TestTargetMapping? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (SetProperty(ref _selectedMapping, value))
                RemoveMappingCommand?.RaiseCanExecuteChanged();
        }
    }
    public string RunApproval
    {
        get => _approval;
        set => SetProperty(ref _approval, value);
    }
    public string ExpectedApproval
    {
        get
        {
            try { return RealTestWorkbenchService.ExpectedApproval(_realTest.Prepare(_transaction.Current)); }
            catch { return ""; }
        }
    }
    public string RunSummary { get => _runSummary; private set => SetProperty(ref _runSummary, value); }
    public string MappingInfo => Mappings.Count + " 个已配置对象";
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
    public bool CanAddChild => !IsBusy && StepInsertionTarget.CanAppend(SelectedItem);
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
        { Kind: StepTreeItemKind.Phase } => "此阶段的步骤按顺序执行。点击“＋ 添加步骤”并直接配置。",
        { Kind: StepTreeItemKind.Slot } => "这是条件/循环的子分支；点击“＋ 添加步骤”将动作加入此分支。",
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
    public event Action<StepInspectorContext>? InspectorRequested;
    public Func<bool>? AllowEditorAction { get; set; }
    private bool _applyingInspector;
    public bool IsApplyingInspector => _applyingInspector;
    private bool CanProceed() => _applyingInspector || (AllowEditorAction?.Invoke() ?? true);

    private StepInsertionTarget? _pendingInsertion;
    private bool _inspectorAdding;
    private StepId? _newDraftId;
    private string? _newDraftActionKey;
    public bool HasNewStepDraft => _newDraftId is not null;
    public event Action<bool>? ActionPaletteRequested;
    // Inspector request versions are owned by TestEditorInteractionState.

    private void BeginInsert(string? actionKey) =>
        StartInsert(_selectedItem, actionKey, StepInsertionMode.Append);

    private void BeginInsertAtSelection(string? actionKey) =>
        StartInsert(_selectedItem, actionKey, _selectedItem?.Kind == StepTreeItemKind.Step
            ? StepInsertionMode.After : StepInsertionMode.Append);

    private void BeginInsertAfter() =>
        StartInsert(_selectedItem, null, StepInsertionMode.After);

    /// <summary>Used by each explicit THEN/ELSE/CASE/Loop context-menu command.</summary>
    public void BeginInsertInto(StepTreeItem slot) =>
        StartInsert(slot, null, StepInsertionMode.Append);

    private StepTreeItem? ResolveLiveTreeItem(StepTreeItem? requested) =>
        StepSelectionKey.Of(requested) is { } key ? key.FindIn(StepRoots) : null;

    private void StartInsert(StepTreeItem? location, string? actionKey, StepInsertionMode mode)
    {
        if (IsBusy || location is null || !CanProceed()) return;
        // Applying pending edits may reconstruct the tree or insert a sibling.
        // Capture new coordinates only after resolving the live location.
        location = ResolveLiveTreeItem(location);
        if (location is null)
        {
            StatusMessage = "目标分支已经变化，请重新选择插入位置。";
            return;
        }
        try { _pendingInsertion = StepInsertionTarget.Resolve(location, mode); }
        catch (InvalidOperationException error)
        {
            StatusMessage = error.Message;
            return;
        }
        _inspectorAdding = true;
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            ActionPaletteRequested?.Invoke(_pendingInsertion.RequiresCase);
            StatusMessage = "选择操作类型后将立即加入可撤销草稿；右侧直接配置。";
            return;
        }
        ChooseNewStepAction(actionKey);
    }

    /// <summary>
    /// The palette creates ONE real, undoable TEST transaction immediately.
    /// The inserted node remains an unsaved editor draft until its Inspector
    /// completes required mapping/field validation. No second persistent model.
    /// </summary>
    public bool ChooseNewStepAction(string actionKey)
    {
        var place = _pendingInsertion;
        var chosen = TestStepInspector.PaletteActions(place?.RequiresCase == true)
            .FirstOrDefault(x => x.Key == actionKey);
        if (place is null || chosen is null || IsBusy) return false;
        try
        {
            var node = StepUiCatalog.NewNode(chosen.StepType) with { Name = chosen.Title };
            place.ValidateNode(node);
            _transaction.Insert(place.Phase, place.ParentId, place.Slot, place.Index, node);
            _newDraftId = node.Id;
            _newDraftActionKey = chosen.Key;
            _inspectorAdding = false;
            _pendingInsertion = null;
            RefreshTree(node.Id);
            StatusMessage = "已插入可撤销草稿步骤。请在右侧填写必要参数并应用；未完成前不得保存资源或执行。";
            return true;
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "无法插入步骤：" + error.Message;
            return false;
        }
    }

    public void CancelInspectorEdit()
    {
        if (_newDraftId is { } provisional)
        {
            try
            {
                if (_transaction.Current.Walk().Any(x => x.Id == provisional))
                    _transaction.Delete(provisional);
                _newDraftId = null;
                _newDraftActionKey = null;
                RefreshTree(null);
                StatusMessage = "已取消新步骤，正式 TEST 文档未保存任何未完成参数。";
                return;
            }
            catch (Exception error) when (IsEditorError(error))
            {
                StatusMessage = "取消草稿失败：" + error.Message;
                return;
            }
        }
        ResetInspector();
    }

    public void ResetInspector()
    {
        _inspectorAdding = _selectedItem?.Kind != StepTreeItemKind.Step;
        _pendingInsertion = null;
        _ = PublishInspectorAsync(_inspectorAdding ? null : _selectedItem?.Step, null);
    }

    private async Task PublishInspectorAsync(StepNode? original, string? initialActionKey)
    {
        var request = _interaction.NextInspectorRequest();
        var choices = await AvailableObjectsAsync();
        if (!_interaction.IsCurrentInspectorRequest(request)) return;
        var insertion = _pendingInsertion;
        if (insertion is null && _inspectorAdding && _selectedItem is { } selected &&
            StepInsertionTarget.CanAppend(selected))
            insertion = StepInsertionTarget.Resolve(selected, StepInsertionMode.Append);
        if (original?.Id == _newDraftId && initialActionKey is null)
            initialActionKey = _newDraftActionKey;
        InspectorRequested?.Invoke(new StepInspectorContext(original, initialActionKey,
            choices, _transaction.Current.Mappings.ToArray(), insertion?.RequiresCase == true));
    }

    public bool ApplyInspector(StepCompositionResult result)
    {
        if (IsBusy || _selectedItem is null || _applyingInspector) return false;
        _applyingInspector = true;
        try
        {
            if (!_inspectorAdding && _selectedItem.Step is { } original)
            {
                _transaction.ReplaceWithMapping(original.Id, result.Step, result.Mapping);
                if (_newDraftId == original.Id)
                {
                    _newDraftId = null;
                    _newDraftActionKey = null;
                }
                RefreshMappings();
                RefreshTree(original.Id);
                StatusMessage = "步骤属性已应用到测试程序草稿；Ctrl+S 保存。";
            }
            else
            {
                var insertion = _pendingInsertion ??
                    StepInsertionTarget.Resolve(_selectedItem, StepInsertionMode.Append);
                insertion.ValidateNode(result.Step);
                _transaction.InsertWithMapping(insertion.Phase, insertion.ParentId,
                    insertion.Slot, insertion.Index, result.Step, result.Mapping);
                RefreshMappings();
                RefreshTree(result.Step.Id);
                StatusMessage = "新步骤已插入测试序列；Ctrl+S 保存。";
            }
            return true;
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "无法应用步骤属性：" + error.Message;
            return false;
        }
        finally { _applyingInspector = false; }
    }


    public DelegateCommand AddChildCommand { get; }
    public DelegateCommand AddStepCommand { get; }
    public DelegateCommand<string> QuickAddCommand { get; }
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
    public DelegateCommand PreflightCommand { get; }
    public DelegateCommand CopyStepCommand { get; }
    public DelegateCommand PasteStepCommand { get; }
    private void CopyToClipboard()
    {
        if (_selectedItem?.Step is not { } step) return;
        _clipboardStepId = step.Id;
        StatusMessage = "已复制所选步骤（当前测试用例内）；选择目标阶段、分支或步骤后 Ctrl+V 粘贴。";
        PasteStepCommand.RaiseCanExecuteChanged();
    }
    private void PasteFromClipboard()
    {
        if (IsBusy || !CanProceed() || _clipboardStepId is not { } source ||
            _selectedItem is not { } selected) return;
        if (!_transaction.Current.Walk().Any(x => x.Id == source))
        {
            StatusMessage = "复制的原步骤已经不存在，无法在本用例中粘贴。";
            _clipboardStepId = null;
            PasteStepCommand.RaiseCanExecuteChanged();
            return;
        }
        try
        {
            var before = _transaction.Current.Walk().Select(x => x.Id).ToHashSet();
            var coords = selected.Kind == StepTreeItemKind.Step
                ? selected.CoordinatesForSibling() : selected.CoordinatesForAppend();
            _transaction.Copy(source, selected.Phase, coords.ParentId, coords.Slot, coords.Index);
            var clone = _transaction.Current.Walk().First(x => !before.Contains(x.Id));
            RefreshTree(clone.Id);
            StatusMessage = "步骤及子分支已复制到目标位置，所有 StepId 已更新；Ctrl+S 保存。";
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "无法粘贴到该分支：" + error.Message;
        }
    }
    public DelegateCommand AddMappingCommand { get; }
    public DelegateCommand RemoveMappingCommand { get; }
    public DelegateCommand RunCommand { get; }
    public DelegateCommand StopRunCommand { get; }

    /// <summary>
    /// Single entry for mouse, keyboard and programmatic STEP navigation.
    /// Never publish a new Inspector until the previous draft is validated.
    /// The identity is stable across immutable TEST tree rebuilds.
    /// </summary>
    public void SelectItem(StepTreeItem? requested)
    {
        var desired = StepSelectionKey.Of(requested);
        if (desired is null) return;
        if (desired == _interaction.Selection && ReferenceEquals(_selectedItem, requested))
            return;
        if (!_interaction.TryNavigate(desired, () => _selectedItem is null || CanProceed()))
        {
            RaisePropertyChanged(nameof(SelectedRow));
            return;
        }
        var live = desired.Value.FindIn(StepRoots);
        if (live is null)
        {
            _interaction.Restore(StepSelectionKey.Of(_selectedItem));
            RaisePropertyChanged(nameof(SelectedRow));
            StatusMessage = "步骤位置已变化，请重新选择。";
            return;
        }
        SelectProjection(live);
    }

    private void SelectProjection(StepTreeItem? item)
    {
        if (_selectedItem is not null) _selectedItem.IsSelected = false;
        _selectedItem = item;
        if (item is not null) item.IsSelected = true;
        var wasSyncing = _syncingRowSelection;
        _syncingRowSelection = true;
        try
        {
            SelectedRow = StepRows.FirstOrDefault(x => ReferenceEquals(x.Item, item));
        }
        finally { _syncingRowSelection = wasSyncing; }
        UpdateInspector();
        _pendingInsertion = null;
        _inspectorAdding = item?.Kind != StepTreeItemKind.Step;
        _ = PublishInspectorAsync(_inspectorAdding ? null : item?.Step, null);
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

    private string DefaultTargetName(string stepType)
    {
        bool Suitable(TestTargetMapping m) => stepType switch
        {
            "Read" or "WaitUntil" or "WaveStart" => m.Kind == TestTargetKind.Signal,
            "Write" => m.Kind is TestTargetKind.Button or TestTargetKind.CanMessage or TestTargetKind.Ignition,
            _ => false
        };
        return Mappings.FirstOrDefault(x => x.Name == SelectedMapping?.Name && Suitable(x))?.Name
            ?? Mappings.FirstOrDefault(Suitable)?.Name ?? "";
    }

    private async Task<IReadOnlyList<MappingChoice>> AvailableObjectsAsync()
    {
        var context = _workspace.Current;
        var tcf = _workspace.SelectedTestConfiguration;
        if (!ReferenceEquals(context, _choiceContext) || tcf != _choiceTcf)
        {
            _choiceContext = context;
            _choiceTcf = tcf;
            _choiceCache = null;
            _choiceLoading = null;
        }
        if (_choiceCache is not null) return _choiceCache;
        try
        {
            var loading = _choiceLoading ??= TestTargetCatalog.LoadAsync(_workspace);
            var choices = await loading;
            if (ReferenceEquals(context, _workspace.Current) &&
                tcf == _workspace.SelectedTestConfiguration)
                _choiceCache = choices;
            return choices;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      UnauthorizedAccessException or ArgumentException or ProjectDataException)
        {
            _choiceLoading = null;
            StatusMessage = "部分工程操作对象无法加载：" + error.Message +
                "；仍可使用等待、条件、IGN 和无 DBC 的原始 CAN 操作。";
            return [new MappingChoice("台架 / IGN", new TestTargetMapping("IGN",
                TestTargetKind.Ignition, default, 0, ""))];
        }
    }

    private void Duplicate()
    {
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node || !CanProceed()) return;
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
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node || !CanProceed()) return;
        if (!_dialogs.Confirm("删除此步骤及其全部子步骤？可通过撤销恢复。")) return;
        Mutate(() => _transaction.Delete(node.Id), null, "步骤已删除（可撤销）");
    }

    private void Move(int direction)
    {
        if (!CanEditSelectedStep || _selectedItem?.Step is not { } node) return;
        if (direction == -1 && !CanMoveUp || direction == 1 && !CanMoveDown || !CanProceed()) return;
        var selected = _selectedItem;
        var sibling = selected.CoordinatesForSibling(0);
        var destination = direction < 0 ? selected.Index - 1 : selected.Index + 2;
        Mutate(() => _transaction.Move(node.Id, selected.Phase, sibling.ParentId,
            sibling.Slot, destination), node.Id, "步骤顺序已调整（可撤销）");
    }

    private void Undo()
    {
        if (!_transaction.CanUndo || IsBusy || !CanProceed()) return;
        _transaction.Undo();
        RefreshTree(_selectedItem?.Step?.Id);
        StatusMessage = "已撤销";
    }
    private void Redo()
    {
        if (!_transaction.CanRedo || IsBusy || !CanProceed()) return;
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

    private void RefreshMappings()
    {
        var selectedName = SelectedMapping?.Name;
        Mappings.Clear();
        foreach (var item in _transaction.Current.Mappings) Mappings.Add(item);
        SelectedMapping = Mappings.FirstOrDefault(x => x.Name == selectedName) ?? Mappings.FirstOrDefault();
        RaisePropertyChanged(nameof(MappingInfo));
        RaisePropertyChanged(nameof(ExpectedApproval));
    }

    private Task AddMappingAsync()
    {
        StatusMessage = "在右侧步骤属性中选择当前工程的 DBC / Controller 对象；应用属性会原子建立修订绑定。";
        return Task.CompletedTask;
    }

    private void RemoveMapping()
    {
        if (SelectedMapping is not { } choice || IsBusy) return;
        try
        {
            _transaction.RemoveMapping(choice.Name);
            RefreshMappings();
            RefreshTree(_selectedItem?.Step?.Id);
            StatusMessage = "已移除映射：" + choice.Name;
        }
        catch (Exception e) when (IsEditorError(e)) { _dialogs.ShowError(e.Message); }
    }

    private void PreflightOnly()
    {
        if (HasNewStepDraft)
        {
            StatusMessage = "步骤草稿尚未完善，不能预检或运行。";
            return;
        }
        if (IsBusy) return;
        try
        {
            var plan = _realTest.Prepare(_transaction.Current);
            if (!plan.Diagnostics.IsEmpty)
            {
                StatusMessage = "BLOCKED_PREFLIGHT: " + plan.Diagnostics.Length +
                    " 项问题；首项：" + plan.Diagnostics[0];
                return;
            }
            StatusMessage = "本地测试用例结构与静态计划检查无错误；未完成项目级 Run、设备身份、授权和资源快照预检，" +
                "尚不可在此按钮触发实体测试。未生成 PASS。";
        }
        catch (Exception error)
        {
            StatusMessage = "BLOCKED_PREFLIGHT: " + error.Message;
        }
    }

    private async Task RunRealAsync()
    {
        if (IsBusy || IsDirty || HasNewStepDraft || !CanProceed()) return;
        IsBusy = true;
        RunFacts.Clear();
        RefreshRows(); // Remove previous-run statuses before starting real execution.
        RunSummary = "正在执行真实测试…";
        StopRunCommand.RaiseCanExecuteChanged();
        try
        {
            var result = await _realTest.ExecuteAsync(_transaction.Current, RunApproval);
            foreach (var fact in result.Facts) RunFacts.Add(fact);
            RefreshRows(); // Apply only actual backend StepId evidence.
            RunSummary = "执行完成 · " + result.Verdict + " · " + result.Facts.Length +
                " 条步骤记录" + (result.EvidencePath is null ? "" :
                " · 证据：" + result.EvidencePath);
            StatusMessage = RunSummary;
        }
        catch (Exception e)
        {
            RunSummary = "真实测试未完成：" + e.Message;
            StatusMessage = RunSummary;
            _dialogs.ShowError(RunSummary);
        }
        finally
        {
            RunApproval = "";
            IsBusy = false;
        }
    }

    private void RefreshTree(StepId? preferred)
    {
        // StepEditTransaction replaces immutable nodes; the UI keeps a stable
        // StepId/Phase/Slot selection while refreshing only its projections.
        var previousKey = _interaction.Selection;
        var wasSyncing = _syncingRowSelection;
        _syncingRowSelection = true;
        try
        {
            StepRoots.Clear();
            foreach (var phase in Enum.GetValues<TestPhase>())
            {
                var root = new StepTreeItem(StepTreeItemKind.Phase, phase,
                    StepUiCatalog.PhaseLabel(phase) + " (" +
                    _transaction.Current.Get(phase).Length + ")");
                StepRoots.Add(root);
                AddNodes(root, _transaction.Current.Get(phase));
            }
            StepTreeItem? selected = null;
            if (preferred is { } id)
                selected = StepTreeItem.Walk(StepRoots).FirstOrDefault(x => x.Step?.Id == id);
            selected ??= previousKey?.FindIn(StepRoots);
            selected ??= StepRoots.First(x => x.Phase == TestPhase.Test);
            _interaction.Restore(StepSelectionKey.Of(selected));
            RefreshRows();
            SelectProjection(selected);
        }
        finally { _syncingRowSelection = wasSyncing; }
        RaisePropertyChanged(nameof(ExpectedApproval));
        RefreshCommands();
    }

    public void CommitQuickValue(StepRowProjection row)
    {
        if (IsBusy || !CanProceed() || row.Item.Step is not { } step || !row.IsQuickEditable) return;
        var key = step.Type == "Wait" ? "milliseconds" : "count";
        var upper = step.Type == "Wait" ? 60000 : 1000;
        if (!int.TryParse(row.QuickValue, out var value) || value < 0 || value > upper)
        {
            StatusMessage = "步骤参数拒绝：请填写 0～" + upper + " 范围内的整数。";
            RefreshRows();
            return;
        }
        if (row.OriginalQuickValue == value.ToString()) return;
        Mutate(() => _transaction.SetProperty(step.Id, key, value.ToString()),
            step.Id, "已直接修改参数；保存后写入 TEST 文档。");
    }

    /// <summary>Move canonical StepNode, preserving StepId, Slots and transaction undo.</summary>
    public void MoveStepAfter(StepRowProjection source, StepRowProjection target)
    {
        if (IsBusy || !CanProceed() || source.Item.Step is not { } moving ||
            target.Item.Step is not { } destination || moving.Id == destination.Id) return;
        var coords = target.Item.CoordinatesForSibling();
        Mutate(() => _transaction.Move(moving.Id, target.Item.Phase, coords.ParentId,
            coords.Slot, coords.Index), moving.Id,
            "步骤已移动；可通过 Ctrl+Z 撤销，Ctrl+S 保存。");
    }

    public void ToggleBreakpoint()
    {
        if (_selectedItem?.Step is not { } step || IsBusy || !CanProceed()) return;
        var next = step.Properties.GetValueOrDefault("editor.breakpoint", "false") == "true"
            ? "false" : "true";
        Mutate(() => _transaction.SetProperty(step.Id, "editor.breakpoint", next),
            step.Id, next == "true" ? "已设置步骤断点（只影响编辑标记，运行器尚未接线）" : "已清除断点");
    }

    public void RenameSelected(string name)
    {
        if (_selectedItem?.Step is not { } step || IsBusy || !CanProceed()) return;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            StatusMessage = "步骤名称不能为空且不能超过 120 字符。";
            return;
        }
        Mutate(() => _transaction.Rename(step.Id, name.Trim()), step.Id, "步骤已重命名；保存后写入正式资源。");
    }

    private void RefreshRows()
    {
        var wasSyncing = _syncingRowSelection;
        _syncingRowSelection = true;
        try
        {
            // The ListBox may emit SelectedItem=null during collection reset.
            // Keep the UI session's identity and never treat that as navigation.
            StepRows.Clear();
            var evidenceByStep = RunFacts.GroupBy(x => x.Step)
                .ToDictionary(x => x.Key, x => x.Last());
            void Append(StepTreeItem item, int depth)
            {
                var collapsed = _interaction.IsCollapsed(item);
                RealStepFact? fact = null;
                if (item.Step is { } node) evidenceByStep.TryGetValue(node.Id, out fact);
                var row = new StepRowProjection(item, depth, collapsed, _stepFind,
                    item.Step?.Id == _newDraftId, _transaction.Current.Mappings, fact);
                StepRows.Add(row);
                if (!collapsed)
                    foreach (var child in item.Children)
                        Append(child, depth + (item.Kind == StepTreeItemKind.Phase ? 0 : 1));
            }
            foreach (var root in StepRoots) Append(root, 0);
            SelectedRow = StepRows.FirstOrDefault(x =>
                StepSelectionKey.Of(x.Item) == _interaction.Selection);
        }
        finally { _syncingRowSelection = wasSyncing; }
        RaisePropertyChanged(nameof(StepCountLabel));
        RaisePropertyChanged(nameof(SearchMatchCount));
    }

    private static string DescribeWrite(StepNode step)
    {
        var target = step.Properties.GetValueOrDefault("target", "");
        var value = step.Properties.GetValueOrDefault("value", "");
        if (value.Contains('='))
        {
            var fields = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return target + " · " + string.Join("; ", fields.Take(2)) +
                (fields.Length > 2 ? " · 另 " + (fields.Length - 2) + " 个信号" : "");
        }
        if (value.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            return target + " · 原始报文 " + value;
        return target + " · " + value;
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
                "Write" => DescribeWrite(step),
                "Read" => step.Properties.GetValueOrDefault("target", "") + " → $" +
                    step.Properties.GetValueOrDefault("variable", ""),
                "WaitUntil" => step.Properties.GetValueOrDefault("target", "") + " " +
                    step.Properties.GetValueOrDefault("operator", "") + " " +
                    step.Properties.GetValueOrDefault("expected", "") + " · 超时 " +
                    step.Properties.GetValueOrDefault("timeoutMs", "") + " ms",
                "WaveStart" => step.Properties.GetValueOrDefault("target", "") + " · " +
                    step.Properties.GetValueOrDefault("captureKey", ""),
                "WaveAnalyze" => step.Properties.GetValueOrDefault("profile", ""),
                "Expect" => step.Properties.GetValueOrDefault("left", "") + " " +
                    step.Properties.GetValueOrDefault("operator", "") + " " +
                    step.Properties.GetValueOrDefault("right", ""),
                _ => ""
            };
            var caption = step.Name + (summary.Length > 0 ? "  ·  " +
                (summary.Length > 100 ? summary[..100] + "…" : summary) : "");
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
        !IsBusy && CanProceed() && (!IsDirty || _dialogs.Confirm("测试程序有未保存修改，确定放弃并关闭吗？"));

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy || HasNewStepDraft) return false;
        if (!IsDirty) return true;
        if (!CanProceed()) return false;
        var previousRevision = AssetRevision;
        await SaveAsync();
        return !IsBusy && !IsDirty && AssetRevision > previousRevision;
    }

    private async Task SaveAsync()
    {
        if (HasNewStepDraft)
        {
            StatusMessage = "仍有未完成的新步骤；先在右侧应用属性，或取消该新增。";
            return;
        }
        if (!IsDirty || IsBusy || !CanProceed()) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.SaveTestEditorAsync(_session, _transaction.Current);
            _transaction = new StepEditTransaction(_session.Document);
            RefreshMappings();
            RefreshTree(_selectedItem?.Step?.Id);
            RaisePropertyChanged(nameof(AssetRevision));
            StatusMessage = "测试程序已保存并重新读取；工程资源修订 " + AssetRevision;
        }
        catch (Exception e) when (IsEditorError(e)) { StatusMessage = "保存失败：" + e.Message; _dialogs.ShowError(StatusMessage); }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync()
    {
        if (IsBusy || !CanProceed()) return;
        if (IsDirty && !_dialogs.Confirm("重新加载会放弃尚未保存的步骤修改，确定继续？")) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.OpenTestEditorAsync(_session.AssetId);
            _transaction = new StepEditTransaction(_session.Document);
            RefreshMappings();
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
        AddStepCommand?.RaiseCanExecuteChanged();
        QuickAddCommand?.RaiseCanExecuteChanged();
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
        PreflightCommand?.RaiseCanExecuteChanged();
        CopyStepCommand?.RaiseCanExecuteChanged();
        PasteStepCommand?.RaiseCanExecuteChanged();
        AddMappingCommand?.RaiseCanExecuteChanged();
        RemoveMappingCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged();
        StopRunCommand?.RaiseCanExecuteChanged();
    }

    private static bool IsEditorError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ProjectDataException or
        InvalidOperationException or InvalidDataException or FormatException or
        ArgumentException or KeyNotFoundException or NotSupportedException or
        JsonException or OverflowException;
}
