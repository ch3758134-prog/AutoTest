using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Devices.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoTest.Testing.Lab;
using AutoTest.Application.V1;

namespace AutoTest.Desktop.Tools.Testing;

public sealed class LabTreeRow
{
    public required string Caption { get; init; }
    public required LabPhase Phase { get; init; }
    // View-only projection of the persisted TEST node, not a second program model.
    public string OrderLabel { get; init; } = "";
    public string StepName { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public bool IsPhase { get; init; }
    public bool IsSlot { get; init; }
    public string Summary { get; init; } = "";
    public Guid? StepId { get; init; }
    public Guid? ParentId { get; init; }
    public string? Slot { get; init; }
    public int Index { get; init; }
    public ObservableCollection<LabTreeRow> Children { get; } = new();
}

public sealed record R1ControllerStateChoice(
    Guid ControllerId, Guid StateId, string ControllerName, string StateName,
    int Revision, uint Mask)
{
    public string Label => ControllerName + " / " + StateName +
        " · 0x" + Mask.ToString("X5") + " · r" + Revision;
}

public sealed class LabTestCaseChoice : INotifyPropertyChanged
{
    private bool _selected = true;
    public Guid Id { get; }
    public string Name { get; }
    public int StepCount { get; }
    public bool IsLegacy { get; }
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public LabTestCaseChoice(LabTestCaseDefinition testCase)
    {
        Id = testCase.Id;
        Name = testCase.Name;
        StepCount = testCase.StepCount;
        IsLegacy = testCase.IsLegacy;
    }
}

internal sealed class LabCommand(Action run, Func<bool>? enabled = null) : ICommand
{
    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;
    public void Execute(object? parameter) => run();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Self-contained TEST workspace. INTEGRATION will own Shell and authoritative document adapter.</summary>
public sealed class LabTestWorkspaceViewModel : INotifyPropertyChanged
{
    private LabDocumentEditor _editor;
    private V1Workspace? _project;
    private readonly LabRunCoordinator _runner;
    private LabPlanCompiler _compiler;
    private readonly SynchronizationContext? _ui;
    private LabTreeRow? _selected;
    private string _selectedStepTypeLabel = "选择左侧步骤查看设置";
    public string SelectedStepTypeLabel { get => _selectedStepTypeLabel; private set { _selectedStepTypeLabel = value; Changed(); } }
    private string _name = "";
    private string _parameterName = "variable", _parameterValue = "";
    private string _insertType = "Observe";
    private string _status = "工程 TEST 待配置；仅按已批准的真实设备能力执行";
    private string _verdict = "NOT_RUN";
    private bool _busy;
    // Owns the single TEST run during preflight and operator approval as well as execution.
    private readonly SemaphoreSlim _physicalRunGate = new(1, 1);
    private long _runGeneration;
    private Func<HardwareBenchController?>? _physicalController;
    private Func<bool>? _physicalApproval;
    private Func<EcuControlProfile?>? _keySimControllerProfile;
    private Func<string?>? _keySimControllerProfilePath;
    private Func<string,bool>? _approveKeySimProgram;
    private R1ControllerStateChoice? _selectedControllerChoice;
    private string _physicalEvidencePath = "未执行";
    public string PhysicalEvidencePath { get => _physicalEvidencePath; private set { _physicalEvidencePath = value; Changed(); } }
    public ICommand RunPhysicalReadOnlyCommand { get; private set; } = null!;
    public void AttachPhysicalReadOnly(Func<HardwareBenchController?> controller, Func<bool> approve)
    {
        _physicalController = controller ?? throw new ArgumentNullException(nameof(controller));
        _physicalApproval = approve ?? throw new ArgumentNullException(nameof(approve));
        RefreshCommands();
    }

