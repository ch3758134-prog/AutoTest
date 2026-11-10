using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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
    public Guid? StepId { get; init; }
    public Guid? ParentId { get; init; }
    public string? Slot { get; init; }
    public int Index { get; init; }
    public ObservableCollection<LabTreeRow> Children { get; } = new();
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
    private readonly ILabStation _station;
    private readonly LabPlanCompiler _compiler;
    private readonly SynchronizationContext? _ui;
    private LabTreeRow? _selected;
    private string _name = "";
    private string _parameterName = "variable", _parameterValue = "";
    private string _insertType = "MockObserve";
    private string _status = "Ready — Mock mode; physical outputs prohibited";
    private string _verdict = "NOT_RUN";
    private bool _busy;
    private long _runGeneration;
    private Func<HardwareBenchController?>? _physicalController;
    private Func<bool>? _physicalApproval;
    private string _physicalEvidencePath = "未执行";
    public string PhysicalEvidencePath { get => _physicalEvidencePath; private set { _physicalEvidencePath = value; Changed(); } }
    public ICommand RunPhysicalReadOnlyCommand { get; private set; } = null!;
    public void AttachPhysicalReadOnly(Func<HardwareBenchController?> controller, Func<bool> approve)
    {
        _physicalController = controller ?? throw new ArgumentNullException(nameof(controller));
        _physicalApproval = approve ?? throw new ArgumentNullException(nameof(approve));
        RefreshCommands();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public LabTestWorkspaceViewModel() : this(new SimulatedLabStation(), new LabPlanCompiler()) { }
    public LabTestWorkspaceViewModel(V1Workspace project) : this(new SimulatedLabStation(), new LabPlanCompiler())
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        ReloadFromProject();
    }
    public LabTestWorkspaceViewModel(ILabStation station, LabPlanCompiler compiler)
    {
        _station = station; _compiler = compiler;
        _editor = new LabDocumentEditor(CreateSample());
        _runner = new LabRunCoordinator();
        _ui = SynchronizationContext.Current;
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
        RunMockCommand = new LabCommand(() => _ = RunMockAsync(), () => !Busy);
        RunPhysicalReadOnlyCommand = new LabCommand(() => _ = RunPhysicalReadOnlyAsync(), () => !Busy && _project is not null && _physicalController is not null);
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
        Rebuild();
    }

    public ObservableCollection<LabTreeRow> Tree { get; } = new();
    public ObservableCollection<LabStepFact> Facts { get; } = new();
    public ObservableCollection<KeyValuePair<string, string>> ParameterRows { get; } = new();
    public string[] StepTypes { get; } = LabPlanCompiler.ExecutableNodes.OrderBy(x => x).ToArray();
    public string SelectedInsertType { get => _insertType; set { _insertType = value; Changed(); } }
    public string SelectedName { get => _name; set { _name = value; Changed(); } }
    public string ParameterName { get => _parameterName; set { _parameterName = value; Changed(); } }
    public string ParameterValue { get => _parameterValue; set { _parameterValue = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Verdict { get => _verdict; private set { _verdict = value; Changed(); } }
    public bool Busy { get => _busy; private set { _busy = value; Changed(); RefreshCommands(); } }
    public string SourceLabel => _project is null ? "STANDALONE LAB / MOCK" :
        "PROJECT-MANAGED R2 TEST / MOCK EXECUTION ONLY";
    public string ProjectAssetStatus => _project is null
        ? "未绑定工程；LAB 文件独立保存"
        : _project.LabProgramAssetId is { } id
            ? "工程测试程序 · " + _project.Assets.Single(x => x.Id == id).Name +
              " · r" + _project.Assets.Single(x => x.Id == id).Revision +
              "（修改后需重新提交工程版本）"
            : "当前工程尚无 R2 高级测试程序；点击【纳入当前工程】登记版本";
    public string DocumentName => _editor.Snapshot.Name;
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
    public ICommand RunMockCommand { get; }
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

    public void Select(LabTreeRow? row)
    {
        _selected = row;
        if (row?.StepId is Guid id)
        {
            var node = _editor.Snapshot.Walk().FirstOrDefault(n => n.Id == id);
            if (node is not null)
            {
                SelectedName = node.Name;
                ParameterRows.Clear();
                foreach (var p in node.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
                    ParameterRows.Add(p);
            }
        }
        else { SelectedName = ""; ParameterRows.Clear(); }
        RefreshCommands();
    }
    public LabDocument Snapshot => _editor.Snapshot;
    public string ExportJson() => LabDocumentCodec.Save(Snapshot);
    public void ImportJson(string json)
    {
        if (Busy) throw new InvalidOperationException("Cannot replace document during run");
        _editor = new LabDocumentEditor(LabDocumentCodec.Open(json));
        Rebuild();
        Status = "Document loaded — re-run preflight before execution";
    }
    private void RefreshCommands()
    {
        foreach (var command in new[] { InsertCommand, DeleteCommand, CopyCommand, UpCommand,
            DownCommand, RenameCommand, SetParameterCommand, UndoCommand, RedoCommand,
            PreflightCommand, RunMockCommand, RunPhysicalReadOnlyCommand, PauseCommand, ResumeCommand, StepCommand,
            SkipCommand, RunToCommand, StopCommand, SaveCommand, OpenCommand,
            SaveToProjectCommand, ReloadProjectCommand })
            (command as LabCommand)?.Refresh();
    }
    private void Rebuild()
    {
        Tree.Clear();
        var doc = Snapshot;
        foreach (var phase in Enum.GetValues<LabPhase>())
        {
            var root = new LabTreeRow { Caption = phase.ToString().ToUpperInvariant(), Phase = phase };
            AddNodes(root.Children, doc.Phases[phase], phase, null, null);
            Tree.Add(root);
        }
        _selected = null; Changed(nameof(DocumentName)); RefreshCommands();
    }
    private static void AddNodes(ObservableCollection<LabTreeRow> rows, List<LabNode> nodes,
        LabPhase phase, Guid? parent, string? slot)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var row = new LabTreeRow
            {
                Caption = $"{i + 1:D2}  {n.Type}  ·  {n.Name}", Phase = phase,
                StepId = n.Id, ParentId = parent, Slot = slot, Index = i
            };
            foreach (var branch in n.Slots)
            {
                var slotRow = new LabTreeRow
                {
                    Caption = branch.Key.ToUpperInvariant(), Phase = phase,
                    ParentId = n.Id, Slot = branch.Key
                };
                AddNodes(slotRow.Children, branch.Value, phase, n.Id, branch.Key);
                row.Children.Add(slotRow);
            }
            rows.Add(row);
        }
    }
    private void Edit(Action mutation)
    {
        try { mutation(); Rebuild(); Status = "Edited — not saved"; }
        catch (Exception ex) { Status = "Edit rejected: " + ex.Message; }
    }
    private static LabNode NewNode(string type)
    {
        var n = LabNode.New(type);
        void P(string key, string value) => n.Parameters.Add(key, value);
        void S(params string[] names) { foreach (var slot in names) n.Slots.Add(slot, new List<LabNode>()); }
        switch (type)
        {
            case "Sequence": S("body"); break;
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
            case "MockObserve": P("variable", "result"); P("value", "ready"); P("quality", "Observed"); break;
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
        { Status = "Step is at the edge"; return; }
        Edit(() => _editor.Move(id, row.Phase, row.ParentId, row.Slot, destination));
    }
    private void Rename() => Edit(() => _editor.Rename(SelectedStepId(), SelectedName));
    private void SetParameter() => Edit(() => _editor.SetParameter(SelectedStepId(), ParameterName, ParameterValue));
    private void Preflight()
    {
        try
        {
            var plan = _compiler.Prepare(Snapshot);
            Status = plan.Ready ? $"Ready · SHA256 {plan.Digest[..12]} · Mock-only" :
                "Preflight blocked: " + string.Join(" | ", plan.Diagnostics.Take(4));
        }
        catch (Exception ex) { Status = "Preflight error: " + ex.Message; }
    }
    public async Task RunMockAsync()
    {
        if (Busy) return;
        LabPreparedPlan plan;
        try
        {
            plan = _compiler.Prepare(Snapshot);
            if (!plan.Ready) { Status = "Preflight blocked: " + string.Join(" | ", plan.Diagnostics.Take(4)); return; }
        }
        catch (Exception ex) { Status = "Preflight error: " + ex.Message; return; }
        _runGeneration++;
        Facts.Clear(); Verdict = "RUNNING"; Busy = true;
        Status = "Running Mock — not a physical ECU or production verdict";
        try
        {
            var report = await _runner.RunAsync(plan, _station, LabSource.Mock);
            foreach (var fact in report.Facts)
                if (!Facts.Any(x => x.OperationId == fact.OperationId)) Facts.Add(fact);
            Verdict = report.Verdict.ToString().ToUpperInvariant();
            Status = $"Mock {Verdict} · RunId {report.RunId} · {report.Facts.Length} facts";
        }
        catch (Exception ex) { Verdict = "ERROR"; Status = "Run failed: " + ex.Message; }
        finally { Busy = false; }
    }
    /// <summary>Formal R1 physical read-only run of the pinned PROJECT TEST resource.</summary>
    public async Task<LabRunReport?> RunPhysicalReadOnlyAsync(CancellationToken ct = default)
    {
        if (Busy) return null;
        try
        {
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var saved = project.LabProgram ??
                throw new InvalidOperationException("PROJECT_TEST_NOT_REGISTERED: 请先在 P10 纳入工程。");
            if (project.LabProgramAssetId is null)
                throw new InvalidOperationException("PROJECT_TEST_ASSET_REQUIRED");
            if (!string.Equals(LabDocumentCodec.Save(saved), ExportJson(), StringComparison.Ordinal))
                throw new InvalidOperationException("TEST_UNSAVED_CHANGES: 请先将 P10 修改登记到工程。");
            if (project.ProjectPath is null)
                throw new InvalidOperationException("PROJECT_NOT_SAVED: 请先将工程保存到磁盘。");
            if (project.Database is null || project.DbcAssetId is null)
                throw new InvalidOperationException("PROJECT_DBC_REQUIRED");
            if (project.StaleDependencies.Count != 0)
                throw new InvalidOperationException("PROJECT_REFERENCE_STALE");
            var plan = _compiler.Prepare(saved);
            if (!plan.Ready)
                throw new InvalidOperationException("TEST_PREFLIGHT_BLOCKED: " + string.Join("; ", plan.Diagnostics));
            if (!saved.Walk().Any(n => n.Type == "Observe") ||
                !saved.Walk().Any(n => n.Type == "Assert"))
                throw new InvalidOperationException("PHYSICAL_READ_TEST_REQUIRES_OBSERVE_AND_ASSERT");
            if (saved.Walk().Any(n => n.Type is "MockObserve" or "ControllerState"))
                throw new UnauthorizedAccessException("PHYSICAL_READ_ONLY_FORBIDS_MOCK_AND_ACTUATION");
            var controller = _physicalController?.Invoke() ??
                throw new InvalidOperationException("STATION_NOT_INITIALIZED: 请先在 P02 连接主控板。");
            if (_physicalApproval?.Invoke() != true)
                throw new UnauthorizedAccessException("PHYSICAL_TEST_OPERATOR_APPROVAL_DENIED");
            var station = new R2PhysicalCanReadOnlyLabStation(project, controller, true);
            var programAsset = project.Assets.Single(a => a.Id == project.LabProgramAssetId.Value);
            var dbcAsset = project.Assets.Single(a => a.Id == project.DbcAssetId.Value);
            var generation = controller.Generation;
            var programHash = programAsset.Sha256;
            var dbcHash = dbcAsset.Sha256;
            _runGeneration++;
            Facts.Clear();
            Verdict = "RUNNING_PHYSICAL_READ";
            PhysicalEvidencePath = "运行中";
            Busy = true;
            Status = "正在执行当前工程已登记的 TEST · Physical CAN1 Read-Only · 不控制输出";
            try
            {
                var report = await _runner.RunAsync(plan, station, LabSource.Physical, ct);
                foreach (var fact in report.Facts)
                    if (!Facts.Any(x => x.OperationId == fact.OperationId)) Facts.Add(fact);
                if (controller.Generation != generation || !station.IsReadOnlyAuthorized ||
                    project.Assets.Single(a => a.Id == programAsset.Id).Sha256 != programHash ||
                    project.Assets.Single(a => a.Id == dbcAsset.Id).Sha256 != dbcHash)
                    throw new IOException("PHYSICAL_RUN_CONTEXT_CHANGED: 运行期间设备或资源发生变化");
                // A physical diagnostic verdict is never automatically a production PASS.
                Verdict = report.Verdict + " / PHYSICAL_READ_ONLY / NOT_PRODUCTION";
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
                    DeviceGeneration = generation, Source = "PhysicalReadOnly",
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
                Status = "真实 CAN1 只读 TEST 完成 · Run " + report.RunId +
                    " · " + report.Facts.Length + " StepFact · 已保存 JSON 证据 · 非出厂判定";
                return report;
            }
            finally { Busy = false; }
        }
        catch (Exception ex)
        {
            Verdict = "ERROR / NOT_PRODUCTION";
            Status = "真实 TEST 被拒绝或运行失败：" + ex.Message;
            return null;
        }
    }

    private void Control(Action action)
    {
        try { action(); Status = "Run control requested"; }
        catch (Exception ex) { Status = "Control rejected: " + ex.Message; }
    }
    private void Post(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
    private void SaveToProject()
    {
        try
        {
            if (Busy) throw new InvalidOperationException("Cannot publish a program during execution");
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var json = ExportJson();
            if (project.LabProgramAssetId is { } id)
            {
                var asset = project.Assets.Single(x => x.Id == id);
                project.ReplaceLabProgram(json, asset.ToReference());
            }
            else project.ImportLabProgram(json);
            Changed(nameof(ProjectAssetStatus));
            Status = "R2 TEST 已登记为当前工程版本资源；请到 P01 点击【保存工程】写入磁盘。当前仍禁止真实物理执行。";
        }
        catch (Exception ex) { Status = "工程 TEST 登记失败：" + ex.Message; }
    }

    public void ReloadFromProject()
    {
        try
        {
            if (Busy) throw new InvalidOperationException("Cannot reload a program during execution");
            var project = _project ?? throw new InvalidOperationException("PROJECT_NOT_BOUND");
            var program = project.LabProgram ?? LabDocument.New("R2 project TEST program");
            ImportJson(LabDocumentCodec.Save(program));
            Status = project.LabProgram is null
                ? "当前工程尚无 R2 高级测试程序；新建草稿未登记，必须显式纳入工程。"
                : "R2 TEST 已从当前工程已登记资源加载；编辑后需创建新 Revision。";
            Changed(nameof(ProjectAssetStatus));
        }
        catch (Exception ex) { Status = "重新加载工程 TEST 失败：" + ex.Message; }
    }

    private void SaveFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "AutoTest LAB document (*.json)|*.json", FileName = "test-program.json" };
            if (dialog.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dialog.FileName, ExportJson());
            Status = "Saved LAB JSON (not yet PROJECT-managed)";
        }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; }
    }
    private void OpenFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AutoTest LAB document (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            ImportJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) { Status = "Open failed: " + ex.Message; }
    }
    private static LabDocument CreateSample()
    {
        var doc = LabDocument.New("Mock smoke program");
        var prepare = NewNode("SetVariable");
        doc.Phases[LabPhase.Prepare].Add(prepare);
        doc.Phases[LabPhase.Test].Add(NewNode("MockObserve"));
        doc.Phases[LabPhase.Test].Add(NewNode("Assert"));
        doc.Phases[LabPhase.Cleanup].Add(NewNode("Log"));
        return doc;
    }
}
