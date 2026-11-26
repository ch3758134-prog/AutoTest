using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
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
    bool CaseOnly = false,
    IReadOnlyList<PublicFlowChoice>? PublicFlows = null,
    IReadOnlyList<TestStepFieldChoice>? IfVariables = null);


/// <summary>Operator-only projection; the original runtime evidence remains untouched.</summary>
public sealed record OperatorStepRunRow(string Type, string Status, string Actual, string Detail);

/// <summary>One StepEditTransaction per TEST document; no alternate authoring model.</summary>
public sealed class TestEditorViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly TestEditorDialogs _dialogs;
    private readonly RealTestWorkbenchService _realTest;
    private TestTargetMapping? _selectedMapping;
    private string _approval = "";
    private string _flashApproval = "";
    private string _dutIdentity = "";
    private string _expectedFlashApproval = "";
    private long _preparedStationEpoch;
    private string _preparedDutIdentity = "";
    private StepId? _preparedStepId;
    private TestDocument? _preparedRunDocument;
    private CancellationTokenSource? _singleRunCancellation;
    private string _runSummary = "尚未执行";
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
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private string? _lastProjectedDocument;
    // The view suppresses persistence while a property overlay holds uncommitted inputs.
    public Func<bool>? CanAutoPersist { get; set; }
    public void QueueAutoSave()
    {
        if (HasNewStepDraft || !IsDirty) return;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }
    private string _status = "请选择阶段或步骤进行编辑。";

    public TestEditorViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestEditorSession session,
        RealTestWorkbenchService realTest)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _realTest = realTest;
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            if (IsBusy || HasNewStepDraft || _realTest.IsRunning || CanAutoPersist?.Invoke() == false)
            {
                // Keep pending document edits until the next valid interaction;
                // never persist a provisional TEST step or a half-edited overlay.
                return;
            }
            try
            {
                if (IsDirty) await SaveAsync(silent: true);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // DispatcherTimer uses async void: do not crash WPF on an
                // unexpected filesystem or serialization failure.
                StatusMessage = "自动保存失败，编辑内容仍保留：" + error.Message;
                System.Diagnostics.Trace.TraceWarning("TEST autosave failed: " + error);
            }
        };
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
            if (_catalog.Lookup(original.Type) is null &&
                original.Type is not ("KeySet" or "CanSend" or "CanWait"))
            {
                StatusMessage = "该步骤尚无对应的可编辑操作：" + original.Name;
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
        PrepareRunApprovalCommand = new DelegateCommand(PrepareRealRunApproval,
            () => !IsBusy && !IsDirty && !HasNewStepDraft);
        RunCommand = new DelegateCommand(async () => await RunCurrentCaseInteractiveAsync(),
            () => !IsBusy && !HasNewStepDraft);
        RunSelectedStepCommand = new DelegateCommand(
            async () => await RunSelectedStepInteractiveAsync(),
            () => !IsBusy && !HasNewStepDraft &&
                SelectedItem?.Kind == StepTreeItemKind.Step);
        StopRunCommand = new DelegateCommand(() =>
        {
            // Stop must also cancel USB/DBC/RECORD preparation, before the
            // step runner exists and can accept RequestStop().
            _singleRunCancellation?.Cancel();
            _realTest.RequestStop();
        }, () => IsBusy);
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
            if (!CanProceed())
            {
                RaisePropertyChanged(nameof(StepFind));
                return;
            }
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
            string.Join(" ", node.Properties.Where(x => StepUiCatalog.IsOperatorProperty(x.Key))
                .Select(x => StepUiCatalog.PropertyLabel(x.Key) + " " + x.Value)))
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
    public ObservableCollection<OperatorStepRunRow> RunFactRows { get; } = new();
    private static string OperatorStepType(string type) => type switch
    {
        "Run" => "运行",
        "Cleanup" => "清理",
        "WaveCleanup" => "波形采集收尾",
        "StationCleanup" => "台架操作收尾",
        _ => StepUiCatalog.Label(type)
    };
    private static string OperatorVerdict(RealTestVerdict verdict) => verdict switch
    {
        RealTestVerdict.Passed => "通过",
        RealTestVerdict.Failed => "失败",
        RealTestVerdict.Inconclusive => "无法判定",
        RealTestVerdict.Error => "执行错误",
        RealTestVerdict.Aborted => "已中止",
        _ => "状态未知"
    };
    private static string OperatorExecutionDetail(string detail)
    {
        if (detail.StartsWith("Branch source unavailable: ", StringComparison.Ordinal))
            return "分支判断所需的数据不可用：" + detail["Branch source unavailable: ".Length..];
        if (detail == "Branch: then") return "条件成立，进入成立分支";
        if (detail == "Branch: else") return "条件不成立，进入否则分支";
        if (detail == "Condition observed") return "已观察到满足条件的信号";
        if (detail == "Condition not observed before timeout") return "等待超时，条件未满足";
        if (detail == "No valid CAN signal") return "未收到有效的 CAN 信号";
        if (detail == "Operand unavailable") return "计算所需的数据不可用";
        if (detail == "Missing or invalid observation") return "缺少有效的测量数据";
        if (detail == "Expectation met") return "实际结果满足期望";
        if (detail == "Expectation failed") return "实际结果不满足期望";
        if (detail == "Capture started") return "已开始采集波形";
        if (detail == "Operator stopped run") return "已手动停止测试";
        // Retain unknown device diagnostics intact instead of mistranslating
        // error codes, CAN identifiers, UDS replies or evidence references.
        return detail;
    }
    private static string OperatorExecutionStatus(RealStepStatus status) => status switch
    {
        RealStepStatus.Completed => "已执行",
        RealStepStatus.Skipped => "已跳过",
        RealStepStatus.ConditionFalse => "条件不成立",
        RealStepStatus.NoData => "未收到数据",
        RealStepStatus.Timeout => "超时",
        RealStepStatus.InvalidData => "数据无效",
        RealStepStatus.DeviceError => "设备错误",
        RealStepStatus.Cancelled => "已取消",
        _ => "结果待核查"
    };

    private static string OperatorExecutionStatus(RealStepFact fact) =>
        fact.Type != "WaveCompare" ? OperatorExecutionStatus(fact.Status) :
        fact.Status switch
        {
            RealStepStatus.Completed => "波形通过",
            RealStepStatus.ConditionFalse => "波形失败",
            RealStepStatus.NoData or RealStepStatus.InvalidData => "波形无法判定",
            _ => OperatorExecutionStatus(fact.Status)
        };

    public TestTargetMapping? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (SetProperty(ref _selectedMapping, value))
                RemoveMappingCommand?.RaiseCanExecuteChanged();
        }
    }
    /// <summary>Volatile operator consent; never part of the persisted TestDocument.</summary>
    /// <summary>Actual operator/scanner value, not generated by the TEST editor.</summary>
    public string DutIdentity
    {
        get => _dutIdentity;
        set
        {
            if (SetProperty(ref _dutIdentity, value))
                RunCommand?.RaiseCanExecuteChanged();
        }
    }
    public string FlashApproval
    {
        get => _flashApproval;
        set => SetProperty(ref _flashApproval, value);
    }
    public string ExpectedFlashApproval
    {
        get => _expectedFlashApproval;
        private set => SetProperty(ref _expectedFlashApproval, value);
    }
    public string RunApproval
    {
        get => _approval;
        set
        {
            if (SetProperty(ref _approval, value))
                RunCommand?.RaiseCanExecuteChanged();
        }
    }
    public string ExpectedApproval
    {
        get
        {
            try
            {
                return RealTestWorkbenchService.ExpectedApproval(
                    _realTest.Prepare(_preparedRunDocument ?? _transaction.Current));
            }
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
    public event Action<string>? InspectorLoading;
    public Func<bool>? AllowEditorAction { get; set; }
    private bool _applyingInspector;
    public bool IsApplyingInspector => _applyingInspector;
    private bool CanProceed() => _applyingInspector || (AllowEditorAction?.Invoke() ?? true);

    private StepInsertionTarget? _pendingInsertion;
    private bool _inspectorAdding;
    private StepId? _newDraftId;
    private string? _newDraftActionKey;
    // If the user cancels an insertion by Undo, Ctrl+Y must restore the
    // provisional state rather than silently reintroducing a saveable step.
    private StepId? _cancelledDraftId;
    private string? _cancelledDraftActionKey;
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
            StatusMessage = "选择要添加的步骤类型。";
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
            // The shared Write descriptor historically defaults to Key "Pulse".
            // A new CAN send must begin with an unconfigured payload instead of
            // accidentally interpreting that KEY action as CAN bytes.
            if (chosen.Key == "can") node = node.WithProperty("value", "HEX:0000000000000000");
            place.ValidateNode(node);
            _transaction.Insert(place.Phase, place.ParentId, place.Slot, place.Index, node);
            _cancelledDraftId = null;
            _cancelledDraftActionKey = null;
            _newDraftId = node.Id;
            _newDraftActionKey = chosen.Key;
            _inspectorAdding = false;
            _pendingInsertion = null;
            RefreshTree(node.Id);
            StatusMessage = "步骤已添加，请编辑属性。";
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
        _interaction.NextInspectorRequest(); // invalidate any late catalog load
        if (_newDraftId is { } provisional)
        {
            try
            {
                // This provisional insertion is the latest operation: the
                // editor disallows unrelated mutations until it is applied.
                // Undo restores the prior document and history without an
                // extra compensating Delete operation in the undo stack.
                if (_transaction.Current.Walk().Any(x => x.Id == provisional))
                {
                    if (!_transaction.CanUndo)
                        throw new InvalidOperationException("缺少可撤销的新步骤插入事务。");
                    _transaction.Undo();
                }
                _cancelledDraftId = provisional;
                _cancelledDraftActionKey = _newDraftActionKey;
                _newDraftId = null;
                _newDraftActionKey = null;
                RefreshTree(null);
                StatusMessage = "已取消新增步骤。";
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
        _interaction.NextInspectorRequest();
        if (_selectedItem?.Step is { } draft && draft.Id == _newDraftId)
            _ = PublishInspectorAsync(draft, _newDraftActionKey);
    }

    private async Task PublishInspectorAsync(StepNode? original, string? initialActionKey)
    {
        var request = _interaction.NextInspectorRequest();
        if (original is null)
        {
            // Phase/slot navigation must be immediate and independent of DBC I/O.
            InspectorRequested?.Invoke(new StepInspectorContext(null, null,
                Array.Empty<MappingChoice>(), _transaction.Current.Mappings.ToArray(), false));
            return;
        }
        // A freshly inserted Write node does not have its mapping yet. The
        // chosen UI action is already known and must be considered BEFORE
        // deciding whether any heavy DBC directory load is necessary.
        if (original.Id == _newDraftId && initialActionKey is null)
            initialActionKey = _newDraftActionKey;
        // The inspector must remain responsive when a user selects IGN, KEY,
        // Wait, a control-flow node or a public-flow call. Resolving all DBC
        // messages and signals for every click makes ordinary authoring look hung.
        // Resource-heavy catalog loading is restricted to steps that actually
        // select DBC signals, CAN messages or wave analysis profiles.
        var mappedTargetName = original.Properties.GetValueOrDefault("target", "");
        var mappedTarget = _transaction.Current.Mappings.FirstOrDefault(x =>
            string.Equals(x.Name, mappedTargetName, StringComparison.OrdinalIgnoreCase));
        var requiresCatalog = initialActionKey is "can" or "read" or "wait-signal" or
            "wave-start" or "wave-analyze" or "can-wait" or "can-send" or "wave-capture" or
            "wave-compare" ||
            original.Type is "Read" or "WaitUntil" or "AssertSignal" or "CanSend" or
                "CanWait" or "WaveStart" or "WaveAnalyze" or "WaveCapture" or "WaveCompare" ||
            (original.Type == "Write" && initialActionKey is not ("ign" or "key") &&
             mappedTarget?.Kind is not (TestTargetKind.Ignition or TestTargetKind.RawKey or
                 TestTargetKind.Button));
        IReadOnlyList<MappingChoice> choices;
        if (requiresCatalog)
        {
            if (!_applyingInspector) InspectorLoading?.Invoke(original.Name);
            choices = await AvailableObjectsAsync();
        }
        else choices = Array.Empty<MappingChoice>();
        if (!_interaction.IsCurrentInspectorRequest(request)) return;
        var insertion = _pendingInsertion;
        if (insertion is null && _inspectorAdding && _selectedItem is { } selected &&
            StepInsertionTarget.CanAppend(selected))
            insertion = StepInsertionTarget.Resolve(selected, StepInsertionMode.Append);
        if (original?.Id == _newDraftId && initialActionKey is null)
            initialActionKey = _newDraftActionKey;
        var flowChoices = _workspace.Current?.Definition.Assets
            .Where(asset => asset.Kind == ProjectWorkspaceService.PublicFlowKind)
            .Select(asset => new PublicFlowChoice(asset.Id.Value,
                asset.Revision, asset.Sha256, asset.Name))
            .ToArray() ?? [];
        // Resolve variables from the actual execution path before this node,
        // not from arbitrary DBC resources or later steps in the document.
        var ifVariables = original?.Type is ("If" or "Switch")
            ? TestVariableFlowValidator.Before(_transaction.Current, original.Id)
                .Select(x => new TestStepFieldChoice(x.Name,
                    x.Name + "（来自：" + x.Source + "）")).ToArray()
            : Array.Empty<TestStepFieldChoice>();
        InspectorRequested?.Invoke(new StepInspectorContext(original, initialActionKey,
            choices, _transaction.Current.Mappings.ToArray(), insertion?.RequiresCase == true,
            flowChoices, ifVariables));
    }

    public bool ApplyInspector(StepCompositionResult result)
    {
        if (IsBusy || _selectedItem is null || _applyingInspector) return false;
        _applyingInspector = true;
        try
        {
            if (!_inspectorAdding && _selectedItem.Step is { } original)
            {
                var existing = result.Mapping is null ? null :
                    _transaction.Current.Mappings.FirstOrDefault(x =>
                        string.Equals(x.Name, result.Mapping.Name,
                            StringComparison.OrdinalIgnoreCase));
                var approvedRebind = existing is not null && existing != result.Mapping;
                if (approvedRebind)
                {
                    if (!StepEditTransaction.CanRebindSamePhysicalIdentity(
                            existing!, result.Mapping!))
                    {
                        StatusMessage = "当前 DBC 操作对象与旧绑定不是同一硬件身份。" +
                            "本次未改动旧映射；请新建独立绑定，不能自动兼容不同 CAN ID 或资源。";
                        return false;
                    }
                    var affected = _transaction.Current.Walk().Count(step =>
                        string.Equals(step.Properties.GetValueOrDefault("target",
                            step.Properties.GetValueOrDefault("profile", "")),
                            result.Mapping!.Name, StringComparison.OrdinalIgnoreCase));
                    var oldHash = existing!.AssetSha256;
                    var newHash = result.Mapping!.AssetSha256;
                    if (!_dialogs.Confirm(
                            "此 CAN/DBC 操作对象仍使用旧修订，不能直接保存以免静默更换生产资源。" +
                            "\n\n操作对象：" + result.Mapping.Name +
                            "\n原固定版本：r" + existing.AssetRevision + " / " +
                            oldHash[..Math.Min(oldHash.Length, 12)] +
                            "\n新选中版本：r" + result.Mapping.AssetRevision + " / " +
                            newHash[..Math.Min(newHash.Length, 12)] +
                            "\n受影响步骤：" + affected +
                            "\n\n是否明确将这个操作对象的所有引用更新到新修订？" +
                            "\n这是一笔可撤销的工程编辑事务；保存工程后仍须进行全量预检。"))
                    {
                        StatusMessage = "已取消重新绑定，旧修订与所有步骤均保持不变。";
                        return false;
                    }
                    _transaction.ReplaceWithExplicitRebind(
                        original.Id, result.Step, result.Mapping);
                }
                else
                    _transaction.ReplaceWithMapping(original.Id, result.Step, result.Mapping);
                if (_newDraftId == original.Id)
                {
                    _newDraftId = null;
                    _newDraftActionKey = null;
                    _cancelledDraftId = null;
                    _cancelledDraftActionKey = null;
                }
                RefreshMappings();
                RefreshTree(original.Id);
                StatusMessage = approvedRebind
                    ? "已明确更新同名 DBC 操作对象的固定修订；请核对全部受影响步骤并执行运行前检查。"
                    : "步骤属性已更新。";
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
                StatusMessage = "已添加步骤。";
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
        StatusMessage = "已复制步骤，选择位置后粘贴。";
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
            StatusMessage = "已粘贴步骤。";
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "无法粘贴到该分支：" + error.Message;
        }
    }
    public DelegateCommand AddMappingCommand { get; }
    public DelegateCommand RemoveMappingCommand { get; }
    public DelegateCommand PrepareRunApprovalCommand { get; }
    public DelegateCommand RunCommand { get; }
    public DelegateCommand RunSelectedStepCommand { get; }
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
        // Selection is observation only. Explicit edit or a new provisional
        // step requests the property editor; ordinary navigation never loads DBC.
        _interaction.NextInspectorRequest();
        if (item?.Step is { } draft && draft.Id == _newDraftId)
            _ = PublishInspectorAsync(draft, _newDraftActionKey);
    }

    private void UpdateInspector()
    {
        InspectorProperties.Clear();
        if (_selectedItem?.Step is { } step)
        {
            foreach (var property in step.Properties
                .Where(x => StepUiCatalog.IsOperatorProperty(x.Key))
                .OrderBy(x => x.Key, StringComparer.Ordinal))
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
            StatusMessage = "已复制步骤。";
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
        if (IsBusy || !CanProceed()) return;
        if (HasNewStepDraft)
        {
            CancelInspectorEdit();
            return;
        }
        if (!_transaction.CanUndo || !CanProceed()) return;
        _transaction.Undo();
        _cancelledDraftId = null;
        _cancelledDraftActionKey = null;
        RefreshTree(_selectedItem?.Step?.Id);
        StatusMessage = "已撤销";
    }
    private void Redo()
    {
        if (!_transaction.CanRedo || IsBusy || !CanProceed()) return;
        _transaction.Redo();
        if (_cancelledDraftId is { } cancelled &&
            _transaction.Current.Walk().Any(x => x.Id == cancelled))
        {
            _newDraftId = cancelled;
            _newDraftActionKey = _cancelledDraftActionKey;
            _cancelledDraftId = null;
            _cancelledDraftActionKey = null;
            RefreshTree(cancelled);
            StatusMessage = "已恢复未完成的新步骤；请先应用浮窗属性。";
            return;
        }
        _cancelledDraftId = null;
        _cancelledDraftActionKey = null;
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
        StatusMessage = "请选择要操作的工程资源。";
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
            StatusMessage = "请先完成步骤编辑。";
            return;
        }
        if (IsBusy) return;
        try
        {
            var plan = _realTest.Prepare(_transaction.Current);
            if (!plan.Diagnostics.IsEmpty)
            {
                StatusMessage = "运行前检查未通过：" + plan.Diagnostics.Length +
                    " 项问题；首项：" + plan.Diagnostics[0];
                return;
            }
            StatusMessage = "运行前检查通过；可以直接运行当前用例，系统会先保存有效修改并冻结运行版本。";
        }
        catch (Exception error)
        {
            StatusMessage = "运行前检查未通过：" + error.Message;
        }
    }

    private void PrepareRealRunApproval() => PrepareRunApproval(null);

    private void PrepareRunApproval(StepId? singleStep)
    {
        if (IsBusy || IsDirty || HasNewStepDraft || !CanProceed()) return;
        _preparedStationEpoch = 0;
        _preparedDutIdentity = "";
        _preparedStepId = null;
        _preparedRunDocument = null;
        RunApproval = "";
        ExpectedFlashApproval = "";
        FlashApproval = "";
        if (!_realTest.IsHardwareConnected || _realTest.ConnectedStationEpoch <= 0)
        {
            StatusMessage = "主控未连接。";
            RefreshCommands();
            return;
        }
        if (DutIdentity.Length > 0 &&
            (string.IsNullOrWhiteSpace(DutIdentity) ||
             DutIdentity != DutIdentity.Trim() || DutIdentity.Length > 256 ||
             DutIdentity.Any(char.IsControl)))
        {
            StatusMessage = "ECU 编号可以留空进行开发诊断；如填写，必须是真实且格式合法的编号。";
            RefreshCommands();
            return;
        }
        try
        {
            var approvedEpoch = _realTest.ConnectedStationEpoch;
            var runDocument = singleStep is { } id
                ? TestRunStepScope.ForSingleStep(_transaction.Current, id)
                : _transaction.Current;
            var plan = _realTest.Prepare(runDocument);
            if (!plan.Diagnostics.IsEmpty)
            {
                StatusMessage = "无法准备真实运行：" + plan.Diagnostics[0];
                return;
            }
            ExpectedFlashApproval = _realTest.ExpectedFlashApproval(runDocument);
            FlashApproval = "";
            if (!_realTest.IsHardwareConnected ||
                _realTest.ConnectedStationEpoch != approvedEpoch)
                throw new IOException("准备运行期间 USB 主控会话变化，请重新确认。");
            _preparedStationEpoch = approvedEpoch;
            _preparedDutIdentity = DutIdentity;
            _preparedStepId = singleStep;
            _preparedRunDocument = runDocument;
            RunApproval = RealTestWorkbenchService.ExpectedApproval(plan);
            RefreshCommands();
            StatusMessage = ExpectedFlashApproval.Length == 0 ?
                (DutIdentity.Length == 0
                    ? "未填写 ECU 编号：可执行真实开发诊断，记录标记为未绑定 DUT，不能计入出厂。"
                    : "当前用例已确认，可以运行真实步骤；编号尚未经扫码规则验证。") :
                "烧录资源已冻结。必须在下方手动输入完整 FLASH 授权文字；授权不写入工程。";
            RunSummary = singleStep is null
                ? "已确认当前测试用例 · 尚未执行"
                : "已确认单步骤诊断 · 尚未执行";
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "准备真实运行失败：" + error.Message;
        }
    }

    /// <summary>One visible run action: do the same version-pinned preflight/authorization
    /// as the former separate confirmation button before entering the existing executor.
    /// A FLASH step still needs its explicit physical FLASH consent.</summary>
    private async Task RunCurrentCaseInteractiveAsync()
    {
        if (IsBusy || HasNewStepDraft || !CanProceed()) return;
        if (!await SaveForCloseAsync()) return;
        await SynchronizeSavedRevisionAsync();
        if (IsBusy || IsDirty || _workspace.Current?.Definition.Assets.SingleOrDefault(a => a.Id == _session.AssetId)?.Revision != _session.AssetRevision) return;
        bool needsPreparation = _preparedRunDocument is null || _preparedStepId is not null ||
            _preparedStationEpoch != _realTest.ConnectedStationEpoch ||
            _preparedDutIdentity != DutIdentity;
        if (!needsPreparation)
        {
            try
            {
                needsPreparation = _realTest.Prepare(_transaction.Current).Fingerprint !=
                    _realTest.Prepare(_preparedRunDocument!).Fingerprint;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                StatusMessage = "当前测试配置未通过运行前检查：" + error.Message;
                return;
            }
        }
        if (needsPreparation) PrepareRunApproval(null);
        if (_preparedRunDocument is null || _preparedStepId is not null) return;
        await RunRealAsync();
    }

    private async Task RunSelectedStepInteractiveAsync()
    {
        if (IsBusy || HasNewStepDraft || !CanProceed()) return;
        var selected = SelectedItem?.Step?.Id;
        if (!await SaveForCloseAsync()) return;
        await SynchronizeSavedRevisionAsync();
        if (IsBusy || IsDirty || _workspace.Current?.Definition.Assets.SingleOrDefault(a => a.Id == _session.AssetId)?.Revision != _session.AssetRevision) return;
        if (selected is not null && SelectedItem?.Step?.Id != selected) { StatusMessage = "用例已更新，请重新选择运行步骤。"; return; }
        if (SelectedItem?.Step is not { } step) return;
        if (!_realTest.IsHardwareConnected || _realTest.ConnectedStationEpoch <= 0)
        {
            StatusMessage = "主控未连接，无法运行步骤。";
            return;
        }
        var periodicOnly = step.Type == "Write" &&
            int.TryParse(step.Properties.GetValueOrDefault("periodMs", "0"),
                out var cycleMs) && cycleMs > 0;
        if (!_dialogs.Confirm("将对真实 ECU 执行选中步骤及其子步骤：\n" +
                step.Name + "（" + step.Type + "）\n\n" +
                "只运行此步骤，不运行其余准备、测试和脚本清理步骤；" +
                "结束后仍执行硬件安全清理。依赖前置变量或 ECU 状态的步骤可能失败。" +
                (periodicOnly
                    ? "\n\n注意：此步骤是后台周期 CAN。单步结束即清理周期任务，" +
                      "可能只观察到首帧。要持续观察周期，请运行包含等待、判断或停止周期步骤的完整用例。"
                    : "") + "\n\n继续？"))
            return;
        if (_preparedRunDocument is null || _preparedStepId != step.Id ||
            _preparedStationEpoch != _realTest.ConnectedStationEpoch ||
            _preparedDutIdentity != DutIdentity)
            PrepareRunApproval(step.Id);
        if (_preparedRunDocument is null || _preparedStepId != step.Id) return;
        if (ExpectedFlashApproval.Length > 0 &&
            !string.Equals(FlashApproval, ExpectedFlashApproval, StringComparison.Ordinal))
        {
            StatusMessage = "单步 FLASH 需要手动填写下方完整实体烧录授权，然后再次点击运行此步骤。";
            return;
        }
        await RunRealAsync(singleStep: true);
    }

    private async Task RunRealAsync(bool singleStep = false)
    {
        if (IsBusy || IsDirty || HasNewStepDraft || !CanProceed()) return;
        if (_preparedRunDocument is null ||
            (singleStep ? _preparedStepId != SelectedItem?.Step?.Id
                : _preparedStepId is not null))
        {
            StatusMessage = singleStep
                ? "请重新选中并确认要运行的步骤。"
                : "请先确认当前完整测试用例。";
            return;
        }
        TestDocument candidate;
        try
        {
            candidate = singleStep && _preparedStepId is { } id
                ? TestRunStepScope.ForSingleStep(_transaction.Current, id)
                : _transaction.Current;
            if (_realTest.Prepare(candidate).Fingerprint !=
                _realTest.Prepare(_preparedRunDocument).Fingerprint)
                throw new InvalidOperationException("运行快照已变化。");
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "用例已更改，请重新确认：" + error.Message;
            RunApproval = "";
            return;
        }
        if (!_realTest.IsHardwareConnected || _preparedStationEpoch <= 0 ||
            _realTest.ConnectedStationEpoch != _preparedStationEpoch ||
            DutIdentity != _preparedDutIdentity)
        {
            RunApproval = "";
            _preparedStationEpoch = 0;
            _preparedDutIdentity = "";
            StatusMessage = "主控 USB 会话或 DUT 身份已改变；请重新连接/核对并确认当前用例。";
            RefreshCommands();
            return;
        }
        if (string.IsNullOrWhiteSpace(RunApproval) ||
            !string.Equals(RunApproval, ExpectedApproval, StringComparison.Ordinal))
        {
            StatusMessage = "请先确认当前用例。";
            return;
        }
        if (DutIdentity.Length > 0 &&
            (string.IsNullOrWhiteSpace(DutIdentity) ||
             DutIdentity != DutIdentity.Trim() || DutIdentity.Length > 256 ||
             DutIdentity.Any(char.IsControl)))
        {
            StatusMessage = "ECU 编号格式不正确；开发调试可留空。";
            return;
        }
        if (ExpectedFlashApproval.Length > 0 &&
            !string.Equals(FlashApproval, ExpectedFlashApproval, StringComparison.Ordinal))
        {
            StatusMessage = "FLASH 授权不匹配：必须手动输入完整 AUTHORIZE ECU FLASH 指纹。";
            return;
        }
        _singleRunCancellation = new CancellationTokenSource();
        IsBusy = true;
        RunFacts.Clear();
        RunFactRows.Clear();
        RefreshRows(); // Remove previous-run statuses before starting real execution.
        RunSummary = singleStep
            ? "正在执行选中步骤（独立诊断）…"
            : "正在执行真实测试用例…";
        StopRunCommand.RaiseCanExecuteChanged();
        void OnPhysicalStep(RealStepFact fact)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            void ShowStep()
            {
                RunFacts.Add(fact);
                RunFactRows.Add(new OperatorStepRunRow(
                    OperatorStepType(fact.Type), OperatorExecutionStatus(fact),
                    fact.Actual, OperatorExecutionDetail(fact.Detail)));
                RunSummary = "真实运行中 · 已归档 " + RunFacts.Count + " 条步骤记录";
                RefreshRows();
            }
            if (dispatcher.CheckAccess()) ShowStep();
            else dispatcher.Invoke(ShowStep);
        }
        _realTest.StepReported += OnPhysicalStep;
        try
        {
            var result = await _realTest.ExecuteAsync(candidate, RunApproval,
                FlashApproval, DutIdentity, _singleRunCancellation!.Token,
                isolatedStep: singleStep);
            RunFacts.Clear();
            RunFactRows.Clear();
            foreach (var fact in result.Facts)
            {
                RunFacts.Add(fact);
                RunFactRows.Add(new OperatorStepRunRow(
                    OperatorStepType(fact.Type),
                    OperatorExecutionStatus(fact),
                    fact.Actual, OperatorExecutionDetail(fact.Detail)));
            }
            RefreshRows(); // Apply only actual backend StepId evidence.
            RunSummary = (singleStep ? "独立步骤诊断记录" :
                DutIdentity.Length == 0 ? "未绑定 ECU 诊断记录" : "正式记录（开发模式）") +
                " · " + OperatorVerdict(result.Verdict) + " · " + result.Facts.Length +
                " 条步骤记录" + (result.EvidencePath is null ? "" :
                " · HTML 报告：" + result.EvidencePath);
            // A case opened from its editor is NOT the shared project scope.
            // Never let a successful sender-start step imply that it will
            // survive into a separately executed next case.
            if (TestRunStepScope.EnabledSteps(candidate).Any(node =>
                    node.Type == "Write" &&
                    int.TryParse(node.Properties.GetValueOrDefault("periodMs", "0"),
                        out var periodMs) && periodMs > 0))
                RunSummary += " · 独立用例/步骤已结束，周期 CAN 随作用域停止。" +
                    "跨用例保持须运行测试项目（含项目准备），不能逐个点击运行此用例。";
            StatusMessage = RunSummary;
        }
        catch (OperationCanceledException)
        {
            RunSummary = "操作员已请求停止：真实执行已取消，设备安全清理另行执行。";
            StatusMessage = RunSummary;
        }
        catch (Exception e)
        {
            RunSummary = "真实测试未完成：" + e.Message;
            StatusMessage = RunSummary;
            // Keep real-run failures in the visible status/result area, not a modal
            // dialog that blocks the operator from examining the test evidence.
        }
        finally
        {
            RunSummary += " · " + _realTest.KeyRequestStatus;
            StatusMessage = RunSummary;
            _realTest.StepReported -= OnPhysicalStep;
            _singleRunCancellation?.Dispose();
            _singleRunCancellation = null;
            _preparedStationEpoch = 0;
            _preparedDutIdentity = "";
            _preparedStepId = null;
            _preparedRunDocument = null;
            RunApproval = "";
            FlashApproval = "";
            IsBusy = false;
        }
    }

    private void RefreshTree(StepId? preferred)
    {
        var projectedDocument = TestDocumentCodec.Serialize(_transaction.Current);
        var documentChanged = _lastProjectedDocument is not null &&
            !string.Equals(_lastProjectedDocument, projectedDocument, StringComparison.Ordinal);
        _lastProjectedDocument = projectedDocument;
        // Any changed TEST revision makes previous-run StepId evidence stale.
        // Clear it before rebuilding; never display old PASS/error facts against
        // a newly edited document. Historical evidence remains in its own audit.
        if (documentChanged)
        {
            _preparedStationEpoch = 0;
            _preparedDutIdentity = "";
            _preparedStepId = null;
            _preparedRunDocument = null;
            RunApproval = "";
            FlashApproval = "";
            ExpectedFlashApproval = "";
        }
        if (documentChanged && RunFacts.Count > 0)
        {
            RunFacts.Clear();
            RunFactRows.Clear();
            RunSummary = "测试程序已更改；此前执行事实不再对应当前编辑版本。";
        }
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
        QueueAutoSave();
    }

    public void CommitQuickValue(StepRowProjection row) => TryCommitQuickValue(row);

    /// <summary>Both inline and Inspector edits update the one formal TEST transaction.</summary>
    public bool TryCommitQuickValue(StepRowProjection row)
    {
        if (IsBusy || !CanProceed() || row.Item.Step is not { } step ||
            !row.IsQuickEditable) return false;
        var key = step.Type == "Wait" ? "milliseconds" : "count";
        var upper = step.Type == "Wait" ? 60000 : 1000;
        if (!int.TryParse(row.QuickValue, out var value) || value < 0 || value > upper)
        {
            StatusMessage = "行内参数无效：请输入 0～" + upper + " 的整数；原输入仍保留，可修正或 Esc 取消。";
            return false;
        }
        if (row.OriginalQuickValue == value.ToString()) return true;
        try
        {
            _transaction.SetProperty(step.Id, key, value.ToString());
            RefreshTree(step.Id);
            StatusMessage = "步骤参数已更新。";
            return true;
        }
        catch (Exception error) when (IsEditorError(error))
        {
            StatusMessage = "行内编辑未应用：" + error.Message;
            return false;
        }
    }

    /// <summary>Move canonical StepNode, preserving StepId, Slots and transaction undo.</summary>
    public void MoveStepAfter(StepRowProjection source, StepRowProjection target)
    {
        if (IsBusy || !CanProceed() || source.Item.Step is not { } moving ||
            target.Item.Step is not { } destination || moving.Id == destination.Id) return;
        var coords = target.Item.CoordinatesForSibling();
        Mutate(() => _transaction.Move(moving.Id, target.Item.Phase, coords.ParentId,
            coords.Slot, coords.Index), moving.Id,
            "步骤已移动；修改将自动保存，可通过 Ctrl+Z 撤销。");
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
        Mutate(() => _transaction.Rename(step.Id, name.Trim()), step.Id, "步骤已重命名；修改将自动保存。");
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
                    item.Step?.Id == _newDraftId, _transaction.Current.Mappings, fact,
                    item.Step?.Id == _newDraftId ? _newDraftActionKey : null);
                StepRows.Add(row);
                if (!collapsed)
                    foreach (var child in item.Children)
                        Append(child, depth + (item.Kind == StepTreeItemKind.Phase ? 0 : 1));
            }
            // Normal TEST authoring is one continuous ordered step list.
            // The persisted format still includes three phases for existing
            // files and the real executor, but new documents do not force
            // users to navigate three empty Prepare/Test/Cleanup headers.
            // Show nonempty legacy Prepare/Cleanup sections for migration;
            // never silently move their physical or recovery actions.
            foreach (var root in StepRoots)
            {
                if (root.Phase == TestPhase.Test)
                {
                    foreach (var child in root.Children) Append(child, 0);
                }
                else if (root.Children.Count > 0)
                    Append(root, 0);
            }
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
                "If" => step.Properties.GetValueOrDefault("variable", "") + " " +
                    step.Properties.GetValueOrDefault("operator", "==") + " " +
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
            // Control-flow has semantic order, not alphabetical order (ELSE first).
            foreach (var slot in step.Slots
                .OrderBy(x => step.Type == "If" ? x.Key switch
                {
                    "then" => 0, "else" => 1, _ => 2
                } : 0)
                .ThenBy(x => x.Key, StringComparer.Ordinal))
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
        if (IsBusy) return false;
        if (HasNewStepDraft)
        {
            StatusMessage = "请完成新步骤的属性编辑或取消新增。";
            return false;
        }
        if (!IsDirty) return true;
        if (!CanProceed()) return false;
        var previousRevision = AssetRevision;
        await SaveAsync();
        return !IsBusy && !IsDirty && AssetRevision > previousRevision;
    }

    private async Task SaveAsync(bool silent = false)
    {
        if (HasNewStepDraft)
        {
            StatusMessage = "仍有未完成的步骤，请应用属性或取消。";
            return;
        }
        if (!IsDirty || IsBusy || !CanProceed()) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.SaveTestEditorAsync(_session, _transaction.Current);
            // Saving immutable TEST content must not rebuild rows or steal input focus.
            // Preserve undo/redo and the current selection; only disk revision/state changes.
            RaisePropertyChanged(nameof(AssetRevision));
            RefreshCommands();
            StatusMessage = "测试程序已保存 · 版本 " + AssetRevision;
        }
        catch (Exception e) when (IsEditorError(e))
        {
            StatusMessage = "保存失败，修改仍在编辑器中：" + e.Message;
            if (!silent) _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }

    public async Task SynchronizeSavedRevisionAsync()
    {
        if (IsBusy || !CanProceed() || HasNewStepDraft) return;
        var context = _workspace.Current;
        var asset = context?.Definition.Assets.FirstOrDefault(a => a.Id == _session.AssetId);
        if (asset is null || context!.Definition.Id != _session.ProjectId ||
            asset.Revision == _session.AssetRevision) return;
        if (IsDirty)
        {
            StatusMessage = "文件已被其他操作修改；请核对后重新加载。";
            return;
        }
        await ReloadAsync();
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
            _preparedRunDocument = null;
            _preparedStepId = null;
            _preparedStationEpoch = 0;
            RunApproval = "";
            FlashApproval = "";
            ExpectedFlashApproval = "";
            _newDraftId = null;
            _newDraftActionKey = null;
            _cancelledDraftId = null;
            _cancelledDraftActionKey = null;
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
        PrepareRunApprovalCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged();
        RunSelectedStepCommand?.RaiseCanExecuteChanged();
        StopRunCommand?.RaiseCanExecuteChanged();
    }

    private static bool IsEditorError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ProjectDataException or
        InvalidOperationException or InvalidDataException or FormatException or
        ArgumentException or KeyNotFoundException or NotSupportedException or
        JsonException or OverflowException;
}