    /// <summary>
    /// Bind the P05 actual Controller/Profile to the ONE formal TEST compiler.
    /// The profile must be saved to disk before enabling a physical key action.
    /// </summary>
    public void AttachPhysicalController(
        Func<EcuControlProfile?> profile, Func<string?> savedProfilePath,
        Func<string,bool> approveProgram)
    {
        _keySimControllerProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        _keySimControllerProfilePath = savedProfilePath ??
            throw new ArgumentNullException(nameof(savedProfilePath));
        _approveKeySimProgram = approveProgram ??
            throw new ArgumentNullException(nameof(approveProgram));
        _compiler = new LabPlanCompiler(new R1KeySimControllerCatalog(profile));
        RefreshControllerStates();
        RefreshCommands();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public LabTestWorkspaceViewModel() : this(new V1Workspace()) { }
    public LabTestWorkspaceViewModel(V1Workspace project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _compiler = new LabPlanCompiler();
        _editor = new LabDocumentEditor(LabDocument.New("新建测试程序"));
        _runner = new LabRunCoordinator();
        _ui = SynchronizationContext.Current;
        Facts.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in RunStepRows) row.Reset();
            }
            else if (e.NewItems is not null)
            {
                foreach (var value in e.NewItems)
                {
                    if (value is not LabStepFact fact) continue;
                    var row = RunStepRows.FirstOrDefault(x => x.StepId == fact.StepId);
                    row?.Observe(fact);
                    if (row is not null && (fact.Verdict is LabVerdict.Fail or LabVerdict.Error ||
                        SelectedRunStep is null))
                        SelectedRunStep = row;
                }
            }
            Changed(nameof(RunFactsText));
            Changed(nameof(CurrentStepText));
            Changed(nameof(CurrentFailureText));
        };
        _runner.FactProduced += fact =>
        {
            var generation = _runGeneration;
            Post(() =>
            {
                if (_runGeneration == generation && !Facts.Any(x => x.OperationId == fact.OperationId))
                    Facts.Add(fact);
            });
        };
        InsertCommand = new LabCommand(Insert, () => !Busy);
        DeleteCommand = new LabCommand(Delete, () => !Busy && _selected?.StepId is not null);
        CopyCommand = new LabCommand(Copy, () => !Busy && _selected?.StepId is not null);
        UpCommand = new LabCommand(() => Move(-1), () => !Busy && _selected?.StepId is not null);
        DownCommand = new LabCommand(() => Move(1), () => !Busy && _selected?.StepId is not null);
        RenameCommand = new LabCommand(Rename, () => !Busy && _selected?.StepId is not null);
        SetParameterCommand = new LabCommand(SetParameter, () => !Busy && _selected?.StepId is not null);
        UndoCommand = new LabCommand(() => Edit(() => _editor.Undo()), () => !Busy && _editor.CanUndo);
        RedoCommand = new LabCommand(() => Edit(() => _editor.Redo()), () => !Busy && _editor.CanRedo);
        PreflightCommand = new LabCommand(Preflight, () => !Busy);
        RunPhysicalReadOnlyCommand = new LabCommand(() => _ = RunPhysicalReadOnlyAsync(), () => !Busy && _project is not null && _physicalController is not null);
        RunSelectedPhysicalCommand = new LabCommand(() => _ = RunSelectedPhysicalAsync(),
            () => !Busy && _project is not null && _physicalController is not null);
        SelectAllCasesCommand = new LabCommand(() => SelectCases(true), () => !Busy);
        ClearCasesCommand = new LabCommand(() => SelectCases(false), () => !Busy);
        PauseCommand = new LabCommand(() => Control(_runner.Pause), () => Busy);
        ResumeCommand = new LabCommand(() => Control(_runner.Resume), () => Busy);
        StepCommand = new LabCommand(() => Control(_runner.Step), () => Busy);
        SkipCommand = new LabCommand(() => Control(() => _runner.Skip(SelectedStepId())),
            () => Busy && _selected?.StepId is not null);
        RunToCommand = new LabCommand(() => Control(() => _runner.RunTo(SelectedStepId())),
            () => Busy && _selected?.StepId is not null);
        StopCommand = new LabCommand(() => Control(_runner.Stop), () => Busy);
        SaveCommand = new LabCommand(SaveFile, () => !Busy);
        OpenCommand = new LabCommand(OpenFile, () => !Busy);
        SaveToProjectCommand = new LabCommand(SaveToProject, () => !Busy && _project is not null);
        ReloadProjectCommand = new LabCommand(ReloadFromProject, () => !Busy && _project is not null);
        RefreshControllerStatesCommand = new LabCommand(RefreshControllerStates, () => !Busy);
        ApplyControllerStateCommand = new LabCommand(ApplyControllerState,
            () => !Busy && _selected?.StepId is not null &&
                  _selectedControllerChoice is not null);
        Rebuild();
        ReloadFromProject();
    }

