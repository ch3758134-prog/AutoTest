using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoTest.Core;
using AutoTest.Contracts;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Diagnostics;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Tools.Testing;

public sealed class FormalTestTreeRow
{
    public required string Caption { get; init; }
    public required TestPhase Phase { get; init; }
    public StepId? StepId { get; init; }
    public StepId? ParentId { get; init; }
    public string? Slot { get; init; }
    public int Index { get; init; }
    public ObservableCollection<FormalTestTreeRow> Children { get; } = new();
}

internal sealed class FormalTestCommand(Action run, Func<bool>? enabled = null) : ICommand
{
    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;
    public void Execute(object? parameter) => run();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// R0-DIAG professional test editor. Its only executable document is the official TestDocument.
/// LabDocument/LabRunCoordinator are intentionally not used; integration owns Project context and Shell.
/// </summary>
public sealed class FormalTestWorkspaceViewModel : INotifyPropertyChanged
{
    private StepEditTransaction _editor;
    private readonly PlanCompiler _compiler = new();
    private readonly IAssetResolver? _assetResolver;
    private readonly RunCoordinator _runner = new();
    private readonly FormalTestContext _context;
    private readonly SynchronizationContext? _ui;
    private FormalTestTreeRow? _selected;
    private string _insertType = "MockObserve", _name = "";
    private string _parameterName = "variable", _parameterValue = "";
    private string _status = "Ready · Formal TestDocument · MOCK ONLY · project binding pending";
    private string _verdict = "NOT_RUN", _planHash = "", _activeRunId = "";
    private bool _busy;
    private long _generation;
    private MockRunReport? _lastReport;
    private TestDocument _lastRunDocument;
    private readonly Stopwatch _runClock = new();
    private string _traceFilter = "";
    private bool _redactValuesOnExport = true;
    private readonly string _diagnosticsDirectory;
    private string _lastEvidencePath = "";
    private readonly HashSet<Guid> _observedFactOperations = new();
    private readonly Dictionary<StepId, TestPhase> _phaseForStep = new();

