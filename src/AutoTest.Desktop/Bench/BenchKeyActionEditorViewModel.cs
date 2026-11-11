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
    public string Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ObservableCollection<BenchKeyStepItem> Steps { get; } = new();
    public BenchKeyActionItem(string id, string name) { Id = id; _name = name; }
}

public sealed class BenchKeyStepItem : BindableBase
{
    private uint _mask;
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
    public string Summary => Kind == BenchActionStepKind.SetKeys
        ? Mask == 0
            ? "切换按键：全部断开"
            : "切换按键：闭合 " + string.Join("、", Enumerable.Range(0, 20)
                .Where(i => (Mask & (1u << i)) != 0).Select(i => $"K{i + 1:00}"))
        : $"保持当前按键组合 {DurationMsText} 毫秒";
    public BenchKeyStepItem(BenchActionStep step)
    {
        Kind = step.Kind;
        _mask = step.Mask;
        _durationMs = step.DurationMs;
        _durationMsText = step.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public BenchActionStep ToStep()
    {
        if (Kind == BenchActionStepKind.SetKeys) return BenchActionStep.SetKeys(Mask);
        if (!int.TryParse(DurationMsText, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var milliseconds))
            throw new ArgumentException("保持时间必须是 1～600000 的整数毫秒；未执行旧值。");
        return BenchActionStep.Hold(milliseconds);
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
        AddSetKeysCommand = new DelegateCommand(() => AddStep(BenchActionStep.SetKeys(0)));
        AddHoldCommand = new DelegateCommand(() => AddStep(BenchActionStep.Hold(100)));
        RemoveStepCommand = new DelegateCommand(RemoveStep);
        MoveStepUpCommand = new DelegateCommand(() => MoveStep(-1));
        MoveStepDownCommand = new DelegateCommand(() => MoveStep(+1));
        ClearMaskCommand = new DelegateCommand(() =>
        { foreach (var sw in Switches) sw.IsSelected = false; });
        SaveActionsCommand = new DelegateCommand(async () => await SaveAsync());
        ReloadActionsCommand = new DelegateCommand(async () => await LoadAsync());
        RunActionCommand = new DelegateCommand(async () => await RunAsync());
        StopActionCommand = new DelegateCommand(() => _execution?.Cancel());
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
    public bool HasUnsavedChanges => _dirty;
    public bool IsActionRunning => _running;
    public bool CanEdit => !_running && !_working;
    public bool IsKeyStep => SelectedStep?.Kind == BenchActionStepKind.SetKeys;
    public bool IsHoldStep => SelectedStep?.Kind == BenchActionStepKind.Hold;
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
            if (e.PropertyName is nameof(BenchKeyStepItem.Mask) or nameof(BenchKeyStepItem.DurationMsText))
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
            var cloned = new BenchKeyStepItem(new(step.Kind, step.Mask, step.DurationMs));
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
    /// <summary>Copy the direct twenty-switch experiment into a named action.</summary>
    public void AddCurrentBenchMask(uint mask)
    {
        if (!CanEdit || mask > 0xFFFFF) return;
        if (SelectedAction is null) NewAction();
        AddStep(BenchActionStep.SetKeys(mask));
        Status = "已将当前 20 路开关组合加入动作；可继续添加保持时间。";
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
