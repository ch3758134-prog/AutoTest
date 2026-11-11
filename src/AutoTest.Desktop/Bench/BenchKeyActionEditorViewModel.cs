using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Bench;

public sealed class BenchKeyActionItem : BindableBase
{
    private string _name;
    private bool _isSelected;
    // A card reflects the currently selected action without a second UI-only action list.
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public string Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ObservableCollection<BenchKeyStepItem> Steps { get; } = new();
    public BenchKeyActionItem(string id, string name) { Id = id; _name = name; }
}

public sealed class BenchKeyStepItem : BindableBase
{
    private uint _mask;
    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) RaisePropertyChanged(nameof(Summary)); } }
    private int _durationMs;
    private string _durationMsText;
    public BenchActionStepKind Kind { get; }
    public uint Mask { get => _mask; set { if (SetProperty(ref _mask, value)) RaisePropertyChanged(nameof(Summary)); } }
    // Keep an int setter for programmatic loading/tests, but the WPF editor
    // uses a string. An invalid text value must NEVER silently run the old int.
    public int DurationMs
    {
        get => _durationMs;
        set
        {
            if (SetProperty(ref _durationMs, value))
                DurationMsText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    public string DurationMsText
    {
        get => _durationMsText;
        set { if (SetProperty(ref _durationMsText, value)) RaisePropertyChanged(nameof(Summary)); }
    }
    public string Summary
    {
        get
        {
            var description = Kind == BenchActionStepKind.SetKeys
                ? (Mask == 0 ? "全部断开" :
                    "闭合 " + string.Join("、", Enumerable.Range(0, 20)
                        .Where(i => (Mask & (1u << i)) != 0).Select(i => $"K{i + 1:00}")))
                : $"保持 {DurationMsText} 毫秒";
            return string.IsNullOrWhiteSpace(Name) ? description : Name.Trim() + " · " + description;
        }
    }
    public BenchKeyStepItem(BenchActionStep step)
    {
        Kind = step.Kind;
        _mask = step.Mask;
        _name = step.Name ?? "";
        _durationMs = step.DurationMs;
        _durationMsText = step.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public BenchActionStep ToStep()
    {
        if (Kind == BenchActionStepKind.SetKeys) return BenchActionStep.SetKeys(Mask, Name.Trim());
        if (!int.TryParse(DurationMsText, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var milliseconds))
            throw new ArgumentException("保持时间必须是 1～600000 的整数毫秒；未执行旧值。");
        return BenchActionStep.Hold(milliseconds, Name.Trim());
    }
}

// Editor-only state. Checked Key bits never send hardware themselves.
// Persisted action sequences are project resources; execution uses the existing DEVICE session.
public sealed class BenchKeyActionEditorViewModel : BindableBase
{
    private readonly IBenchWorkbenchPort _port;
    private readonly Action<string> _audit;
    private BenchKeyActionItem? _selectedAction;
    private BenchKeyStepItem? _selectedStep;
    private bool _dirty;
    private bool _running;
    private bool _working;
    private bool _loadingSwitches;
    private string _selectedStepResult = "选中开关组合步骤后，可试发这一组开关，或执行完整动作。";
    public string SelectedStepResult
    { get => _selectedStepResult; private set => SetProperty(ref _selectedStepResult, value); }
    private string? _editorProjectId;
    private bool DifferentProject => _editorProjectId != _port.ActiveProjectId;
    private const string ProjectChangedMessage =
        "当前工程已切换：不能把旧工程按键动作发给新 ECU。请返回原工程保存草稿，或取消旧草稿后重新加载。";
    private string _status = "尚未加载工程动作";
    private string _progress = "尚未执行";
    private CancellationTokenSource? _execution;

    public BenchKeyActionEditorViewModel(IBenchWorkbenchPort port, Action<string>? audit = null)
    {
        _port = port;
        _audit = audit ?? (_ => { });
        foreach (var n in Enumerable.Range(0, 20))
        {
            var item = new BenchSwitchItem(n);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BenchSwitchItem.IsSelected) && !_loadingSwitches &&
                    SelectedStep is { Kind: BenchActionStepKind.SetKeys } selected)
                {
                    selected.Mask = CurrentMask;
                    MarkDirty();
                }
            };
            Switches.Add(item);
        }
        NewActionCommand = new DelegateCommand(NewAction);
        CopyActionCommand = new DelegateCommand(CopyAction);
        DeleteActionCommand = new DelegateCommand(DeleteAction);
        AddSetKeysCommand = new DelegateCommand(() =>
            AddStep(BenchActionStep.SetKeys(0, "开关组合 " +
                (Steps.Count(x => x.Kind == BenchActionStepKind.SetKeys) + 1))));
        AddHoldCommand = new DelegateCommand(() =>
            AddStep(BenchActionStep.Hold(100, "保持 " +
                (Steps.Count(x => x.Kind == BenchActionStepKind.Hold) + 1))));
        RemoveStepCommand = new DelegateCommand(RemoveStep);
        MoveStepUpCommand = new DelegateCommand(() => MoveStep(-1));
        MoveStepDownCommand = new DelegateCommand(() => MoveStep(+1));
        ClearMaskCommand = new DelegateCommand(() =>
        { foreach (var sw in Switches) sw.IsSelected = false; });
        SaveActionsCommand = new DelegateCommand(async () => await SaveAsync());
        ReloadActionsCommand = new DelegateCommand(async () => await LoadAsync());
        RunActionCommand = new DelegateCommand(async () => await RunAsync());
        StopActionCommand = new DelegateCommand(() => _execution?.Cancel());
        SendSelectedStepCommand = new DelegateCommand(async () => await SendSelectedStepAsync());
    }

    public ObservableCollection<BenchKeyActionItem> Actions { get; } = new();
    public ObservableCollection<BenchKeyStepItem> Steps { get; } = new();
    public ObservableCollection<BenchSwitchItem> Switches { get; } = new();

    public DelegateCommand NewActionCommand { get; }
    public DelegateCommand CopyActionCommand { get; }
    public DelegateCommand DeleteActionCommand { get; }
    public DelegateCommand AddSetKeysCommand { get; }
    public DelegateCommand AddHoldCommand { get; }
    public DelegateCommand RemoveStepCommand { get; }
    public DelegateCommand MoveStepUpCommand { get; }
    public DelegateCommand MoveStepDownCommand { get; }
    public DelegateCommand ClearMaskCommand { get; }
    public DelegateCommand SaveActionsCommand { get; }
    public DelegateCommand ReloadActionsCommand { get; }
    public DelegateCommand RunActionCommand { get; }
    public DelegateCommand StopActionCommand { get; }
    public DelegateCommand SendSelectedStepCommand { get; }
    public bool HasUnsavedChanges => _dirty;
    public bool IsActionRunning => _running;
    public bool CanEdit => !_running && !_working;
    public bool IsKeyStep => SelectedStep?.Kind == BenchActionStepKind.SetKeys;
    public bool IsHoldStep => SelectedStep?.Kind == BenchActionStepKind.Hold;
    public bool HasSelectedStep => SelectedStep is not null;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public uint CurrentMask => Switches.Aggregate(0u, (mask, sw) =>
        sw.IsSelected ? mask | sw.Bit : mask);

    public BenchKeyActionItem? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value)) return;
            foreach (var action in Actions)
                action.IsSelected = ReferenceEquals(action, value);
            Steps.Clear();
            if (value is not null)
                foreach (var step in value.Steps) Steps.Add(step);
            SelectedStep = Steps.FirstOrDefault();
        }
    }
    public BenchKeyStepItem? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!SetProperty(ref _selectedStep, value)) return;
            _loadingSwitches = true;
            try
            {
                foreach (var sw in Switches)
                    sw.IsSelected = value is { Kind: BenchActionStepKind.SetKeys } &&
                        (value.Mask & sw.Bit) != 0;
            }
            finally { _loadingSwitches = false; }
            RaisePropertyChanged(nameof(IsKeyStep));
            RaisePropertyChanged(nameof(IsHoldStep));
            RaisePropertyChanged(nameof(HasSelectedStep));
            SelectedStepResult = value is { Kind: BenchActionStepKind.SetKeys }
                ? "可试发当前选中的开关组合；仅选择步骤或勾选开关不会发送。"
                : "当前没有选中可单独发送的开关步骤。";
        }
    }

    private void Track(BenchKeyActionItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BenchKeyActionItem.Name)) MarkDirty();
        };
        foreach (var step in item.Steps) Track(step);
    }
    private void Track(BenchKeyStepItem step) =>
        step.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(BenchKeyStepItem.Mask)
                or nameof(BenchKeyStepItem.DurationMsText)
                or nameof(BenchKeyStepItem.Name))
                MarkDirty();
        };

    private void MarkDirty()
    {
        if (_dirty || _working) return;
        _dirty = true;
        RaisePropertyChanged(nameof(HasUnsavedChanges));
        Status = "按键配置已修改；可立即试运行，确认效果后再保存到工程。";
    }
    private void SetClean()
    {
        _dirty = false;
        RaisePropertyChanged(nameof(HasUnsavedChanges));
    }
    private void NewAction()
    {
        if (!CanEdit) return;
        var name = "新动作";
        for (var i = 2; Actions.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"新动作 {i}";
        var action = new BenchKeyActionItem(Guid.NewGuid().ToString("D"), name);
        // One editable 20-switch step is available immediately after "New action".
        action.Steps.Add(new BenchKeyStepItem(BenchActionStep.SetKeys(0, "开关组合 1")));
        Track(action);
        Actions.Add(action);
        SelectedAction = action;
        MarkDirty();
    }
    private void CopyAction()
    {
        if (!CanEdit || SelectedAction is not { } source) return;
        var copy = new BenchKeyActionItem(Guid.NewGuid().ToString("D"), source.Name + " 副本");
        while (Actions.Any(a => a.Name.Equals(copy.Name, StringComparison.OrdinalIgnoreCase)))
            copy.Name += " 副本";
        foreach (var step in source.Steps)
        {
            var cloned = new BenchKeyStepItem(new(step.Kind, step.Mask, step.DurationMs, step.Name));
            if (step.Kind == BenchActionStepKind.Hold)
                cloned.DurationMsText = step.DurationMsText; // preserve visible draft, even if invalid
            copy.Steps.Add(cloned);
        }
        Track(copy);
        Actions.Add(copy);
        SelectedAction = copy;
        MarkDirty();
    }
    private void DeleteAction()
    {
        if (!CanEdit || SelectedAction is not { } selected) return;
        Actions.Remove(selected);
        SelectedAction = Actions.FirstOrDefault();
        MarkDirty();
    }
    private void AddStep(BenchActionStep step)
    {
        if (!CanEdit || SelectedAction is null) return;
        var item = new BenchKeyStepItem(step);
        Track(item);
        var next = SelectedStep is not null ? Steps.IndexOf(SelectedStep) + 1 : Steps.Count;
        SelectedAction.Steps.Insert(next, item);
        Steps.Insert(next, item);
        SelectedStep = item;
        MarkDirty();
    }
    private void RemoveStep()
    {
        if (!CanEdit || SelectedAction is null || SelectedStep is not { } step) return;
        var index = Steps.IndexOf(step);
        SelectedAction.Steps.Remove(step);
        Steps.Remove(step);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
        MarkDirty();
    }
    private void MoveStep(int delta)
    {
        if (!CanEdit || SelectedAction is null || SelectedStep is not { } step) return;
        var index = Steps.IndexOf(step);
        var target = index + delta;
        if (target < 0 || target >= Steps.Count) return;
        Steps.Move(index, target);
        SelectedAction.Steps.Move(index, target);
        MarkDirty();
    }

    /// <summary>
    /// Experimental single-step send uses the same verified physical DEVICE
    /// adapter as the full action. Selecting/editing any checkbox sends nothing.
    /// A zero-mask step is an explicit all-open request, never an implicit reset.
    /// </summary>
    public async Task SendSelectedStepAsync()
    {
        if (!CanEdit || SelectedStep is not { Kind: BenchActionStepKind.SetKeys } step) return;
        if (DifferentProject) { Status = ProjectChangedMessage; return; }
        var mask = step.Mask; // immutable command snapshot
        var stepName = string.IsNullOrWhiteSpace(step.Name) ? "当前开关组合" : step.Name.Trim();
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        SelectedStepResult = $"正在试发“{stepName}”……";
        try
        {
            var result = mask == 0
                ? await _port.ReleaseAllPhysicalKeysAsync(CancellationToken.None)
                : await _port.ApplyPhysicalKeyMaskAsync(mask, CancellationToken.None);
            SelectedStepResult = result.Accepted
                ? $"主控已接收“{stepName}”开关指令；请观察 ECU CAN 报文，实际 5 路电压未测量。"
                : $"“{stepName}”未确认发出：{result.ErrorMessage ?? result.ErrorCode ?? "状态未知"}。";
            _audit(SelectedStepResult);
        }
        catch (Exception e)
        {
            SelectedStepResult = $"“{stepName}”试发异常：{e.Message}；硬件实际状态未知。";
        }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    public async Task DiscardDraftAndReloadAsync()
    {
        if (_running || _working) return;
        // Call only following an explicit confirmation in the WPF view.
        SelectedAction = null;
        Actions.Clear();
        SetClean();
        _editorProjectId = null;
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_running || _working) return;
        if (_dirty && DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        if (_dirty)
        {
            Status = "存在未保存修改。请先保存；重新加载不会覆盖草稿。";
            return;
        }
        if (DifferentProject)
        {
            // No local edits to lose. Drop previously loaded ECU actions before
            // touching another project's store, including unsaved projects.
            SelectedAction = null;
            Actions.Clear();
            _editorProjectId = _port.ActiveProjectId;
        }
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        try
        {
            var editingProjectId = _port.ActiveProjectId;
            var loaded = await _port.LoadKeyActionsAsync(CancellationToken.None);
            if (editingProjectId != _port.ActiveProjectId)
                throw new InvalidOperationException("加载期间工程已切换，已拒绝替换编辑器内容。");
            _editorProjectId = editingProjectId;
            BenchActionValidation.ValidateAll(loaded);
            Actions.Clear();
            SelectedAction = null;
            foreach (var action in loaded)
            {
                var item = new BenchKeyActionItem(action.Id, action.Name);
                foreach (var step in action.Steps)
                    item.Steps.Add(new BenchKeyStepItem(step));
                Track(item);
                Actions.Add(item);
            }
            SelectedAction = Actions.FirstOrDefault();
            SetClean();
            Status = $"已加载当前工程的 {Actions.Count} 个按键动作";
        }
        catch (InvalidOperationException e) when (
            e.Message.Contains("请先创建并保存正式工程", StringComparison.Ordinal) ||
            e.Message.Contains("工程未保存到有效", StringComparison.Ordinal))
        {
            // An unsaved PROJECT is a valid exploratory bench workspace:
            // allow temporary key actions and real trials, but the project store
            // still rejects persistence until the project itself is saved.
            if (Actions.Count == 0 && !_dirty)
            {
                _editorProjectId = _port.ActiveProjectId;
                SetClean();
                Status = "工程尚未保存：可新建按键按钮并试运行；要保存动作配置，请先保存工程。";
            }
            else Status = "当前工程动作无法读取：" + e.Message;
        }
        catch (Exception e) { Status = "加载工程动作失败：" + e.Message; }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    private BenchKeyAction Snapshot(BenchKeyActionItem item) =>
        new(item.Id, item.Name.Trim(), item.Steps.Select(s => s.ToStep()).ToArray());

    public async Task SaveAsync()
    {
        if (_running || _working) return;
        if (DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        _working = true;
        RaisePropertyChanged(nameof(CanEdit));
        try
        {
            var actions = Actions.Select(Snapshot).ToArray();
            BenchActionValidation.ValidateAll(actions);
            await _port.SaveKeyActionsAsync(actions, CancellationToken.None);
            SetClean();
            Status = $"已保存 {actions.Length} 个按键动作到当前工程";
            _audit(Status);
        }
        catch (Exception e) { Status = "保存失败，草稿仍保留：" + e.Message; }
        finally { _working = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    public async Task RunAsync()
    {
        if (!CanEdit || SelectedAction is not { } selected) return;
        if (DifferentProject)
        {
            Status = ProjectChangedMessage;
            return;
        }
        // Trial runs use a validated frozen snapshot; persistence is optional.
        BenchKeyAction action;
        try
        {
            action = Snapshot(selected);
            BenchActionValidation.Validate(action);
        }
        catch (Exception e) { Status = "动作不合法：" + e.Message; return; }
        _execution = new CancellationTokenSource();
        _running = true;
        RaisePropertyChanged(nameof(IsActionRunning));
        RaisePropertyChanged(nameof(CanEdit));
        Progress = $"{(_dirty ? "未保存配置试运行" : "开始真实动作")}：{action.Name} / {action.Steps.Count} 步";
        _audit(Progress);
        try
        {
            var report = new Progress<BenchActionProgress>(p =>
            {
                Progress = $"步骤 {p.StepNumber}/{p.StepCount} · {p.Description}";
                _audit(Progress);
            });
            var result = await _port.RunKeyActionAsync(action, report, _execution.Token);
            Progress = result.Detail;
            Status = result.Completed ? "真实动作执行结束（不是 ECU 判定通过）" :
                result.Cancelled ? "用户停止；最后的实际输出状态未知" :
                "真实动作失败或被拒绝";
            _audit($"{action.Name} · {Status} · {result.Detail}");
        }
        catch (OperationCanceledException)
        {
            Status = "执行已取消；未自动释放按键";
            Progress = "物理状态 UNKNOWN，请检查实际输出。";
            _audit(Status);
        }
        catch (Exception e)
        {
            Status = "动作执行异常，实际输出 UNKNOWN";
            Progress = e.Message;
            _audit(Status + " · " + e.Message);
        }
        finally
        {
            _execution.Dispose();
            _execution = null;
            _running = false;
            RaisePropertyChanged(nameof(IsActionRunning));
            RaisePropertyChanged(nameof(CanEdit));
        }
    }
}
