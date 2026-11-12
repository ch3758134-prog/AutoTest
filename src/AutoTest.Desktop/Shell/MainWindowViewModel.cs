using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>A projection of a persisted TEST StepNode, not a separate editable DemoStep.</summary>
public sealed class WorkbenchStepItem : BindableBase
{
    private string _runBadge = "—";
    public WorkbenchStepItem(StepNode node, TestPhase phase, StepId? parent, string? slot,
        int position, int depth, int number)
    {
        Id = node.Id; Phase = phase; Parent = parent; Slot = slot; Position = position;
        Depth = depth; Index = number; Name = node.Name; NodeType = node.Type;
        Expected = node.Properties.GetValueOrDefault("equals") ?? "";
    }
    public StepId Id { get; }
    public TestPhase Phase { get; }
    public StepId? Parent { get; }
    public string? Slot { get; }
    public int Position { get; }
    public int Depth { get; }
    public int Index { get; }
    public string Name { get; }
    public string NodeType { get; }
    public string Expected { get; }
    public string IndexLabel => Index.ToString("00");
    public string TreeLabel => $"{Phase}  " + new string(' ', Depth * 3) +
        (Depth > 0 ? "↳ " : "") + Name;
    public string RunBadge { get => _runBadge; set => SetProperty(ref _runBadge, value); }
}

public sealed record WorkbenchDbcSignal(uint FrameId, string SignalName, double Factor, string Unit)
{
    public string Label => $"0x{FrameId:X} / {SignalName} · Factor={Factor.ToString("R", CultureInfo.InvariantCulture)} {Unit}";
}
public sealed class WorkbenchResourceNode(string label)
{
    public string Label { get; } = label;
    public ObservableCollection<WorkbenchResourceNode> Children { get; } = new();
}