    public FormalTestWorkspaceViewModel() : this(CreateSample()) { }
    public FormalTestWorkspaceViewModel(TestDocument initial, FormalTestContext? context = null,
        string? diagnosticsDirectory = null, IAssetResolver? assetResolver = null)
    {
        _context = context ?? FormalTestContext.Unbound;
        _assetResolver = assetResolver;
        _diagnosticsDirectory = diagnosticsDirectory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "TEST", "Diagnostics");
        _editor = new StepEditTransaction(initial);
        _lastRunDocument = initial;
        _ui = SynchronizationContext.Current;
        _runner.FactProduced += fact =>
        {
            var gen = _generation;
            var observedAt = DateTimeOffset.UtcNow;
            var elapsed = _runClock.ElapsedMilliseconds;
            Post(() => { if (_generation == gen) AppendFact(fact, observedAt, elapsed); });
        };
        _runner.BoundaryPaused += id =>
        {
            var gen = _generation;
            Post(() =>
            {
                if (_generation != gen || !Busy) return;
                Status = $"Paused at step {id}; Resume, Step, Skip or Run to selected";
                AddEvent(TestDiagnosticArchive.Operation(_activeRunId, "BOUNDARY_PAUSED",
                    id.ToString(), monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            });
        };

        InsertCommand = new FormalTestCommand(Insert, () => !Busy);
        DeleteCommand = new FormalTestCommand(() => Edit(() => _editor.Delete(SelectedId())), HasEditableSelection);
        CopyCommand = new FormalTestCommand(Copy, HasEditableSelection);
        UpCommand = new FormalTestCommand(() => Move(-1), HasEditableSelection);
        DownCommand = new FormalTestCommand(() => Move(1), HasEditableSelection);
        ApplyNameCommand = new FormalTestCommand(() => Edit(() => _editor.Rename(SelectedId(), SelectedName)), HasEditableSelection);
        ApplyPropertyCommand = new FormalTestCommand(() => Edit(() => _editor.SetProperty(SelectedId(), ParameterName, ParameterValue)), HasEditableSelection);
        UndoCommand = new FormalTestCommand(() => Edit(_editor.Undo), () => !Busy && _editor.CanUndo);
        RedoCommand = new FormalTestCommand(() => Edit(_editor.Redo), () => !Busy && _editor.CanRedo);
        NewCommand = new FormalTestCommand(() => ReplaceDocument(TestDocument.Create("New test program")), () => !Busy);
        PreflightCommand = new FormalTestCommand(() => Preflight(), () => !Busy);
        RunMockCommand = new FormalTestCommand(() => _ = RunMockAsync(), () => !Busy);
        PauseCommand = new FormalTestCommand(() => Control(_runner.PauseAtBoundary, "PAUSE_REQUESTED"), () => Busy);
        ResumeCommand = new FormalTestCommand(() => Control(_runner.Resume, "RESUME_REQUESTED"), () => Busy);
        StepCommand = new FormalTestCommand(() => Control(_runner.Step, "STEP_REQUESTED"), () => Busy);
        StopCommand = new FormalTestCommand(() => Control(_runner.RequestStop, "STOP_REQUESTED"), () => Busy);
        RunToCommand = new FormalTestCommand(() => Control(() => _runner.RunTo(SelectedId()), "RUN_TO_REQUESTED"),
            () => Busy && _selected?.StepId is not null);
        SkipCommand = new FormalTestCommand(() => Control(() => _runner.Skip(SelectedId()), "SKIP_REQUESTED"),
            () => Busy && _selected?.StepId is not null);
        ToggleBreakpointCommand = new FormalTestCommand(ToggleBreakpoint,
            () => _selected?.StepId is not null);
        SaveCommand = new FormalTestCommand(SaveFile, () => !Busy);
        OpenCommand = new FormalTestCommand(OpenFile, () => !Busy);
        ExportJsonCommand = new FormalTestCommand(ExportTraceJson);
        ExportCsvCommand = new FormalTestCommand(ExportTraceCsv);
        CopyVisibleTraceCommand = new FormalTestCommand(CopyVisibleTrace);
        Rebuild();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public ObservableCollection<FormalTestTreeRow> Tree { get; } = new();
    public ObservableCollection<StepFact> Facts { get; } = new();
    public ObservableCollection<TestDiagnosticEvent> Timeline { get; } = new();
    public ObservableCollection<TestDiagnosticEvent> FilteredTimeline { get; } = new();
    public ObservableCollection<KeyValuePair<string, string>> ParameterRows { get; } = new();
    public ObservableCollection<string> Diagnostics { get; } = new();
    public string[] StepTypes { get; } = StepDescriptorCatalog.Builtins().Enumerate()
        .Select(x => x.Type).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    public string SelectedInsertType { get => _insertType; set { _insertType = value; Changed(); } }
    public string SelectedName { get => _name; set { _name = value; Changed(); } }
    public string ParameterName { get => _parameterName; set { _parameterName = value; Changed(); } }
    public string ParameterValue { get => _parameterValue; set { _parameterValue = value; Changed(); } }
    public string TraceFilter
    {
        get => _traceFilter;
        set { _traceFilter = value ?? ""; Changed(); ApplyTraceFilter(); }
    }
    public bool RedactValuesOnExport
    {
        get => _redactValuesOnExport;
        set { _redactValuesOnExport = value; Changed(); }
    }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Verdict { get => _verdict; private set { _verdict = value; Changed(); } }
    public bool Busy { get => _busy; private set { _busy = value; Changed(); Refresh(); } }
    public string ModeLabel => "SOFTWARE_ONLY / MOCK · Replay NOT_CONNECTED · Physical DISABLED";
    public string DocumentName => _editor.Current.Name;
    public string DocumentIdentity => $"{_editor.Current.Id} · v{_editor.Current.FormatVersion}";
    public string ResourceStatus =>
        $"Project {_context.ProjectId} (rev {_context.ProjectRevision}) · DUT {_context.DutProfile} · " +
        $"Assets {_context.ResourceRevision} · Station {_context.StationId} · Session {_context.SessionGeneration}";
    public string ActiveRunId => _activeRunId;
    public string LastEvidencePath => _lastEvidencePath;
    public string DiagnosticsDirectory => _diagnosticsDirectory;
    public string PlanFingerprint => _planHash;
    public TestDocument Snapshot => _editor.Current;

    public ICommand InsertCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand ApplyNameCommand { get; }
    public ICommand ApplyPropertyCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand PreflightCommand { get; }
    public ICommand RunMockCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StepCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RunToCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand ToggleBreakpointCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand CopyVisibleTraceCommand { get; }

    public void Select(FormalTestTreeRow? row)
    {
        _selected = row;
        ParameterRows.Clear();
        if (row?.StepId is StepId id)
        {
            var node = Snapshot.Walk().FirstOrDefault(n => n.Id == id);
            if (node is not null)
            {
                SelectedName = node.Name;
                foreach (var p in node.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
                    ParameterRows.Add(p);
            }
        }
        else SelectedName = "";
        Refresh();
    }
    private bool HasEditableSelection() => !Busy && _selected?.StepId is not null;
    private StepId SelectedId() => _selected?.StepId ?? throw new InvalidOperationException("Select a step");
    private void Refresh()
    {
        foreach (var command in new[] { InsertCommand, DeleteCommand, CopyCommand, UpCommand, DownCommand,
            ApplyNameCommand, ApplyPropertyCommand, UndoCommand, RedoCommand, NewCommand, PreflightCommand,
            RunMockCommand, PauseCommand, ResumeCommand, StepCommand, StopCommand, RunToCommand,
            SkipCommand, ToggleBreakpointCommand, SaveCommand, OpenCommand, ExportJsonCommand,
            ExportCsvCommand, CopyVisibleTraceCommand })
            (command as FormalTestCommand)?.Refresh();
    }
    private void Rebuild()
    {
        Tree.Clear();
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            var row = new FormalTestTreeRow { Phase = phase, Caption = phase.ToString().ToUpperInvariant() };
            AddRows(row.Children, Snapshot.Get(phase), phase, null, null);
            Tree.Add(row);
        }
        _selected = null;
        ParameterRows.Clear();
        SelectedName = "";
        Changed(nameof(DocumentName));
        Changed(nameof(DocumentIdentity));
        Refresh();
    }
    private void AddRows(ObservableCollection<FormalTestTreeRow> rows,
        IEnumerable<StepNode> nodes, TestPhase phase, StepId? parent, string? slot)
    {
        var index = 0;
        foreach (var n in nodes)
        {
            var mark = _runner.HasBreakpoint(n.Id) ? "● " : "";
            var row = new FormalTestTreeRow
            {
                Phase = phase, Caption = $"{mark}{index + 1:D2}  {n.Type}  ·  {n.Name}",
                StepId = n.Id, ParentId = parent, Slot = slot, Index = index
            };
            foreach (var branch in n.Slots.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                var branchRow = new FormalTestTreeRow
                {
                    Phase = phase, Caption = branch.Key.ToUpperInvariant(), ParentId = n.Id,
                    Slot = branch.Key
                };
                AddRows(branchRow.Children, branch.Value, phase, n.Id, branch.Key);
                row.Children.Add(branchRow);
            }
            rows.Add(row);
            index++;
        }
    }
    private void Edit(Action mutate)
    {
        if (Busy) { Status = "Edit blocked during run"; return; }
        try
        {
            mutate();
            Rebuild();
            Status = "Edited · unsaved · execution snapshot will be created at Preflight";
        }
        catch (Exception ex)
        {
            Status = "Edit rejected: " + ex.Message;
            Diagnostics.Add(Status);
        }
    }
    private static StepNode NewStep(string type)
    {
        var n = StepNode.Create(type);
        return type switch
        {
            "Sequence" => n.WithSlot("body"),
            "If" => n.WithProperty("variable", "mode").WithProperty("equals", "run")
                .WithSlot("then").WithSlot("else"),
            "Loop" => n.WithProperty("count", "2").WithSlot("body"),
            "SetVariable" => n.WithProperty("variable", "mode").WithProperty("value", "run"),
            "MockObserve" => n.WithProperty("variable", "result").WithProperty("value", "ready")
                .WithProperty("quality", "Observed"),
            "Assert" => n.WithProperty("variable", "result").WithProperty("equals", "ready"),
            "Wait" => n.WithProperty("milliseconds", "100"),
            "Switch" => n.WithProperty("variable", "mode").WithSlot("cases"),
            "Case" => n.WithProperty("equals", "run").WithSlot("body"),
            "Default" => n.WithSlot("body"),
            "Log" or "Fail" => n.WithProperty("message", "Mock diagnostic message"),
            "ControllerState" => n.WithProperty("controllerId", "")
                .WithProperty("stateId", "").WithProperty("revision", ""),
            "When" => n.WithProperty("variable", "mode").WithProperty("equals", "run")
                .WithProperty("timeoutMs", "1000").WithSlot("body"),
            "TryCatch" => n.WithSlot("try").WithSlot("catch").WithSlot("finally"),
            "Subroutine" => n.WithProperty("name", "subroutine").WithSlot("body"),
            "Parallel" => n.WithSlot("branches"),
            "CallSubroutine" => n.WithProperty("target", ""),
            _ => n
        };
    }
    private void Insert()
    {
        var row = _selected;
        var phase = row?.Phase ?? TestPhase.Test;
        var parent = row?.ParentId;
        var slot = row?.Slot;
        var index = row?.StepId is null
            ? parent is null ? Snapshot.Get(phase).Length
                : Snapshot.Walk().First(n => n.Id == parent).Slots[slot!].Length
            : row.Index + 1;
        if (row?.StepId is StepId selected)
        {
            var node = Snapshot.Walk().First(n => n.Id == selected);
            if (!node.Slots.IsEmpty)
            {
                parent = selected;
                slot = node.Slots.ContainsKey("body") ? "body" :
                    node.Slots.ContainsKey("then") ? "then" : node.Slots.Keys.OrderBy(x => x).First();
                index = node.Slots[slot].Length;
            }
        }
        Edit(() => _editor.Insert(phase, parent, slot, index, NewStep(SelectedInsertType)));
    }
    private void Copy()
    {
        if (_selected is not { StepId: StepId id } row) return;
        Edit(() => _editor.Copy(id, row.Phase, row.ParentId, row.Slot, row.Index + 1));
    }
    private void Move(int delta)
    {
        if (_selected is not { StepId: StepId id } row) return;
        var count = row.ParentId is null ? Snapshot.Get(row.Phase).Length
            : Snapshot.Walk().First(n => n.Id == row.ParentId).Slots[row.Slot!].Length;
        var target = delta < 0 ? row.Index - 1 : row.Index + 2;
        if (target < 0 || (delta > 0 && row.Index >= count - 1))
        {
            Status = "Step is at list boundary";
            return;
        }
        Edit(() => _editor.Move(id, row.Phase, row.ParentId, row.Slot, target));
    }
    private void ToggleBreakpoint()
    {
        try
        {
            var id = SelectedId();
            var newValue = !_runner.HasBreakpoint(id);
            _runner.SetBreakpoint(id, newValue);
            Rebuild();
            Status = newValue ? $"Breakpoint enabled: {id}" : $"Breakpoint removed: {id}";
        }
        catch (Exception ex) { Status = "Breakpoint rejected: " + ex.Message; }
    }
    public string ExportDocumentJson() => TestDocumentCodec.Serialize(Snapshot);
    public void ImportDocumentJson(string json)
    {
        if (Busy) throw new InvalidOperationException("Cannot load a document during a run");
        // Parsing before replacing protects the current edit/undo history on malformed input.
        var parsed = TestDocumentCodec.Deserialize(json);
        ReplaceDocument(parsed);
    }
    private void ReplaceDocument(TestDocument document)
    {
        if (Busy) throw new InvalidOperationException("Cannot replace a running document");
        _editor = new StepEditTransaction(document);
        Rebuild();
        Status = "Formal TestDocument loaded · file-only until PROJECT provides a resource adapter";
    }
    private void ResetEvidenceForAttempt()
    {
        _generation++;
        _lastRunDocument = Snapshot;
        _lastReport = null;
        _activeRunId = "";
        _planHash = "";
        _runClock.Reset();
        _lastEvidencePath = "";
        Changed(nameof(LastEvidencePath));
        Facts.Clear();
        _observedFactOperations.Clear();
        _phaseForStep.Clear();
        Timeline.Clear();
        FilteredTimeline.Clear();
        Diagnostics.Clear();
        Verdict = "NOT_RUN";
        Changed(nameof(ActiveRunId));
        Changed(nameof(PlanFingerprint));
    }
    public string Preflight()
    {
        if (Busy) return "Preflight blocked: active run";
        ResetEvidenceForAttempt();
        try
        {
            var plan = _compiler.Prepare(Snapshot, assetResolver: _assetResolver);
            _planHash = plan.Fingerprint;
            Changed(nameof(PlanFingerprint));
            foreach (var issue in plan.Diagnostics) Diagnostics.Add(issue);
            if (plan.Diagnostics.IsEmpty)
            {
                Status = $"Preflight READY · SHA256 {_planHash[..12]} · Mock-only";
                AddEvent(TestDiagnosticArchive.Operation("", "PREFLIGHT_READY", _planHash));
            }
            else
            {
                Verdict = "BLOCKED";
                Status = $"Preflight BLOCKED · {plan.Diagnostics.Length} diagnostic(s) · {plan.Diagnostics[0]}";
                foreach (var issue in plan.Diagnostics)
                    AddEvent(TestDiagnosticArchive.Operation("", "PREFLIGHT_BLOCKED", issue));
            }
        }
        catch (Exception ex)
        {
            Verdict = "ERROR";
            Diagnostics.Add(ex.ToString());
            Status = "Preflight exception: " + ex.Message;
            AddEvent(TestDiagnosticArchive.Operation("", "PREFLIGHT_ERROR", ex.ToString()));
        }
        TryAutoSaveEvidence();
        return Status;
    }
    public async Task RunMockAsync()
    {
        if (Busy) return;
        ResetEvidenceForAttempt();
        PreparedTestPlan plan;
        try
        {
            plan = _compiler.Prepare(Snapshot, assetResolver: _assetResolver);
            _planHash = plan.Fingerprint;
            Changed(nameof(PlanFingerprint));
            foreach (var issue in plan.Diagnostics) Diagnostics.Add(issue);
            if (!plan.Diagnostics.IsEmpty)
            {
                Verdict = "BLOCKED";
                Status = $"Preflight BLOCKED · {plan.Diagnostics.Length} issue(s)";
                foreach (var issue in plan.Diagnostics)
                    AddEvent(TestDiagnosticArchive.Operation("", "PREFLIGHT_BLOCKED", issue));
                TryAutoSaveEvidence();
                return;
            }
        }
        catch (Exception ex)
        {
            Verdict = "ERROR";
            Status = "Run preflight error: " + ex.Message;
            Diagnostics.Add(ex.ToString());
            AddEvent(TestDiagnosticArchive.Operation("", "PREFLIGHT_ERROR", ex.ToString()));
            TryAutoSaveEvidence();
            return;
        }
        // Build a bounded StepId/Phase index once for this immutable, validated snapshot.
        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var node in TestDocument.WalkNodes(_lastRunDocument.Get(phase)))
                _phaseForStep[node.Id] = phase;
        var run = RunId.New();
        _activeRunId = run.ToString();
        Changed(nameof(ActiveRunId));
        _runClock.Restart();
        Verdict = "RUNNING";
        Busy = true;
        AddEvent(TestDiagnosticArchive.Operation(_activeRunId, "RUN_STARTED",
            "Formal snapshot pinned; software-only Mock; no real station/ECU action",
            monotonicOffsetMs: 0));
        Status = $"RUNNING MOCK · {_activeRunId}";
        try
        {
            // A new station per run prevents an old RunId's observations from leaking.
            var report = await _runner.StartAsync(plan, new MockStation(), requestedRun: run);
            _lastReport = report;
            foreach (var fact in report.Facts) AppendFact(fact);
            Verdict = report.Verdict.ToString().ToUpperInvariant();
            AddEvent(TestDiagnosticArchive.Operation(_activeRunId,
                report.Stopped ? "RUN_STOPPED" : "RUN_COMPLETED",
                $"Verdict={Verdict}; Stopped={report.Stopped}; Facts={report.Facts.Length}",
                monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            Status = $"MOCK {Verdict} · RunId {_activeRunId} · {report.Facts.Length} facts · not production evidence";
        }
        catch (Exception ex)
        {
            Verdict = "ERROR";
            AddEvent(TestDiagnosticArchive.Operation(_activeRunId, "RUN_ERROR", ex.ToString(),
                monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            Diagnostics.Add(ex.ToString());
            Status = "MOCK ERROR · " + ex.Message;
        }
        finally
        {
            _runClock.Stop();
            Busy = false;
            TryAutoSaveEvidence();
        }
    }
    private void AppendFact(StepFact fact, DateTimeOffset? timestamp = null,
        long? elapsedMs = null)
    {
        if (!_observedFactOperations.Add(fact.OperationId)) return;
        Facts.Add(fact);
        var phase = _phaseForStep.TryGetValue(fact.StepId, out var knownPhase)
            ? knownPhase.ToString() : "Runtime";
        AddEvent(TestDiagnosticArchive.FromFact(_activeRunId, phase, fact, timestamp, elapsedMs));
    }
    private void AddEvent(TestDiagnosticEvent entry)
    {
        Timeline.Add(entry);
        if (MatchesFilter(entry)) FilteredTimeline.Add(entry);
    }
    private bool MatchesFilter(TestDiagnosticEvent entry)
    {
        if (string.IsNullOrWhiteSpace(TraceFilter)) return true;
        return new[] { entry.EventKind, entry.Source, entry.RunId, entry.StepId,
                entry.OperationId, entry.Phase, entry.Type, entry.Expected, entry.Actual,
                entry.Quality, entry.Verdict, entry.Diagnostic, entry.RawMockFactJson }
            .Any(x => x?.Contains(TraceFilter, StringComparison.OrdinalIgnoreCase) == true);
    }
    private void ApplyTraceFilter()
    {
        FilteredTimeline.Clear();
        foreach (var entry in Timeline.Where(MatchesFilter)) FilteredTimeline.Add(entry);
    }
    public string VisibleTraceText() => JsonSerializer.Serialize(
        CreateEvidenceBundle() with { Timeline = FilteredTimeline.ToArray() },
        new JsonSerializerOptions { WriteIndented = true });
    private void CopyVisibleTrace()
    {
        try
        {
            System.Windows.Clipboard.SetText(VisibleTraceText());
            Status = $"Copied {FilteredTimeline.Count} visible event(s) · " +
                (RedactValuesOnExport ? "redacted" : "UNREDACTED — do not publish without review");
        }
        catch (Exception ex) { Status = "Clipboard failed: " + ex.Message; Diagnostics.Add(ex.ToString()); }
    }
    private void Control(Action command, string kind)
    {
        try
        {
            command();
            AddEvent(TestDiagnosticArchive.Operation(_activeRunId, kind,
                _selected?.StepId?.ToString() ?? "", monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            Status = kind + " accepted at Mock engine boundary; no physical effect";
        }
        catch (Exception ex)
        {
            AddEvent(TestDiagnosticArchive.Operation(_activeRunId, "CONTROL_REJECTED",
                ex.Message, monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            Status = "Control rejected: " + ex.Message;
        }
    }
    private void Post(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
    private TestDiagnosticBundle BuildEvidenceBundle(bool redact) =>
        TestDiagnosticArchive.Build(_lastReport is null ? Snapshot : _lastRunDocument,
            _lastReport?.PlanFingerprint ?? _planHash, _lastReport, Timeline.ToArray(),
            hostBuildGitSha: _context.HostBuildGitSha, projectRevision: _context.ProjectRevision,
            redactValues: redact, projectId: _context.ProjectId,
            dutProfile: _context.DutProfile, stationId: _context.StationId,
            sessionGeneration: _context.SessionGeneration, resourceRevision: _context.ResourceRevision,
            preflightDiagnostics: Diagnostics.ToArray()) with { AttemptStatus = Verdict };

    public TestDiagnosticBundle CreateEvidenceBundle() => BuildEvidenceBundle(RedactValuesOnExport) with
    {
        EvidenceCompleteness = string.IsNullOrEmpty(_lastEvidencePath)
            ? "IN_MEMORY_ONLY_NOT_DURABLE" : "REDACTED_LOCAL_BEST_EFFORT_SAVED"
    };

    private void TryAutoSaveEvidence()
    {
        try
        {
            // Auto-save is ALWAYS redacted, irrespective of manual export preferences.
            // This is a TEST-private diagnostic snapshot, not a Records/Production commit.
            var safe = BuildEvidenceBundle(redact: true) with
                { EvidenceCompleteness = "LOCAL_BEST_EFFORT_SNAPSHOT" };
            _lastEvidencePath = TestDiagnosticArchiveWriter.Save(_diagnosticsDirectory, safe);
            Changed(nameof(LastEvidencePath));
        }
        catch (Exception ex)
        {
            _lastEvidencePath = "";
            Changed(nameof(LastEvidencePath));
            var error = "TEST_TRACE_PERSIST_FAILED: " + ex;
            Diagnostics.Add(error);
            AddEvent(TestDiagnosticArchive.Operation(_activeRunId, "TRACE_PERSIST_ERROR", error,
                monotonicOffsetMs: _runClock.ElapsedMilliseconds));
            Status += " · TRACE SAVE FAILED; export manually";
        }
    }

    public string ExportEvidenceJson() => TestDiagnosticArchive.ToJson(CreateEvidenceBundle());
    public string ExportEvidenceCsv() => TestDiagnosticArchive.ToCsv(CreateEvidenceBundle());
    private void SaveFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            { Filter = "AutoTest TestDocument (*.json)|*.json", FileName = "test-program.json" };
            if (dialog.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dialog.FileName, ExportDocumentJson());
            Status = "Saved formal TestDocument JSON · no PROJECT revision was assigned";
        }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; Diagnostics.Add(ex.ToString()); }
    }
    private void OpenFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            { Filter = "AutoTest TestDocument (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            ImportDocumentJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) { Status = "Open failed: " + ex.Message; Diagnostics.Add(ex.ToString()); }
    }
    private void ExportTraceJson() => ExportTrace("AutoTest TEST evidence (*.json)|*.json",
        "test-mock-trace.json", ExportEvidenceJson());
    private void ExportTraceCsv() => ExportTrace("AutoTest TEST facts (*.csv)|*.csv",
        "test-mock-trace.csv", ExportEvidenceCsv());
    private void ExportTrace(string filter, string name, string text)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name };
            if (dialog.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dialog.FileName, text, System.Text.Encoding.UTF8);
            Status = $"Exported Mock evidence to {dialog.FileName} · review values before sharing";
        }
        catch (Exception ex) { Status = "Export failed: " + ex.Message; Diagnostics.Add(ex.ToString()); }
    }
    private static TestDocument CreateSample()
    {
        var d = TestDocument.Create("Formal TEST diagnostic sample");
        var edit = new StepEditTransaction(d);
        edit.Insert(TestPhase.Prepare, null, null, 0, NewStep("SetVariable"));
        edit.Insert(TestPhase.Test, null, null, 0, NewStep("MockObserve"));
        edit.Insert(TestPhase.Test, null, null, 1, NewStep("Assert"));
        edit.Insert(TestPhase.Cleanup, null, null, 0, NewStep("SetVariable"));
        return edit.Current;
    }
}