    public ObservableCollection<LabTreeRow> Tree { get; } = new();
    public ObservableCollection<R1RunStepRow> RunStepRows { get; } = new();
    private R1RunStepRow? _selectedRunStep;
    public R1RunStepRow? SelectedRunStep
    {
        get => _selectedRunStep;
        set
        {
            if (ReferenceEquals(_selectedRunStep, value)) return;
            _selectedRunStep = value;
            Changed();
        }
    }
    private void RefreshRunStepRows()
    {
        RunStepRows.Clear();
        var doc = Snapshot;
        var counter = 0;
        void Visit(IEnumerable<LabNode> nodes, LabPhase phase, int depth)
        {
            foreach (var node in nodes)
            {
                RunStepRows.Add(new R1RunStepRow(node, phase, ++counter, depth));
                foreach (var slot in node.Slots.OrderBy(k => k.Key, StringComparer.Ordinal))
                    Visit(slot.Value, phase, depth + 1);
            }
        }
        foreach (var phase in Enum.GetValues<LabPhase>())
            Visit(doc.Phases[phase], phase, 0);
        SelectedRunStep = RunStepRows.FirstOrDefault(x => x.Type != "Subroutine")
            ?? RunStepRows.FirstOrDefault();
    }
    // UI-only run selection; never written back to the published TEST file.
    // The barcode path intentionally ignores these choices and runs the entire TEST.
    public ObservableCollection<LabTestCaseChoice> TestCases { get; } = new();
    public ICommand RunSelectedPhysicalCommand { get; }
    public ICommand SelectAllCasesCommand { get; }
    public ICommand ClearCasesCommand { get; }
    private void SelectCases(bool selected)
    {
        foreach (var item in TestCases) item.IsSelected = selected;
        Status = selected ? "已选择全部测试用例（尚未运行）" : "已取消全部选择；不能空选运行";
    }
    private void RefreshTestCases()
    {
        var oldSelection = TestCases.ToDictionary(x => x.Id, x => x.IsSelected);
        TestCases.Clear();
        foreach (var item in LabSelectiveExecution.Discover(Snapshot))
        {
            var choice = new LabTestCaseChoice(item);
            if (oldSelection.TryGetValue(item.Id, out var previous)) choice.IsSelected = previous;
            choice.PropertyChanged += (_, _) => Changed(nameof(SelectedScopeText));
            TestCases.Add(choice);
        }
        Changed(nameof(SelectedScopeText));
    }
    public ObservableCollection<LabStepFact> Facts { get; } = new();
    public string SelectedScopeText =>
        TestCases.Count == 0 ? "当前 TEST 尚无可运行用例" :
        $"已勾选 {TestCases.Count(x => x.IsSelected)} / {TestCases.Count} 个测试用例";
    public string RunFactsText =>
        $"{Facts.Count} 条实际步骤事实";
    public string CurrentStepText
    {
        get
        {
            var last = Facts.LastOrDefault();
            return last is null ? "等待执行" :
                $"{last.Phase} · {last.NodeType} · {last.Verdict}";
        }
    }
    public string CurrentFailureText
    {
        get
        {
            var failure = Facts.LastOrDefault(f =>
                f.Verdict is LabVerdict.Error or LabVerdict.Fail);
            return failure is null ? "无已观察到的失败步骤" :
                failure.NodeType + "：" + (string.IsNullOrWhiteSpace(failure.Diagnostic)
                    ? $"预期 {failure.Expected} / 实际 {failure.Actual ?? "未观测"}"
                    : failure.Diagnostic);
        }
    }
    public ObservableCollection<KeyValuePair<string, string>> ParameterRows { get; } = new();
    public ObservableCollection<R1ControllerStateChoice> ControllerStateChoices { get; } = new();
    public R1ControllerStateChoice? SelectedControllerChoice
    {
        get => _selectedControllerChoice;
        set
        {
            _selectedControllerChoice = value;
            Changed();
            RefreshCommands();
        }
    }
    public string[] StepTypes { get; } = LabPlanCompiler.ExecutableNodes.Where(x => x != "MockObserve").OrderBy(x => x).ToArray();
    public string SelectedInsertType { get => _insertType; set { _insertType = value; Changed(); } }
    public string SelectedName { get => _name; set { _name = value; Changed(); } }
    public string ParameterName
    {
        get => _parameterName;
        set { _parameterName = value; Changed(); Changed(nameof(SelectedParameterLabel)); }
    }
    public string SelectedParameterLabel => LabStepLocalization.DisplayParameter(ParameterName);
    private KeyValuePair<string, string> _selectedParameterRow;
    public KeyValuePair<string, string> SelectedParameterRow
    {
        get => _selectedParameterRow;
        set
        {
            _selectedParameterRow = value;
            Changed();
            if (string.IsNullOrWhiteSpace(value.Key)) return;
            ParameterName = value.Key;
            ParameterValue = value.Value;
        }
    }
    public string ParameterValue { get => _parameterValue; set { _parameterValue = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Verdict { get => _verdict; private set { _verdict = value; Changed(); } }
    public bool Busy { get => _busy; private set { _busy = value; Changed(); RefreshCommands(); } }
    public string SourceLabel => "工程 TEST · 真实设备执行 / 物理能力按设备与步骤合同约束";
    public string ProjectAssetStatus => _project is null
        ? "未保存工程 · 可以编辑测试程序，正式执行前需保存到工程"
        : _project.LabProgramAssetId is { } id
            ? "工程测试程序 · " + _project.Assets.Single(x => x.Id == id).Name +
              " · r" + _project.Assets.Single(x => x.Id == id).Revision +
              "（修改后需重新提交工程版本）"
            : "当前工程尚未登记测试程序；请先保存测试程序作为工程版本资源";
    public string DocumentName => _editor.Snapshot.Name;
    public string StepCountLabel
    {
        get
        {
            var document = Snapshot;
            var total = document.Walk().Count();
            var cases = document.Walk().Count(node => node.Type == "TestCase");
            return $"{total} 步骤 · {cases} 测试用例";
        }
    }
    public ICommand InsertCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand SetParameterCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand PreflightCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StepCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand RunToCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveToProjectCommand { get; }
    public ICommand ReloadProjectCommand { get; }
    public ICommand RefreshControllerStatesCommand { get; }
    public ICommand ApplyControllerStateCommand { get; }

    public void Select(LabTreeRow? row)
    {
        _selected = row;
        if (row?.StepId is Guid id)
        {
            var node = _editor.Snapshot.Walk().FirstOrDefault(n => n.Id == id);
            if (node is not null)
            {
                SelectedName = node.Name;
                SelectedStepTypeLabel = LabStepLocalization.Display(node.Type) + " · " + LabStepLocalization.Display(row.Phase.ToString());
                ParameterRows.Clear();
                foreach (var p in node.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
                    ParameterRows.Add(p);
                var firstParameter = ParameterRows.FirstOrDefault();
                SelectedParameterRow = firstParameter;
            }
        }
        else
        {
            SelectedName = "";
            SelectedStepTypeLabel = "选择左侧步骤查看设置";
            ParameterRows.Clear();
            SelectedParameterRow = default;
            ParameterName = ""; ParameterValue = "";
        }
        RefreshCommands();
    }
    public LabDocument Snapshot => _editor.Snapshot;
    public string ExportJson() => LabDocumentCodec.Save(Snapshot);
    public void ImportJson(string json)
    {
        if (Busy) throw new InvalidOperationException("Cannot replace document during run");
        _editor = new LabDocumentEditor(LabDocumentCodec.Open(json));
        Rebuild();
        Status = "已打开测试程序；正式运行前请重新检查工程资源与设备状态";
    }
    private void RefreshCommands()
    {
        foreach (var command in new[] { InsertCommand, DeleteCommand, CopyCommand, UpCommand,
            DownCommand, RenameCommand, SetParameterCommand, UndoCommand, RedoCommand,
            PreflightCommand, RunPhysicalReadOnlyCommand, RunSelectedPhysicalCommand,
            SelectAllCasesCommand, ClearCasesCommand, PauseCommand, ResumeCommand, StepCommand,
            SkipCommand, RunToCommand, StopCommand, SaveCommand, OpenCommand,
            SaveToProjectCommand, ReloadProjectCommand, RefreshControllerStatesCommand,
            ApplyControllerStateCommand })
            (command as LabCommand)?.Refresh();
    }
    private void Rebuild()
    {
        // Editing the program must not throw the operator back to the first case.
        var previousStepId = _selected?.StepId;
        Tree.Clear();
        var doc = Snapshot;
        foreach (var phase in Enum.GetValues<LabPhase>())
        {
            var root = new LabTreeRow
            {
                Caption = LabStepLocalization.Display(phase.ToString()),
                StepName = LabStepLocalization.Display(phase.ToString()),
                Phase = phase, IsPhase = true,
                Summary = phase switch
                {
                    LabPhase.Prepare => "初始化与运行前条件",
                    LabPhase.Test => "功能与结果判定",
                    _ => "恢复安全状态与结束"
                }
            };
            AddNodes(root.Children, doc.Phases[phase], phase, null, null);
            Tree.Add(root);
        }
        // Keep an inspector populated when opening a real program. No synthetic step is added.
        _selected = null;
        RefreshTestCases(); RefreshRunStepRows(); Changed(nameof(DocumentName)); Changed(nameof(StepCountLabel));
        static LabTreeRow? FindStep(IEnumerable<LabTreeRow> roots, Guid id)
        {
            foreach (var row in roots)
            {
                if (row.StepId == id) return row;
                var nested = FindStep(row.Children, id);
                if (nested is not null) return nested;
            }
            return null;
        }
        var preferred = previousStepId.HasValue ? FindStep(Tree, previousStepId.Value) : null;
        static IEnumerable<LabTreeRow> WalkRows(IEnumerable<LabTreeRow> roots)
        {
            foreach (var row in roots)
            {
                yield return row;
                foreach (var nested in WalkRows(row.Children)) yield return nested;
            }
        }
        var parametersByNode = doc.Walk()
            .Where(n => n.Parameters.Count > 0)
            .Select(n => n.Id).ToHashSet();
        var first = preferred ?? WalkRows(Tree.Where(p => p.Phase == LabPhase.Test))
            .FirstOrDefault(row => row.StepId is Guid id && parametersByNode.Contains(id))
            ?? Tree.FirstOrDefault(p => p.Phase == LabPhase.Test)?.Children.FirstOrDefault()
            ?? Tree.SelectMany(p => p.Children).FirstOrDefault();
        Select(first);
        RefreshCommands();
    }
    private static void AddNodes(ObservableCollection<LabTreeRow> rows, List<LabNode> nodes,
        LabPhase phase, Guid? parent, string? slot)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var row = new LabTreeRow
            {
                Caption = $"{i + 1:D2}  {LabStepLocalization.Display(n.Type)}  ·  {n.Name}", Phase = phase,
                OrderLabel = (i + 1).ToString("D2"), StepName = n.Name,
                TypeLabel = LabStepLocalization.Display(n.Type),
                Summary = DescribeStep(n),
                StepId = n.Id, ParentId = parent, Slot = slot, Index = i
            };
            foreach (var branch in n.Slots)
            {
                var slotRow = new LabTreeRow
                {
                    Caption = LabStepLocalization.Display(branch.Key), Phase = phase,
                    StepName = LabStepLocalization.Display(branch.Key),
                    IsSlot = true, Summary = branch.Value.Count + " 个步骤",
                    ParentId = n.Id, Slot = branch.Key
                };
                AddNodes(slotRow.Children, branch.Value, phase, n.Id, branch.Key);
                row.Children.Add(slotRow);
            }
            rows.Add(row);
        }
    }
    private static string DescribeStep(LabNode node)
    {
        // A readable summary of the persisted parameters; no separate editable representation.
        string? Get(string key) => node.Parameters.GetValueOrDefault(key);
        return node.Type switch
        {
            "Wait" => "等待 " + (Get("milliseconds") ?? "未配置") + " 毫秒",
            "When" => (Get("variable") ?? "变量") + " = " + (Get("equals") ?? "目标值")
                + "，超时 " + (Get("timeoutMs") ?? "未配置") + " 毫秒",
            "Assert" => (Get("variable") ?? "变量") + " = " + (Get("equals") ?? "未配置"),
            "If" => (Get("variable") ?? "变量") + " = " + (Get("equals") ?? "未配置"),
            "Switch" => "判断变量：" + (Get("variable") ?? "未配置"),
            "Case" => "匹配：" + (Get("equals") ?? "未配置"),
            "Loop" => "重复 " + (Get("count") ?? "未配置") + " 次",
            "SetVariable" => (Get("variable") ?? "变量") + " ← " + (Get("value") ?? ""),
            "Observe" => "读取：" + (Get("variable") ?? "未配置"),
            "TestCase" => node.Slots.GetValueOrDefault("body")?.Count + " 个直接子步骤",
            "Subroutine" => "子程序：" + (Get("name") ?? node.Name),
            "CallSubroutine" => "调用：" + (Get("target") ?? "未配置"),
            "ControllerState" => "已配置状态：" + (Get("stateId") ?? "未关联硬件"),
            "Log" => Get("message") ?? "运行信息",
            _ => string.Join(" · ", node.Parameters.Take(2).Select(pair => pair.Key + "=" + pair.Value))
        };
    }

    public void RefreshControllerStates()
    {
        ControllerStateChoices.Clear();
        SelectedControllerChoice = null;
        var profile = _keySimControllerProfile?.Invoke();
        if (profile is null)
        {
            Status = "请先打开控制器与按键配置，创建并保存 ECU 对应的按键状态。";
            return;
        }
        foreach (var controller in profile.Controllers.OrderBy(x => x.Order))
            foreach (var state in controller.States.OrderBy(x => x.Order))
                ControllerStateChoices.Add(new R1ControllerStateChoice(
                    controller.Id, state.Id, controller.Name, state.Name,
                    profile.Revision, state.KeyMask));
        Status = "当前控制器可用按键状态 " + ControllerStateChoices.Count +
            " 项；执行前会检查配置版本是否仍然有效。";
    }

    public void ApplyControllerState()
    {
        if (_selected?.StepId is not Guid stepId ||
            _editor.Snapshot.Walk().All(x => x.Id != stepId || x.Type != "ControllerState"))
        {
            Status = "请先选中测试步骤中的“按键状态”，再关联工程中的控制器状态。";
            return;
        }
        var choice = SelectedControllerChoice;
        var profile = _keySimControllerProfile?.Invoke();
        if (choice is null || profile is null ||
            choice.Revision != profile.Revision ||
            !profile.Controllers.Any(c => c.Id == choice.ControllerId &&
                c.States.Any(state => state.Id == choice.StateId &&
                    state.KeyMask == choice.Mask)))
        {
            Status = "控制器按键配置已经变化，请刷新按键状态列表。";
            return;
        }
        Edit(() =>
        {
            _editor.SetParameter(stepId, "controllerId", choice.ControllerId.ToString("D"));
            _editor.SetParameter(stepId, "stateId", choice.StateId.ToString("D"));
            _editor.SetParameter(stepId, "revision", choice.Revision.ToString());
        });
        Status = "已关联控制器按键状态：" + choice.Label +
            "；正式执行前请保存测试程序并保存工程。";
    }

    private void Edit(Action mutation)
    {
        try { mutation(); Rebuild(); Status = "步骤已修改，尚未保存"; }
        catch (Exception ex) { Status = "无法修改步骤：" + ex.Message; }
    }
    private static LabNode NewNode(string type)
    {
        var n = LabNode.New(type, LabStepLocalization.Display(type));
        void P(string key, string value) => n.Parameters.Add(key, value);
        void S(params string[] names) { foreach (var slot in names) n.Slots.Add(slot, new List<LabNode>()); }
        switch (type)
        {
            case "Sequence": case "TestCase": S("body"); break;
            case "If": P("variable", "mode"); P("equals", "run"); S("then", "else"); break;
            case "Switch": P("variable", "mode"); S("cases"); break;
            case "Case": P("equals", "run"); S("body"); break;
            case "Default": S("body"); break;
            case "When": P("variable", "mode"); P("equals", "run"); P("timeoutMs", "1000"); S("body"); break;
            case "Loop": P("count", "2"); S("body"); break;
            case "TryCatch": S("try", "catch", "finally"); break;
            case "Subroutine": P("name", "helper"); S("body"); break;
            case "CallSubroutine": P("target", "helper"); break;
            case "SetVariable": P("variable", "mode"); P("value", "run"); break;
            case "Observe": P("variable", "result"); break;
            case "Assert": P("variable", "result"); P("equals", "ready"); break;
            case "Wait": P("milliseconds", "100"); break;
            case "ControllerState": P("controllerId", ""); P("stateId", ""); P("revision", ""); break;
            case "Fail": case "Log": P("message", ""); break;
        }
        return n;
    }
    private void Insert()
    {
        var chosen = _selected;
        var phase = chosen?.Phase ?? LabPhase.Test;
        var parent = chosen?.ParentId;
        var slot = chosen?.Slot;
        var index = chosen?.StepId is null ? (parent is null ? Snapshot.Phases[phase].Count :
            Snapshot.Walk().First(n => n.Id == parent).Slots[slot!].Count) : chosen.Index + 1;
        // A TEST CASE is a top-level runnable group, not a nested physical action.
        // Insert it at TEST root independently of the current selected step.
        if (SelectedInsertType == "TestCase")
        {
            var c = NewNode("TestCase") with { Name = "新测试用例" };
            Edit(() => _editor.Insert(LabPhase.Test, null, null,
                Snapshot.Phases[LabPhase.Test].Count, c));
            return;
        }
        // Selected container can insert directly into its first child slot.
        if (chosen?.StepId is Guid id)
        {
            var node = Snapshot.Walk().First(n => n.Id == id);
            if (node.Slots.Count > 0)
            {
                parent = id; slot = node.Slots.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
                index = node.Slots[slot].Count;
            }
        }
        Edit(() => _editor.Insert(phase, parent, slot, index, NewNode(SelectedInsertType)));
    }
    private Guid SelectedStepId() => _selected?.StepId ?? throw new InvalidOperationException("Select a step");
    private void Delete() => Edit(() => _editor.Delete(SelectedStepId()));
    private void Copy()
    {
        if (_selected is not { StepId: Guid id } row) return;
        Edit(() => _editor.Copy(id, row.Phase, row.ParentId, row.Slot, row.Index + 1));
    }
    private void Move(int delta)
    {
        if (_selected is not { StepId: Guid id } row) return;
        var count = row.ParentId is null ? Snapshot.Phases[row.Phase].Count :
            Snapshot.Walk().First(n => n.Id == row.ParentId).Slots[row.Slot!].Count;
        // Move() target is an insertion index BEFORE removal (adjusted internally).
        var destination = delta < 0 ? row.Index - 1 : row.Index + 2;
        if (destination < 0 || (delta > 0 && row.Index >= count - 1))
        { Status = "该步骤已在当前位置的最前或最后，无法继续移动"; return; }
        Edit(() => _editor.Move(id, row.Phase, row.ParentId, row.Slot, destination));
    }
    private void Rename() => Edit(() => _editor.Rename(SelectedStepId(), SelectedName));
    private void SetParameter() => Edit(() => _editor.SetParameter(SelectedStepId(), ParameterName, ParameterValue));
    private void Preflight()
    {
        try
        {
            var plan = _compiler.Prepare(Snapshot);
            Status = plan.Ready ? $"工程 TEST 预检通过 · SHA256 {plan.Digest[..12]} · 尚未执行" :
                "运行前检查未通过：" + string.Join(" | ", plan.Diagnostics.Take(4));
        }
        catch (Exception ex) { Status = "运行前检查失败：" + ex.Message; }
    }
    /// <summary>Formal R1 physical read-only run of the pinned PROJECT TEST resource.</summary>
    public Task<LabRunReport?> RunPhysicalReadOnlyAsync(CancellationToken ct = default)
        => RunPhysicalReadOnlyAsync(null, ct);

    /// <summary>
    /// The barcode variant uses exactly the same physical TEST runner as P10/P11.
    /// The scanned DUT and recipe revision are recorded as source facts, never
    /// treated as a production PASS solely because an execution finished.
    /// </summary>
    public Task<LabRunReport?> RunPhysicalReadOnlyAsync(
        R1PhysicalScanBinding? scanned, CancellationToken ct = default) =>
        RunPhysicalCoreAsync(scanned, null, ct);

    // Engineering-only execution of selected TestCase groups. The scanner path
    // still runs the full published program and does not consume UI checkboxes.
    public Task<LabRunReport?> RunSelectedPhysicalAsync(CancellationToken ct = default) =>
        RunPhysicalCoreAsync(null, TestCases.Where(x => x.IsSelected).Select(x => x.Id).ToArray(), ct);

    private async Task<LabRunReport?> RunPhysicalCoreAsync(
        R1PhysicalScanBinding? scanned, IReadOnlyCollection<Guid>? selectedCaseIds,
        CancellationToken ct = default)
    {
        if (!_physicalRunGate.Wait(0))
        {
            Status = "当前工位已有测试正在执行，请先结束当前运行。";
            return null;
        }
        Busy = true;
        try
        {
            ct.ThrowIfCancellationRequested();
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var saved = project.LabProgram ??
                throw new InvalidOperationException("工程没有已保存的测试程序；请在程序编辑器点击“保存到当前工程”。");
            // A production scan must match the exact current saved TEST asset
            // before any operator confirmation, device access or run event.
            scanned?.Verify(project);
            if (project.LabProgramAssetId is null)
                throw new InvalidOperationException("PROJECT_TEST_ASSET_REQUIRED");
            if (!string.Equals(LabDocumentCodec.Save(saved), ExportJson(), StringComparison.Ordinal))
                throw new InvalidOperationException("当前测试程序存在未保存的修改；请先保存到当前工程。");
            if (project.ProjectPath is null)
                throw new InvalidOperationException("工程尚未保存到磁盘，请先保存工程。");
            if (project.Database is null || project.DbcAssetId is null)
                throw new InvalidOperationException("PROJECT_DBC_REQUIRED");
            if (project.StaleDependencies.Count != 0)
                throw new InvalidOperationException("PROJECT_REFERENCE_STALE");
            if (scanned is not null && selectedCaseIds is not null)
                throw new InvalidOperationException("PRODUCTION_MUST_RUN_FULL_TEST");
            var selective = selectedCaseIds is null ? null :
                LabSelectiveExecution.Prepare(saved, selectedCaseIds, _compiler);
            var plan = selective?.Plan ?? _compiler.Prepare(saved);
            if (!plan.Ready)
                throw new InvalidOperationException("TEST_PREFLIGHT_BLOCKED: " + string.Join("; ", plan.Diagnostics));
            if (saved.Walk().Any(n => n.Type == "MockObserve"))
                throw new UnauthorizedAccessException("R1_PHYSICAL_TEST_HAS_MOCK_STEP");
            var controller = _physicalController?.Invoke() ??
                throw new InvalidOperationException("主控尚未连接，请从“设备连接”窗口完成接线确认并连接设备。");
            var keySteps = plan.ControllerBindings.Distinct().ToArray();
            var hasKeyActions = keySteps.Length > 0;
            Func<bool> controllerProfileCurrent = () => true;
            string controllerProfileSha = "";
            string? controllerProfilePath = null;
            EcuControlProfile? savedControllerProfile = null;
            IAuthorizedReadOnlyPhysicalLabStation station;
            if (hasKeyActions)
            {
                var active = _keySimControllerProfile?.Invoke() ??
                    throw new InvalidOperationException("P05_CONTROLLER_PROFILE_REQUIRED");
                controllerProfilePath = _keySimControllerProfilePath?.Invoke();
                if (string.IsNullOrWhiteSpace(controllerProfilePath))
                    throw new InvalidOperationException("P05_CONTROLLER_PROFILE_NOT_SAVED");
                var loaded = await ControllerRepository.OpenAsync(controllerProfilePath, ct);
                if (active.ProfileId != loaded.Profile.ProfileId ||
                    active.Revision != loaded.Profile.Revision ||
                    !string.Equals(JsonSerializer.Serialize(active),
                        JsonSerializer.Serialize(loaded.Profile), StringComparison.Ordinal))
                    throw new InvalidOperationException("P05_CONTROLLER_PROFILE_UNSAVED_CHANGES");
                savedControllerProfile = loaded.Profile;
                controllerProfilePath = loaded.Receipt.Path;
                controllerProfileSha = loaded.Receipt.Sha256;
                var fixedProfile = active;
                var fixedPath = controllerProfilePath;
                var fixedSha = controllerProfileSha;
                controllerProfileCurrent = () =>
                    ReferenceEquals(_keySimControllerProfile?.Invoke(), fixedProfile) &&
                    File.Exists(fixedPath) &&
                    string.Equals(Convert.ToHexString(
                        SHA256.HashData(File.ReadAllBytes(fixedPath))),
                        fixedSha, StringComparison.OrdinalIgnoreCase);
                var masks = keySteps.Select(binding =>
                {
                    if (!Guid.TryParse(binding.ControllerId, out var c) ||
                        !Guid.TryParse(binding.StateId, out var state) ||
                        !int.TryParse(binding.Revision, out var revision))
                        throw new InvalidOperationException("KEYSIM_TEST_CONTROLLER_REFERENCE_INVALID");
                    return savedControllerProfile.ResolveState(c, state, revision).KeyMask;
                }).ToArray();
                R1KeySimCleanupPolicy.RequireUnconditionalRelease(
                    saved, savedControllerProfile, keySteps);
                if (!controllerProfileCurrent())
                    throw new InvalidOperationException("P05_CONTROLLER_PROFILE_CHANGED_BEFORE_RUN");
                var summary = "本次真实 ECU 工程 TEST 计划：" +
                    "\nTEST SHA256：" + plan.Digest +
                    "\nP05 Controller Profile：" + fixedPath +
                    "\nProfile Revision：" + savedControllerProfile.Revision +
                    "\nCAN0 KEY_SIM 掩码操作：" +
                    string.Join(", ", masks.Select(m => "0x" + m.ToString("X5"))) +
                    "\n已检查 Cleanup 释放状态；执行中会发送实际 CAN0 0x730 帧。" +
                    "\n每个发送步骤只记录主控 ACK，不伪造按键板电压/ECU 识别结果。" +
                    "\nCAN1 实际观测/断言作为独立测试步骤；不控制 DUT 主电源。" +
                    "\n\n是否逐项授权本次已固定的实际动作？";
                if (_approveKeySimProgram?.Invoke(summary) != true)
                    throw new UnauthorizedAccessException("KEYSIM_TEST_OPERATOR_APPROVAL_DENIED");
                station = new R1PhysicalKeySimLabStation(
                    project, controller, savedControllerProfile, controllerProfileCurrent,
                    keySteps, operatorApproved: true);
            }
            else
            {
                if (_physicalApproval?.Invoke() != true)
                    throw new UnauthorizedAccessException("PHYSICAL_TEST_OPERATOR_APPROVAL_DENIED");
                station = new R2PhysicalCanReadOnlyLabStation(project, controller, true);
            }
            var programAsset = project.Assets.Single(a => a.Id == project.LabProgramAssetId.Value);
            var dbcAsset = project.Assets.Single(a => a.Id == project.DbcAssetId.Value);
            var generation = controller.Generation;
            var programHash = programAsset.Sha256;
            var dbcHash = dbcAsset.Sha256;
            _runGeneration++;
            Facts.Clear();
            Verdict = "RUNNING_PHYSICAL";
            PhysicalEvidencePath = "运行中";
            Busy = true;
            Status = hasKeyActions
                ? "真实工程 TEST 执行中：KEY_SIM CAN0 实际动作 + CAN1 观测"
                : "真实工程 TEST 执行中：CAN1 只读观测";
            try
            {
                var report = await _runner.RunAsync(plan, station, LabSource.Physical, ct);
                foreach (var fact in report.Facts)
                    if (!Facts.Any(x => x.OperationId == fact.OperationId)) Facts.Add(fact);
                scanned?.Verify(project);
                if (controller.Generation != generation || !station.IsReadOnlyAuthorized ||
                    (hasKeyActions && !controllerProfileCurrent()) ||
                    project.Assets.Single(a => a.Id == programAsset.Id).Sha256 != programHash ||
                    project.Assets.Single(a => a.Id == dbcAsset.Id).Sha256 != dbcHash)
                    throw new IOException("PHYSICAL_RUN_CONTEXT_CHANGED: 运行期间设备或资源发生变化");
                // A physical diagnostic verdict is never automatically a production PASS.
                Verdict = report.Verdict +
                    (hasKeyActions ? " / PHYSICAL_KEYSIM_CAN0" : " / PHYSICAL_CAN1_READONLY") +
                    (selectedCaseIds is null ? "" : " / SELECTED_CASES_ONLY") +
                    " / NOT_PRODUCTION";
                var root = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "AutoTest", "R1", "PhysicalRuns");
                Directory.CreateDirectory(root);
                var destination = Path.Combine(root, report.RunId.ToString("N") + ".json");
                var evidence = new
                {
                    Schema = 1, RecordedUtc = DateTimeOffset.UtcNow, ProjectPath = project.ProjectPath,
                    ProgramAssetId = programAsset.Id.Value, ProgramRevision = programAsset.Revision,
                    ProgramSha256 = programHash, DbcAssetId = dbcAsset.Id.Value,
                    DbcRevision = dbcAsset.Revision, DbcSha256 = dbcHash,
                    DeviceGeneration = generation,
                    Source = hasKeyActions ? "PhysicalCAN0KeySimAndCAN1" : "PhysicalCAN1ReadOnly",
                    ControllerProfilePath = controllerProfilePath,
                    ControllerProfileSha256 = controllerProfileSha,
                    ControllerProfileRevision = savedControllerProfile?.Revision,
                    BarcodeBinding = scanned,
                    RunScope = selectedCaseIds is null ? "FULL_PROGRAM" : "SELECTED_TEST_CASES",
                    SourceProgramDigest = selective?.SourceProgramDigest ?? plan.Digest,
                    SelectedTestCaseIds = selective?.SelectedCaseIds,
                    ProductionApproved = false, Report = report
                };
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary,
                        JsonSerializer.SerializeToUtf8Bytes(evidence), CancellationToken.None);
                    File.Move(temporary, destination);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                PhysicalEvidencePath = destination;
                Status = (selectedCaseIds is null ? "完整工程 TEST 完成" : "已勾选测试用例执行完成") + " · Run " + report.RunId +
                    " · " + report.Facts.Length + " StepFact · 已保存 JSON 证据 · 非出厂判定";
                return report;
            }
            finally { Busy = false; }
        }
        catch (Exception ex)
        {
            Verdict = "ERROR / NOT_PRODUCTION";
            Status = "测试未能执行：" + FriendlyTestFailure(ex.Message);
            return null;
        }
        finally
        {
            Busy = false;
            _physicalRunGate.Release();
        }
    }

    private void Control(Action action)
    {
        try { action(); Status = "运行控制请求已提交"; }
        catch (Exception ex) { Status = "操作已拒绝：" + ex.Message; }
    }
    private void Post(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
    private static string FriendlyTestFailure(string message) => message switch
    {
        "PROJECT_NOT_BOUND" => "当前没有工程，请新建或打开工程。",
        "P05_CONTROLLER_PROFILE_REQUIRED" => "请先创建当前 ECU 的控制器与按键配置。",
        "P05_CONTROLLER_PROFILE_NOT_SAVED" => "请先保存控制器与按键配置文件。",
        "P05_CONTROLLER_PROFILE_UNSAVED_CHANGES" => "控制器配置存在未保存修改，请先保存。",
        "P05_CONTROLLER_PROFILE_CHANGED_BEFORE_RUN" => "控制器配置在运行前发生变化，请重新加载并检查测试程序。",
        _ => message
    };

    private void SaveToProject()
    {
        try
        {
            if (Busy) throw new InvalidOperationException("测试执行期间不能保存或发布测试程序");
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var json = ExportJson();
            if (project.LabProgramAssetId is { } id)
            {
                var asset = project.Assets.Single(x => x.Id == id);
                project.ReplaceLabProgram(json, asset.ToReference());
            }
            else project.ImportLabProgram(json);
            Changed(nameof(ProjectAssetStatus));
            Status = "测试程序已加入当前工程；请保存工程文件。控制器按键步骤仍需有效配置与执行授权。";
        }
        catch (Exception ex) { Status = "保存测试程序到工程失败：" + ex.Message; }
    }

    public void ReloadFromProject()
    {
        try
        {
            if (Busy) throw new InvalidOperationException("测试执行期间不能重新加载测试程序");
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var program = project.LabProgram ?? LabDocument.New("新建测试程序");
            ImportJson(LabDocumentCodec.Save(program));
            Status = project.LabProgram is null
                ? "当前工程尚未登记测试程序；新建草稿须显式纳入工程。"
                : "已从当前工程加载测试程序；修改后需要保存新的资源版本。";
            Changed(nameof(ProjectAssetStatus));
        }
        catch (Exception ex) { Status = "从工程重新加载测试程序失败：" + ex.Message; }
    }

    private void SaveFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "AutoTest 测试程序 (*.json)|*.json", FileName = "test-program.json" };
            if (dialog.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dialog.FileName, ExportJson());
            Status = "测试程序已保存为独立文件；如要用于当前工程，请点击“保存到当前工程”。";
        }
        catch (Exception ex) { Status = "保存测试程序失败：" + ex.Message; }
    }
    private void OpenFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AutoTest 测试程序 (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            ImportJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) { Status = "打开测试程序失败：" + ex.Message; }
    }

}