/// <summary>
/// Prism workbench for a real offline PROJECT/TEST workspace. V0 gateway commands are kept
/// separately for regression diagnostics; F5 runs only the TEST PreparedTestPlan.
/// </summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly IApplicationCommandGateway _gateway;
    public V1Workspace Workspace { get; }
    private WorkbenchStepItem? _selectedStep;
    private StepId? _selectionAfterEdit;
    private string _searchText = "";
    private string _editedName = "";
    private string _editedVariable = "";
    private string _editedValue = "";
    private string _editedExpected = "";
    private string _editedQuality = "Observed";
    private string _editedCount = "2";
    private string _editedMilliseconds = "0";
    private string _newStepType = "SetVariable";
    private string _newStepPhase = "Test";
    private string _diagnostic = "请选择节点，所有改动写入 TEST 事务模型。";
    private string _status = "主线工程编辑基线 · 实体运行未接入";
    private string _requestStatus = "NOT_STARTED";
    private string _ackStatus = "NO_HARDWARE_ACK";
    private string _observationStatus = "NO_OBSERVATION";
    private string _verdict = "Inconclusive / NOT_PRODUCTION";
    private string _failureReason = "";
    private string _projectLabel = "未保存工程";
    private string _editableProjectName = "未命名测试工程";
    private string _dbcFactor = "1";
    private WorkbenchDbcSignal? _selectedDbcSignal;
    private bool _busy;

    public MainWindowViewModel(IApplicationCommandGateway gateway) : this(gateway, new V1Workspace()) { }
    public MainWindowViewModel(IApplicationCommandGateway gateway, V1Workspace workspace)
    {
        _gateway = gateway;
        Workspace = workspace;
        InsertRootCommand = new DelegateCommand(() => Edit(InsertRoot), () => !_busy);
        CopyCommand = new DelegateCommand(() => Edit(CopyStep), () => !_busy && SelectedStep is not null);
        InsertBeforeCommand = new DelegateCommand(() => Edit(() => Insert(false, false)), () => !_busy);
        InsertAfterCommand = new DelegateCommand(() => Edit(() => Insert(true, false)), () => !_busy);
        InsertChildCommand = new DelegateCommand(() => Edit(() => Insert(true, true)), () => !_busy && SelectedStep is not null);
        InsertElseCommand = new DelegateCommand(() => Edit(() => Insert(true, true, true)), () => !_busy && SelectedStep?.NodeType == "If");
        DeleteCommand = new DelegateCommand(() => Edit(() => { if (SelectedStep is not null) Workspace.Editor.Delete(SelectedStep.Id); }), () => !_busy && SelectedStep is not null);
        MoveUpCommand = new DelegateCommand(() => Edit(() => Move(-1)), () => !_busy && SelectedStep is not null);
        MoveDownCommand = new DelegateCommand(() => Edit(() => Move(1)), () => !_busy && SelectedStep is not null);
        ApplyPropertiesCommand = new DelegateCommand(() => Edit(ApplyProperties), () => !_busy && SelectedStep is not null);
        PinToDbcCommand = new DelegateCommand(() => Edit(() => { if (SelectedStep is not null) Workspace.PinStepToDbc(SelectedStep.Id); }), () => !_busy && SelectedStep is not null);
        RebindCommand = new DelegateCommand(() => Edit(() => { if (SelectedStep is not null) Workspace.RebindDbcReference(SelectedStep.Id); }), () => !_busy && SelectedStep is not null);
        ValidateCommand = new DelegateCommand(Validate);
        UndoCommand = new DelegateCommand(() => Edit(() => Workspace.Editor.Undo()), () => !_busy && Workspace.Editor.CanUndo);
        RedoCommand = new DelegateCommand(() => Edit(() => Workspace.Editor.Redo()), () => !_busy && Workspace.Editor.CanRedo);
        NewProjectCommand = new DelegateCommand(NewProject, () => !_busy);
        OpenProjectCommand = new DelegateCommand(() => _ = OpenProjectDialogAsync(), () => !_busy);
        SaveCommand = new DelegateCommand(() => _ = SaveProjectDialogAsync(), () => !_busy);
        ImportDbcCommand = new DelegateCommand(() => _ = ImportDbcDialogAsync(), () => !_busy);
        EditDbcCommand = new DelegateCommand(EditDbc, () => !_busy && SelectedDbcSignal is not null);
        RenameProjectCommand = new DelegateCommand(() => Edit(() => Workspace.RenameProject(EditableProjectName)), () => !_busy);
        AddExampleCommand = new DelegateCommand(() => Edit(() => Workspace.AddEditableMockExample()), () => !_busy);
        RunCommand = new DelegateCommand(() => _ = RunCurrentDocumentAsync(), () => !_busy);
        StopCommand = new DelegateCommand(() => Workspace.Stop(), () => _busy);
        PauseCommand = new DelegateCommand(() => EditRunControl(() => Workspace.Pause()));
        ResumeCommand = new DelegateCommand(() => EditRunControl(() => Workspace.Resume()));
        StepCommand = new DelegateCommand(() => EditRunControl(() => Workspace.Step()));
        NormalCommand = new DelegateCommand(() => _ = ExecuteScenarioAsync(ApplicationCommandKind.MockNormal), () => !_busy);
        TimeoutCommand = new DelegateCommand(() => _ = ExecuteScenarioAsync(ApplicationCommandKind.MockTimeout), () => !_busy);
        DisconnectCommand = new DelegateCommand(() => _ = ExecuteScenarioAsync(ApplicationCommandKind.MockDisconnect), () => !_busy);
        UnauthorizedCommand = new DelegateCommand(() => _ = ExecuteScenarioAsync(ApplicationCommandKind.MockUnauthorized), () => !_busy);
        FocusNameCommand = new DelegateCommand(() => Diagnostic = "请在右侧属性检查器编辑名称。 ");
        OpenCanCommand = Tool("CAN"); OpenDbcCommand = Tool("DBC"); OpenUdsCommand = Tool("UDS");
        OpenFlashCommand = Tool("Flash"); OpenWaveCommand = Tool("Wave");
        Refresh();
    }

    public ObservableCollection<WorkbenchStepItem> Steps { get; } = new();
    public ObservableCollection<string> AvailablePhases { get; } = new(new[] { "Prepare", "Test", "Cleanup" });
    public ObservableCollection<WorkbenchStepItem> FilteredSteps { get; } = new();
    public ObservableCollection<string> ResourceLines { get; } = new();
    public ObservableCollection<WorkbenchResourceNode> ResourceNodes { get; } = new();
    public ObservableCollection<WorkbenchDbcSignal> DbcSignals { get; } = new();
    public WorkbenchDbcSignal? SelectedDbcSignal
    {
        get => _selectedDbcSignal;
        set
        {
            if (!SetProperty(ref _selectedDbcSignal, value)) return;
            DbcFactor = value?.Factor.ToString("R", CultureInfo.InvariantCulture) ?? "";
            UpdateCommands();
        }
    }
    public ObservableCollection<string> FactLines { get; } = new();
    public ObservableCollection<string> AvailableTypes { get; } = new(new[]
        { "SetVariable", "Assert", "Wait", "Sequence", "If", "Loop" });
    public WorkbenchStepItem? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!SetProperty(ref _selectedStep, value)) return;
            var node = value is null ? null : Workspace.Document.Walk().FirstOrDefault(n => n.Id == value.Id);
            EditedName = node?.Name ?? "";
            EditedVariable = node?.Properties.GetValueOrDefault("variable") ?? "";
            EditedValue = node?.Properties.GetValueOrDefault("value") ?? "";
            EditedExpected = node?.Properties.GetValueOrDefault("equals") ?? "";
            EditedCount = node?.Properties.GetValueOrDefault("count") ?? "2";
            EditedMilliseconds = node?.Properties.GetValueOrDefault("milliseconds") ?? "0";
            EditedQuality = node?.Properties.GetValueOrDefault("quality") ?? "Observed";
            RaisePropertyChanged(nameof(SelectedAssetStatus));
            UpdateCommands();
        }
    }
    public string SelectedAssetStatus => SelectedStep is null ? "未选择步骤" :
        Workspace.StepAssetStatus(Workspace.Document.Walk().First(n => n.Id == SelectedStep.Id));
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshFilter(); } }
    public string EditedName { get => _editedName; set => SetProperty(ref _editedName, value); }
    public string EditedVariable { get => _editedVariable; set => SetProperty(ref _editedVariable, value); }
    public string EditedValue { get => _editedValue; set => SetProperty(ref _editedValue, value); }
    public string EditedExpected { get => _editedExpected; set => SetProperty(ref _editedExpected, value); }
    public string EditedQuality { get => _editedQuality; set => SetProperty(ref _editedQuality, value); }
    public string EditedCount { get => _editedCount; set => SetProperty(ref _editedCount, value); }
    public string EditedMilliseconds { get => _editedMilliseconds; set => SetProperty(ref _editedMilliseconds, value); }
    public string NewStepType { get => _newStepType; set => SetProperty(ref _newStepType, value); }
    public string NewStepPhase { get => _newStepPhase; set => SetProperty(ref _newStepPhase, value); }
    public string ProjectLabel { get => _projectLabel; private set => SetProperty(ref _projectLabel, value); }
    public string EditableProjectName { get => _editableProjectName; set => SetProperty(ref _editableProjectName, value); }
    public string DbcFactor { get => _dbcFactor; set => SetProperty(ref _dbcFactor, value); }
    public string Diagnostic { get => _diagnostic; private set => SetProperty(ref _diagnostic, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string RequestStatus { get => _requestStatus; private set => SetProperty(ref _requestStatus, value); }
    public string AckStatus { get => _ackStatus; private set => SetProperty(ref _ackStatus, value); }
    public string ObservationStatus { get => _observationStatus; private set => SetProperty(ref _observationStatus, value); }
    public string Verdict { get => _verdict; private set => SetProperty(ref _verdict, value); }
    public string FailureReason { get => _failureReason; private set => SetProperty(ref _failureReason, value); }
    public DelegateCommand InsertRootCommand { get; }
    public DelegateCommand CopyCommand { get; }
    public DelegateCommand InsertBeforeCommand { get; }
    public DelegateCommand InsertAfterCommand { get; }
    public DelegateCommand InsertChildCommand { get; }
    public DelegateCommand InsertElseCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand MoveUpCommand { get; }
    public DelegateCommand MoveDownCommand { get; }
    public DelegateCommand ApplyPropertiesCommand { get; }
    public DelegateCommand PinToDbcCommand { get; }
    public DelegateCommand RebindCommand { get; }
    public DelegateCommand ValidateCommand { get; }
    public DelegateCommand UndoCommand { get; }
    public DelegateCommand RedoCommand { get; }
    public DelegateCommand NewProjectCommand { get; }
    public DelegateCommand OpenProjectCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ImportDbcCommand { get; }
    public DelegateCommand EditDbcCommand { get; }
    public DelegateCommand RenameProjectCommand { get; }
    public DelegateCommand AddExampleCommand { get; }
    public DelegateCommand RunCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand PauseCommand { get; }
    public DelegateCommand ResumeCommand { get; }
    public DelegateCommand StepCommand { get; }
    public DelegateCommand NormalCommand { get; }
    public DelegateCommand TimeoutCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand UnauthorizedCommand { get; }
    public DelegateCommand FocusNameCommand { get; }
    public DelegateCommand OpenCanCommand { get; }
    public DelegateCommand OpenDbcCommand { get; }
    public DelegateCommand OpenUdsCommand { get; }
    public DelegateCommand OpenFlashCommand { get; }
    public DelegateCommand OpenWaveCommand { get; }

    private DelegateCommand Tool(string label) => new(() =>
    {
        Diagnostic = $"{label} 专业工具未注册；当前仅有工程、DBC 和 TEST 编辑；真实执行待集成。";
        Status = "无真实设备连接或输出";
    });
    private void Edit(Action work)
    {
        try
        {
            if (_busy) return;
            var id = SelectedStep?.Id;
            _selectionAfterEdit = null;
            work(); Refresh(_selectionAfterEdit ?? id);
            Diagnostic = "已提交 TEST 编辑事务。请使用 Ctrl+S 保存完整工程。";
        }
        catch (Exception ex) { Diagnostic = ex.Message; }
        UpdateCommands();
    }
    private void EditRunControl(Action work)
    {
        try { work(); } catch (Exception ex) { Diagnostic = ex.Message; }
    }
    private StepNode NewNode() => NewStepType switch
    {
        "MockObserve" => StepNode.Create("MockObserve", "模拟观测")
            .WithProperty("variable", "pressure").WithProperty("value", "8").WithProperty("quality", "Observed"),
        "Assert" => StepNode.Create("Assert", "断言观测")
            .WithProperty("variable", "pressure").WithProperty("equals", "8"),
        "SetVariable" => StepNode.Create("SetVariable", "设置变量")
            .WithProperty("variable", "mode").WithProperty("value", "ready"),
        "Wait" => StepNode.Create("Wait", "等待")
            .WithProperty("milliseconds", "0"),
        "Loop" => StepNode.Create("Loop", "循环").WithProperty("count", "2").WithSlot("body"),
        "Sequence" => StepNode.Create("Sequence", "顺序块").WithSlot("body"),
        "If" => StepNode.Create("If", "条件")
            .WithProperty("variable", "mode").WithProperty("equals", "ready").WithSlot("then").WithSlot("else"),
        _ => throw new InvalidOperationException("不支持的 V1 节点类型")
    };
    private void InsertRoot()
    {
        if (!Enum.TryParse<TestPhase>(NewStepPhase, out var phase))
            throw new ArgumentException("无效的测试阶段");
        var node = NewNode();
        Workspace.Editor.Insert(phase, null, null, Workspace.Document.Get(phase).Length, node);
        _selectionAfterEdit = node.Id;
    }
    private void CopyStep()
    {
        if (SelectedStep is not { } item) return;
        Workspace.Editor.Copy(item.Id, item.Phase, item.Parent, item.Slot, item.Position + 1);
        var siblings = item.Parent is null
            ? Workspace.Document.Get(item.Phase)
            : Workspace.Document.Walk().First(n => n.Id == item.Parent.Value).Slots[item.Slot!];
        _selectionAfterEdit = siblings[item.Position + 1].Id;
    }
    private void Insert(bool after, bool child, bool intoElse = false)
    {
        var at = SelectedStep;
        var phase = at?.Phase ?? TestPhase.Test;
        var parent = child ? at?.Id : at?.Parent;
        string? slot = child ? at?.NodeType switch
        {
            "Loop" or "Sequence" => "body", "If" => intoElse ? "else" : "then", _ => null
        } : at?.Slot;
        if (child && slot is null) throw new InvalidOperationException("只能在 Sequence / Loop / If 内插入子节点");
        var count = CountSiblings(phase, parent, slot);
        var position = child ? count : at is null ? count : Math.Min(count, at.Position + (after ? 1 : 0));
        var newNode = NewNode();
        Workspace.Editor.Insert(phase, parent, slot, position, newNode);
        _selectionAfterEdit = newNode.Id;
    }
    private int CountSiblings(TestPhase phase, StepId? parent, string? slot)
    {
        if (parent is null) return Workspace.Document.Get(phase).Length;
        var node = Workspace.Document.Walk().First(n => n.Id == parent.Value);
        return node.Slots[slot!].Length;
    }
    private void Move(int delta)
    {
        if (SelectedStep is not { } item) return;
        var count = CountSiblings(item.Phase, item.Parent, item.Slot);
        var target = item.Position + delta;
        if (target < 0 || target >= count) return;
        Workspace.Editor.Move(item.Id, item.Phase, item.Parent, item.Slot,
            delta > 0 ? target + 1 : target);
    }
    private void ApplyProperties()
    {
        if (SelectedStep is not { } item) return;
        if (string.IsNullOrWhiteSpace(EditedName) || EditedName.Length > 120)
            throw new ArgumentException("名称不能为空且不得超过 120 字符");
        var node = Workspace.Document.Walk().First(n => n.Id == item.Id);
        var props = node.Properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        switch (node.Type)
        {
            case "MockObserve":
                props["variable"] = EditedVariable; props["value"] = EditedValue;
                props["quality"] = EditedQuality; break;
            case "Assert": case "If":
                props["variable"] = EditedVariable; props["equals"] = EditedExpected; break;
            case "SetVariable":
                props["variable"] = EditedVariable; props["value"] = EditedValue; break;
            case "Loop": props["count"] = EditedCount; break;
            case "Wait": props["milliseconds"] = EditedMilliseconds; break;
        }
        Workspace.Editor.ApplyProperties(item.Id, EditedName.Trim(), props);
    }
    private void Refresh(StepId? selected = null)
    {
        selected ??= SelectedStep?.Id;
        Steps.Clear(); var number = 0;
        void Add(ImmutableArray<StepNode> nodes, TestPhase phase, StepId? parent, string? slot, int depth)
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                Steps.Add(new WorkbenchStepItem(node, phase, parent, slot, i, depth, ++number));
                foreach (var entry in node.Slots.OrderBy(s => s.Key, StringComparer.Ordinal))
                    Add(entry.Value, phase, node.Id, entry.Key, depth + 1);
            }
        }
        foreach (var phase in Enum.GetValues<TestPhase>()) Add(Workspace.Document.Get(phase), phase, null, null, 0);
        ResourceLines.Clear();
        ResourceLines.Add($"工程：{Workspace.Project.Name} · r{Workspace.Project.Revision}");
        foreach (var a in Workspace.Assets)
        {
            var stale = Workspace.StaleDependencies.Any(d => d.To == a.Id);
            ResourceLines.Add($"{a.Kind.ToUpperInvariant()}  {a.Name} · r{a.Revision}" + (stale ? "  ⚠ 引用过期" : ""));
        }
        ResourceLines.Add("测试程序：" + Workspace.Document.Name);
        ResourceNodes.Clear();
        var root = new WorkbenchResourceNode($"工程 · {Workspace.Project.Name} · r{Workspace.Project.Revision}");
        var assetGroup = new WorkbenchResourceNode("资源 / Assets");
        foreach (var asset in Workspace.Assets)
        {
            var stale = Workspace.StaleDependencies.Any(d => d.To == asset.Id);
            assetGroup.Children.Add(new WorkbenchResourceNode($"{asset.Kind} / {asset.Name} · r{asset.Revision}" +
                (stale ? " ⚠ REFERENCE_STALE" : "")));
        }
        root.Children.Add(assetGroup);
        var testsGroup = new WorkbenchResourceNode("测试程序 / TEST");
        testsGroup.Children.Add(new WorkbenchResourceNode(Workspace.Document.Name +
            $" ({Workspace.Document.Walk().Count()} 步骤)"));
        root.Children.Add(testsGroup);
        ResourceNodes.Add(root);
        EditableProjectName = Workspace.Project.Name;
        var oldDbc = SelectedDbcSignal;
        DbcSignals.Clear();
        if (Workspace.Database is { } db)
            foreach (var msg in db.Messages)
                foreach (var signal in msg.Signals)
                    DbcSignals.Add(new WorkbenchDbcSignal(msg.Id, signal.Name, signal.Factor, signal.Unit));
        SelectedDbcSignal = DbcSignals.FirstOrDefault(x => x.FrameId == oldDbc?.FrameId &&
            x.SignalName == oldDbc?.SignalName) ?? DbcSignals.FirstOrDefault();
        ProjectLabel = Workspace.ProjectPath is null ? "未保存 · " + Workspace.Project.Name :
            Path.GetFileName(Workspace.ProjectPath) + " · " + Workspace.Project.Name;
        RefreshFilter();
        SelectedStep = Steps.FirstOrDefault(s => s.Id == selected) ?? Steps.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedAssetStatus));
        UpdateCommands();
    }
    private void RefreshFilter()
    {
        FilteredSteps.Clear();
        foreach (var step in Steps.Where(s => string.IsNullOrWhiteSpace(SearchText) ||
            s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            s.NodeType.Contains(SearchText, StringComparison.OrdinalIgnoreCase))) FilteredSteps.Add(step);
    }
    private void Validate()
    {
        var errors = Workspace.Diagnostics();
        Diagnostic = errors.Count == 0 ? "Prepare READY · 测试文档及资源引用有效（仅完成预检，不执行测试）"
            : "Prepare BLOCKED: " + string.Join("; ", errors);
    }
    private void NewProject()
    {
        try { Workspace.CreateProject("新建工程"); Refresh(); FactLines.Clear(); Status = "已新建未保存工程"; }
        catch (Exception ex) { Diagnostic = ex.Message; }
    }
    private async Task OpenProjectDialogAsync()
    {
        var dlg = new OpenFileDialog { Title = "打开 AutoTest 工程", Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json" };
        if (dlg.ShowDialog() == true) await OpenProjectAsync(dlg.FileName);
    }
    public async Task OpenProjectAsync(string path)
    {
        if (_busy) return;
        _busy = true; UpdateCommands();
        try
        {
            await Workspace.OpenProjectAsync(path);
            Refresh(); FactLines.Clear(); FailureReason = "";
            Status = "工程和 TEST 文档已从磁盘重开（未启动执行）";
            Validate();
        }
        catch (Exception ex) { Diagnostic = "打开工程失败：" + ex.Message; }
        finally { _busy = false; UpdateCommands(); }
    }
    private async Task SaveProjectDialogAsync()
    {
        var path = Workspace.ProjectPath;
        if (path is null)
        {
            var dlg = new SaveFileDialog { Title = "保存 AutoTest 工程（含 DBC 与测试程序）",
                Filter = "AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json",
                FileName = "MyProject.autotest-project.json", AddExtension = true };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        await SaveProjectAsync(path);
    }
    public async Task SaveProjectAsync(string path)
    {
        if (_busy) return;
        _busy = true; UpdateCommands();
        try
        {
            await Workspace.SaveProjectAsync(path);
            Refresh(); Status = "工程图、DBC、TEST 文档均已落盘（非生产记录）";
        }
        catch (Exception ex) { Diagnostic = "保存工程失败：" + ex.Message; }
        finally { _busy = false; UpdateCommands(); }
    }
    private async Task ImportDbcDialogAsync()
    {
        var dlg = new OpenFileDialog { Title = "导入 DBC（UTF-8，部分语法）", Filter = "DBC 文件 (*.dbc)|*.dbc" };
        if (dlg.ShowDialog() != true) await Task.CompletedTask;
        else
        {
            try
            {
                var text = await File.ReadAllTextAsync(dlg.FileName);
                Workspace.ImportDbc(text, Path.GetFileName(dlg.FileName));
                Refresh(); Status = "DBC 已导入工程资源树，Ctrl+S 后持久化";
            }
            catch (Exception ex) { Diagnostic = "DBC 导入失败：" + ex.Message; }
        }
    }
    private void EditDbc()
    {
        try
        {
            if (SelectedDbcSignal is not { } selected)
            {
                Diagnostic = "请在资源管理器选择一个 DBC 信号"; return;
            }
            if (!double.TryParse(DbcFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) ||
                !double.IsFinite(factor) || factor == 0)
                throw new ArgumentException("Factor 必须是有限非零数（小数点使用 .）");
            Workspace.EditDbcSignal(selected.FrameId, selected.SignalName, factor);
            Refresh();
            Diagnostic = $"DBC {selected.SignalName} Factor={factor:R} 已保存为新 Revision；旧引用保留，需手动重绑";
        }
        catch (Exception ex) { Diagnostic = "DBC 编辑失败：" + ex.Message; }
    }
    public async Task<MockRunReport?> RunCurrentDocumentAsync()
    {
        if (_busy) return null;
        Validate(); if (Workspace.Diagnostics().Count != 0)
        {
            RequestStatus = "PREPARE_BLOCKED"; Verdict = "Inconclusive / NOT_PRODUCTION";
            FailureReason = Diagnostic; return null;
        }
        _busy = true; UpdateCommands();
        RequestStatus = "RUNNING_TEST_MOCK"; AckStatus = "NO_HARDWARE_ACK";
        ObservationStatus = "SIMULATED_ONLY"; Verdict = "Inconclusive / NOT_PRODUCTION";
        FailureReason = "";
        try
        {
            FactLines.Clear();
            var report = await Task.Run(() => Workspace.RunMockAsync());
            RequestStatus = report.Stopped ? "STOPPED_MOCK" : "MOCK_STEPS_EXECUTED";
            Verdict = $"SIMULATED_{report.Verdict} / NOT_PRODUCTION";
            ObservationStatus = "SIMULATED_STEP_FACTS";
            foreach (var fact in report.Facts)
                FactLines.Add($"[MOCK/Simulated] {fact.Type} {fact.StepId} 期待={fact.Expected} 实际={fact.Actual ?? "<无>"} " +
                    $"质量={fact.Quality} 判定={fact.Verdict} {fact.Detail}");
            foreach (var step in Steps)
            {
                var matches = report.Facts.Where(f => f.StepId == step.Id).ToArray();
                if (matches.Length == 0) continue;
                step.RunBadge = "Mock: " + matches[^1].Verdict;
            }
            Status = "实际 TEST 步骤已执行 · 仅模拟证据，严禁作为出厂 PASS";
            return report;
        }
        catch (Exception ex)
        {
            RequestStatus = "MOCK_ERROR"; FailureReason = ex.Message;
            Verdict = "Inconclusive / NOT_PRODUCTION"; return null;
        }
        finally { _busy = false; UpdateCommands(); }
    }
    // Unchanged V0 diagnostic scenario, deliberately NOT bound to F5 / RunCommand.
    public async Task ExecuteScenarioAsync(ApplicationCommandKind kind)
    {
        if (_busy) return;
        var command = new ApplicationCommand(kind, Guid.NewGuid());
        var denial = _gateway.GetCanExecuteReason(command);
        if (denial is not null) { FailureReason = denial; return; }
        _busy = true; UpdateCommands();
        try
        {
            var result = await _gateway.ExecuteAsync(command, CancellationToken.None);
            var projection = result.Projection;
            RequestStatus = projection.RequestStatus; AckStatus = projection.AckStatus;
            ObservationStatus = projection.ObservationStatus; Verdict = projection.Verdict.ToString();
            FailureReason = projection.FailureReason;
            FactLines.Clear(); foreach (var fact in projection.Facts)
                FactLines.Add($"[V0-SIM] {fact.Kind,-15} [{fact.Quality}] {fact.Description}");
        }
        catch (Exception e) { RequestStatus = "ERROR"; FailureReason = "HOST_MOCK_EXCEPTION: " + e.GetType().Name; }
        finally { _busy = false; UpdateCommands(); }
    }
    private void UpdateCommands()
    {
        foreach (var command in new[] { InsertRootCommand, CopyCommand, InsertBeforeCommand, InsertAfterCommand, InsertChildCommand, InsertElseCommand,
            DeleteCommand, MoveUpCommand, MoveDownCommand, ApplyPropertiesCommand, PinToDbcCommand,
            RebindCommand, UndoCommand, RedoCommand, NewProjectCommand, OpenProjectCommand,
            SaveCommand, ImportDbcCommand, EditDbcCommand, RenameProjectCommand, AddExampleCommand, RunCommand, StopCommand,
            NormalCommand, TimeoutCommand, DisconnectCommand, UnauthorizedCommand })
            command?.RaiseCanExecuteChanged();
    }
}
