using System.IO;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using AutoTest.Contracts;
using AutoTest.Desktop.Projects;
using AutoTest.Devices.Station;
using AutoTest.Projects.KeyActions;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>KEY requests persist after each wait and after the final segment.
/// A wait-only segment explicitly sends no CAN; a zero mask explicitly releases all.</summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class KeyActionStepEditor : BindableBase
    {
        private uint _mask;
        private string _maskHexText;
        private string _holdText;
        private bool _waitOnly;
        public Guid StepId { get; }
        public KeyActionStepEditor(Guid id, uint mask, int holdMs, bool waitOnly = false)
        {
            StepId = id;
            _mask = mask;
            _maskHexText = "0x" + mask.ToString("X5", CultureInfo.InvariantCulture);
            _holdText = holdMs.ToString(CultureInfo.InvariantCulture);
            _waitOnly = waitOnly;
        }
        public bool WaitOnly
        {
            get => _waitOnly;
            set
            {
                if (!SetProperty(ref _waitOnly, value)) return;
                if (value) KeyMask = 0; // No KEY frame can accompany a pure wait.
                RaisePropertyChanged(nameof(CanChangeKeys));
                RaisePropertyChanged(nameof(Summary));
            }
        }
        public bool CanChangeKeys => !WaitOnly;
        public uint KeyMask
        {
            get => _mask;
            set
            {
                if (value > 0xFFFFF) throw new ArgumentOutOfRangeException(nameof(value));
                if (!SetProperty(ref _mask, value)) return;
                _maskHexText = "0x" + value.ToString("X5", CultureInfo.InvariantCulture);
                RaisePropertyChanged(nameof(MaskHexText));
                RaisePropertyChanged(nameof(MaskError));
                RaisePropertyChanged(nameof(Summary));
            }
        }
        public string MaskHexText
        {
            get => _maskHexText;
            set
            {
                if (!SetProperty(ref _maskHexText, value ?? "")) return;
                var text = _maskHexText.Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
                if (uint.TryParse(text, NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out var parsed) && parsed <= 0xFFFFF)
                {
                    if (SetProperty(ref _mask, parsed)) RaisePropertyChanged(nameof(Summary));
                }
                RaisePropertyChanged(nameof(MaskError));
            }
        }
        public string MaskError
        {
            get
            {
                var value = MaskHexText.Trim();
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
                return uint.TryParse(value, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var mask) && mask <= 0xFFFFF
                    ? "" : "请输入 0x00000～0xFFFFF 的十六进制掩码。";
            }
        }
        public string HoldMsText
        {
            get => _holdText;
            set { if (SetProperty(ref _holdText, value ?? "")) RaisePropertyChanged(nameof(Summary)); }
        }
        public string Summary => WaitOnly
            ? "纯等待 " + HoldMsText + " ms · 不发送 KEY CAN / 不改变保持状态"
            : (KeyMask == 0 ? "明确全部断开" :
                string.Join(" + ", Enumerable.Range(1, 20)
                    .Where(i => (KeyMask & (1u << (i - 1))) != 0)
                    .Select(i => "K" + i.ToString("00", CultureInfo.InvariantCulture)))) +
                " · " + HoldMsText + " ms 后进入下一段（不会自动释放）";
        public KeyActionStep Export()
        {
            if (!string.IsNullOrEmpty(MaskError))
                throw new InvalidOperationException(MaskError);
            if (!int.TryParse(HoldMsText, NumberStyles.None, CultureInfo.InvariantCulture, out var hold))
                throw new InvalidOperationException("保持时间必须为非负整数毫秒。");
            return new KeyActionStep(StepId, KeyMask, hold, WaitOnly).Validate();
        }
    }

    public sealed class KeyActionEditor : BindableBase
    {
        private string _name;
        public Guid ActionId { get; }
        public ObservableCollection<KeyActionStepEditor> Steps { get; } = new();
        public KeyActionEditor(Guid actionId, string name)
        {
            ActionId = actionId;
            _name = name;
        }
        public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }
        public string Summary => Steps.Count + " 段操作 · " +
            Steps.Sum(x => int.TryParse(x.HoldMsText, out var ms) ? ms : 0) + " ms";
        public void NotifySummary() => RaisePropertyChanged(nameof(Summary));
        public KeyActionDefinition Export(int order) =>
            new KeyActionDefinition(ActionId, Name.Trim(), order,
                Steps.Select(step => step.Export()).ToImmutableArray()).Validate();
    }

    private KeyActionCatalogSession? _keyCatalogSession;
    private string? _keyCatalogProject;
    private bool _loadingKeyActions;
    private bool _savingKeyActions;
    private long _keyEditGeneration;
    private bool _keyActionsDirty;
    private bool _syncingActionKeys;
    private KeyActionEditor? _selectedKeyAction;
    private KeyActionStepEditor? _selectedKeyStep;
    private CancellationTokenSource? _keyActionStop;
    private Task? _keyActionTask;
    private bool _keyActionRunning;
    private string _keyActionRunStatus = "尚无 KEY 动作执行记录。";
    public ObservableCollection<KeyActionEditor> KeyActions { get; } = new();
    public ObservableCollection<KeyChoice> StepKeys { get; } = new(
        Enumerable.Range(1, 20).Select(i => new KeyChoice(i)));
    public KeyActionEditor? SelectedKeyAction
    {
        get => _selectedKeyAction;
        set
        {
            if (!SetProperty(ref _selectedKeyAction, value)) return;
            SelectedKeyStep = value?.Steps.FirstOrDefault();
            RaisePropertyChanged(nameof(HasSelectedKeyAction));
            NotifyKeyCommands();
        }
    }
    public KeyActionStepEditor? SelectedKeyStep
    {
        get => _selectedKeyStep;
        set
        {
            if (!SetProperty(ref _selectedKeyStep, value)) return;
            _syncingActionKeys = true;
            try
            {
                foreach (var key in StepKeys)
                    key.Checked = value is not null && (value.KeyMask & key.Mask) != 0;
            }
            finally { _syncingActionKeys = false; }
            RaisePropertyChanged(nameof(HasSelectedKeyStep));
            NotifyKeyCommands();
        }
    }
    public bool HasSelectedKeyAction => SelectedKeyAction is not null;
    public bool HasSelectedKeyStep => SelectedKeyStep is not null;
    public bool KeyActionsDirty
    {
        get => _keyActionsDirty;
        private set { if (SetProperty(ref _keyActionsDirty, value)) NotifyKeyCommands(); }
    }
    public bool IsKeyActionRunning
    {
        get => _keyActionRunning;
        private set { if (SetProperty(ref _keyActionRunning, value)) { NotifyKeyCommands(); RaiseCommands(); } }
    }
    public bool CanEditKeyActions => !IsBusy && !_savingKeyActions && !IsKeyActionRunning && _projects.Current is not null;
    public bool ControllerBindingNeedsSave => _loadedControllerAsset != Guid.Empty &&
        _loadedControllerSha is not null &&
        (_keyCatalogSession?.Document.Controller is not { } pin ||
         pin.AssetId != _loadedControllerAsset || pin.Revision != _loadedControllerRevision ||
         !pin.Sha256.Equals(_loadedControllerSha, StringComparison.OrdinalIgnoreCase));
    public bool CanSaveKeyActions => CanEditKeyActions && (KeyActionsDirty || ControllerBindingNeedsSave);
    // Buttons execute the last successfully saved document, never editor drafts.
    // When one KEY action is running another button may request a serialized
    // transition; non-KEY operations still block a new action.
    public bool CanRunKeyAction => CanPressKeyPad;
    public string KeyActionRunStatus
    {
        get => _keyActionRunStatus;
        private set => SetProperty(ref _keyActionRunStatus, value);
    }
    public string KeyLibraryState => IsKeyActionRunning ? "执行中" :
        (KeyActionsDirty || ControllerBindingNeedsSave) ? "未保存" :
        _keyCatalogSession is null ? "未创建" : "已保存";
    public string KeyCatalogRevisionLabel => _keyCatalogSession is null
        ? "尚未保存到工程" : "工程动作库 · Revision " + _keyCatalogSession.AssetRevision +
        (KeyActionsDirty ? " · 未保存修改" : "");
    public DelegateCommand AddKeyActionCommand { get; private set; } = null!;
    public DelegateCommand<KeyActionEditor> EditKeyActionCommand { get; private set; } = null!;
    public DelegateCommand<KeyActionEditor> ExecuteKeyActionCommand { get; private set; } = null!;
    public DelegateCommand CopyKeyActionCommand { get; private set; } = null!;
    public DelegateCommand DeleteKeyActionCommand { get; private set; } = null!;
    public DelegateCommand SaveKeyActionsCommand { get; private set; } = null!;
    public DelegateCommand AddKeyStepCommand { get; private set; } = null!;
    public DelegateCommand AddWaitOnlyStepCommand { get; private set; } = null!;
    public DelegateCommand CopyKeyStepCommand { get; private set; } = null!;
    public DelegateCommand DeleteKeyStepCommand { get; private set; } = null!;
    public DelegateCommand MoveKeyStepUpCommand { get; private set; } = null!;
    public DelegateCommand MoveKeyStepDownCommand { get; private set; } = null!;
    public DelegateCommand ClearStepKeysCommand { get; private set; } = null!;
    public DelegateCommand InvertStepKeysCommand { get; private set; } = null!;
    public DelegateCommand CopyPreviousStepKeysCommand { get; private set; } = null!;
    public DelegateCommand StopKeyActionCommand { get; private set; } = null!;

    private void InitializeKeyActions()
    {
        foreach (var key in StepKeys) key.PropertyChanged += OnStepKeyChecked;
        AddKeyActionCommand = new DelegateCommand(AddKeyAction, () => CanEditKeyActions);
        EditKeyActionCommand = new DelegateCommand<KeyActionEditor>(
            action => { if (action is not null) SelectActionForEdit(action); },
            _ => CanEditKeyActions);
        ExecuteKeyActionCommand = new DelegateCommand<KeyActionEditor>(
            async action =>
            {
                if (action is not null) await SwitchSavedKeyActionAsync(action.ActionId);
            },
            action => CanRunKeyAction && action is not null &&
                _keyCatalogSession?.Document.Actions.Any(x => x.ActionId == action.ActionId) == true);
        CopyKeyActionCommand = new DelegateCommand(CopyKeyAction,
            () => CanEditKeyActions && HasSelectedKeyAction);
        DeleteKeyActionCommand = new DelegateCommand(DeleteKeyAction,
            () => CanEditKeyActions && HasSelectedKeyAction);
        SaveKeyActionsCommand = new DelegateCommand(async () => await SaveKeyActionsAsync(),
            () => CanSaveKeyActions);
        AddKeyStepCommand = new DelegateCommand(AddKeyStep,
            () => CanEditKeyActions && HasSelectedKeyAction);
        AddWaitOnlyStepCommand = new DelegateCommand(AddWaitOnlyStep,
            () => CanEditKeyActions && HasSelectedKeyAction);
        CopyKeyStepCommand = new DelegateCommand(CopyKeyStep,
            () => CanEditKeyActions && HasSelectedKeyStep);
        DeleteKeyStepCommand = new DelegateCommand(DeleteKeyStep,
            () => CanEditKeyActions && HasSelectedKeyStep);
        MoveKeyStepUpCommand = new DelegateCommand(() => MoveKeyStep(-1),
            () => CanEditKeyActions && HasSelectedKeyStep);
        MoveKeyStepDownCommand = new DelegateCommand(() => MoveKeyStep(1),
            () => CanEditKeyActions && HasSelectedKeyStep);
        ClearStepKeysCommand = new DelegateCommand(() => SetStepMask(0),
            () => CanEditKeyActions && HasSelectedKeyStep);
        InvertStepKeysCommand = new DelegateCommand(() => SetStepMask(SelectedKeyStep!.KeyMask ^ 0xFFFFF),
            () => CanEditKeyActions && HasSelectedKeyStep);
        CopyPreviousStepKeysCommand = new DelegateCommand(CopyPreviousStepKeys,
            () => CanEditKeyActions && HasSelectedKeyStep);
        StopKeyActionCommand = new DelegateCommand(async () => await StopKeyActionAsync(),
            () => IsKeyActionRunning);
        InitializeKeyPad();
    }
    private void NotifyKeyCommands()
    {
        RaisePropertyChanged(nameof(CanEditKeyActions));
        RaisePropertyChanged(nameof(CanSaveKeyActions));
        RaisePropertyChanged(nameof(CanRunKeyAction));
        RaisePropertyChanged(nameof(KeyCatalogRevisionLabel));
        RaisePropertyChanged(nameof(KeyLibraryState));
        AddKeyActionCommand?.RaiseCanExecuteChanged();
        EditKeyActionCommand?.RaiseCanExecuteChanged();
        ExecuteKeyActionCommand?.RaiseCanExecuteChanged();
        CopyKeyActionCommand?.RaiseCanExecuteChanged();
        DeleteKeyActionCommand?.RaiseCanExecuteChanged();
        SaveKeyActionsCommand?.RaiseCanExecuteChanged();
        AddKeyStepCommand?.RaiseCanExecuteChanged();
        AddWaitOnlyStepCommand?.RaiseCanExecuteChanged();
        CopyKeyStepCommand?.RaiseCanExecuteChanged();
        DeleteKeyStepCommand?.RaiseCanExecuteChanged();
        MoveKeyStepUpCommand?.RaiseCanExecuteChanged();
        MoveKeyStepDownCommand?.RaiseCanExecuteChanged();
        ClearStepKeysCommand?.RaiseCanExecuteChanged();
        InvertStepKeysCommand?.RaiseCanExecuteChanged();
        CopyPreviousStepKeysCommand?.RaiseCanExecuteChanged();
        StopKeyActionCommand?.RaiseCanExecuteChanged();
        NotifyKeyPadCommands();
    }

    private void MarkKeyDirty()
    {
        if (_loadingKeyActions) return;
        _keyEditGeneration++;
        KeyActionsDirty = true;
        SelectedKeyAction?.NotifySummary();
        NotifyKeyCommands();
    }
    private void OnStepKeyChecked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KeyChoice.Checked) || _syncingActionKeys ||
            SelectedKeyStep is null || !CanEditKeyActions || SelectedKeyStep.WaitOnly) return;
        SelectedKeyStep.KeyMask = StepKeys.Aggregate(0u,
            (mask, key) => key.Checked ? mask | key.Mask : mask);
    }
    private void OnStepEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedKeyStep && e.PropertyName is
            nameof(KeyActionStepEditor.KeyMask) or nameof(KeyActionStepEditor.MaskHexText))
        {
            _syncingActionKeys = true;
            try
            {
                foreach (var key in StepKeys)
                    key.Checked = SelectedKeyStep is { } step && (step.KeyMask & key.Mask) != 0;
            }
            finally { _syncingActionKeys = false; }
        }
        if (e.PropertyName is nameof(KeyActionStepEditor.KeyMask) or
            nameof(KeyActionStepEditor.MaskHexText) or nameof(KeyActionStepEditor.HoldMsText) or
            nameof(KeyActionStepEditor.WaitOnly))
            MarkKeyDirty();
    }
    private void OnActionEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyActionEditor.Name)) MarkKeyDirty();
    }
    private void WireAction(KeyActionEditor action)
    {
        action.PropertyChanged += OnActionEdited;
        foreach (var step in action.Steps) step.PropertyChanged += OnStepEdited;
    }
    private void UnwireAction(KeyActionEditor action)
    {
        action.PropertyChanged -= OnActionEdited;
        foreach (var step in action.Steps) step.PropertyChanged -= OnStepEdited;
    }
    private void SelectActionForEdit(KeyActionEditor action)
    {
        SelectedKeyAction = action;
        IsKeyEditorExpanded = true;
    }
    private void AddKeyAction()
    {
        var action = new KeyActionEditor(Guid.NewGuid(), "动作 " +
            (KeyActions.Count + 1).ToString("00", CultureInfo.InvariantCulture));
        action.Steps.Add(new KeyActionStepEditor(Guid.NewGuid(), 0, 100));
        WireAction(action);
        KeyActions.Add(action);
        SelectActionForEdit(action);
        MarkKeyDirty();
    }
    private void CopyKeyAction()
    {
        if (SelectedKeyAction is not { } original) return;
        var action = new KeyActionEditor(Guid.NewGuid(), original.Name + " 副本");
        foreach (var step in original.Steps)
            action.Steps.Add(new KeyActionStepEditor(Guid.NewGuid(), step.KeyMask,
                int.TryParse(step.HoldMsText, out var ms) ? ms : 100, step.WaitOnly));
        WireAction(action);
        KeyActions.Insert(KeyActions.IndexOf(original) + 1, action);
        SelectActionForEdit(action);
        MarkKeyDirty();
    }
    private void DeleteKeyAction()
    {
        if (SelectedKeyAction is not { } action) return;
        var idx = KeyActions.IndexOf(action);
        UnwireAction(action);
        KeyActions.Remove(action);
        SelectedKeyAction = KeyActions.Count == 0 ? null : KeyActions[Math.Min(idx, KeyActions.Count - 1)];
        MarkKeyDirty();
    }
    private void AddKeyStep()
    {
        if (SelectedKeyAction is not { } action) return;
        var mask = SelectedKeyStep?.KeyMask ?? 0;
        var step = new KeyActionStepEditor(Guid.NewGuid(), mask, 100);
        step.PropertyChanged += OnStepEdited;
        var idx = SelectedKeyStep is null ? action.Steps.Count : action.Steps.IndexOf(SelectedKeyStep) + 1;
        action.Steps.Insert(idx, step);
        SelectedKeyStep = step;
        MarkKeyDirty();
    }
    private void AddWaitOnlyStep()
    {
        if (SelectedKeyAction is not { } action) return;
        var step = new KeyActionStepEditor(Guid.NewGuid(), 0, 1000, waitOnly: true);
        step.PropertyChanged += OnStepEdited;
        var idx = SelectedKeyStep is null ? action.Steps.Count :
            action.Steps.IndexOf(SelectedKeyStep) + 1;
        action.Steps.Insert(idx, step);
        SelectedKeyStep = step;
        MarkKeyDirty();
    }

    private void CopyKeyStep()
    {
        if (SelectedKeyAction is not { } action || SelectedKeyStep is not { } current) return;
        var copy = new KeyActionStepEditor(Guid.NewGuid(), current.KeyMask,
            int.TryParse(current.HoldMsText, out var ms) ? ms : 100, current.WaitOnly);
        copy.PropertyChanged += OnStepEdited;
        action.Steps.Insert(action.Steps.IndexOf(current) + 1, copy);
        SelectedKeyStep = copy;
        MarkKeyDirty();
    }
    private void DeleteKeyStep()
    {
        if (SelectedKeyAction is not { } action || SelectedKeyStep is not { } current) return;
        if (action.Steps.Count == 1)
        {
            Status = "每个动作至少保留一段 KEY 组合。";
            return;
        }
        var idx = action.Steps.IndexOf(current);
        current.PropertyChanged -= OnStepEdited;
        action.Steps.Remove(current);
        SelectedKeyStep = action.Steps[Math.Min(idx, action.Steps.Count - 1)];
        MarkKeyDirty();
    }
    private void MoveKeyStep(int offset)
    {
        if (SelectedKeyAction is not { } action || SelectedKeyStep is not { } current) return;
        var index = action.Steps.IndexOf(current);
        var to = index + offset;
        if (to < 0 || to >= action.Steps.Count) return;
        action.Steps.Move(index, to);
        MarkKeyDirty();
    }
    private void SetStepMask(uint mask)
    {
        if (SelectedKeyStep is null) return;
        var edited = SelectedKeyStep;
        edited.KeyMask = mask;
        SelectedKeyStep = null;
        SelectedKeyStep = edited;
    }
    private void CopyPreviousStepKeys()
    {
        if (SelectedKeyAction is not { } action || SelectedKeyStep is not { } step) return;
        var index = action.Steps.IndexOf(step);
        if (index > 0) SetStepMask(action.Steps[index - 1].KeyMask);
    }

    public async Task ReloadKeyActionsAsync()
    {
        if (_disposed || _savingKeyActions || IsKeyActionRunning) return;
        var path = _projects.Current?.ManifestPath;
        if (KeyActionsDirty && string.Equals(_keyCatalogProject, path, StringComparison.OrdinalIgnoreCase))
        {
            Status = "按键动作有未保存修改；刷新资源前请保存。";
            return;
        }
        _loadingKeyActions = true;
        try
        {
            foreach (var action in KeyActions) UnwireAction(action);
            KeyActions.Clear();
            _keyCatalogSession = null;
            _keyCatalogProject = path;
            SelectedKeyAction = null;
            if (_projects.Current?.Definition.Assets.Any(x => x.Kind ==
                ProjectWorkspaceService.KeyActionsKind) == true)
            {
                var loaded = await _projects.OpenKeyActionsAsync();
                if (!string.Equals(_projects.Current?.ManifestPath, path,
                    StringComparison.OrdinalIgnoreCase)) return;
                _keyCatalogSession = loaded;
                foreach (var item in loaded.Document.Actions.OrderBy(x => x.Order))
                {
                    var action = new KeyActionEditor(item.ActionId, item.Name);
                    foreach (var step in item.Steps)
                        action.Steps.Add(new KeyActionStepEditor(step.StepId, step.KeyMask, step.HoldMs, step.WaitOnly));
                    WireAction(action);
                    KeyActions.Add(action);
                }
                SelectedKeyAction = KeyActions.FirstOrDefault();
            }
            KeyActionsDirty = false;
        }
        catch (Exception ex)
        {
            Status = "按键动作资源加载被拒绝：" + ex.Message;
        }
        finally
        {
            _loadingKeyActions = false;
            RefreshSavedKeyPads();
            NotifyKeyCommands();
        }
    }

    private async Task SaveKeyActionsAsync() => await Execute("保存按键动作", async () =>
    {
        var project = _projects.Current ?? throw new InvalidOperationException("请先打开工程。");
        if (_keyCatalogProject != project.ManifestPath)
            throw new InvalidOperationException("工程已经切换，不能保存旧工程的动作草稿。");
        var baseCatalog = _keyCatalogSession?.Document ?? KeyActionCatalog.Create();
        KeyActionControllerPin? pin = baseCatalog.Controller;
        if (_loadedControllerAsset != Guid.Empty && _loadedControllerSha is not null)
        {
            await VerifyControllerFreshAsync();
            pin = new KeyActionControllerPin(_loadedControllerAsset, _loadedControllerRevision,
                _loadedControllerSha);
        }
        var catalog = baseCatalog with
        {
            Controller = pin,
            Actions = KeyActions.Select((action, index) => action.Export(index)).ToImmutableArray()
        };
        catalog.Validate();
        var generationBeingSaved = _keyEditGeneration;
        _savingKeyActions = true;
        NotifyKeyCommands();
        try
        {
            if (_keyCatalogSession is null)
            {
                _keyCatalogSession = await _projects.CreateKeyActionsAsync();
                catalog = catalog with { CatalogId = _keyCatalogSession.Document.CatalogId };
            }
            _keyCatalogSession = await _projects.SaveKeyActionsAsync(_keyCatalogSession, catalog);
            // Input could change while async file persistence is pending.
            // A successful write must not clear edits made after its snapshot.
            KeyActionsDirty = _keyEditGeneration != generationBeingSaved;
            KeyActionRunStatus = KeyActionsDirty
                ? "保存了上一个动作库修订；保存期间出现新修改，仍需再次保存。"
                : "动作库已保存、回读校验通过；没有执行任何硬件动作。";
            Status = "按键动作库已保存至工程（Revision " + _keyCatalogSession.AssetRevision +
                "）；快捷按钮现已使用此保存版本。";
            RefreshSavedKeyPads();
        }
        finally { _savingKeyActions = false; NotifyKeyCommands(); }
    });

    private async Task RunKeyActionAsync(Guid actionId)
    {
      try
      {
       await Execute("执行 KEY 动作", async () =>
       {
        var stop = _keyActionStop ?? throw new InvalidOperationException("KEY 动作取消上下文不存在。");
        // Validation and execution refer exclusively to the persisted
        // engineering action. Dirty drafts cannot alter a live button.
        stop.Token.ThrowIfCancellationRequested();
        if (_keyCatalogSession is null)
            throw new InvalidOperationException("动作尚未保存到工程，未下发 KEY。");
        var reopened = await _projects.OpenKeyActionsAsync();
        if (reopened.AssetId != _keyCatalogSession.AssetId ||
            reopened.AssetRevision != _keyCatalogSession.AssetRevision ||
            !reopened.FileSha256.Equals(_keyCatalogSession.FileSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("动作资源已修订或文件变动，请重新打开。");
        var saved = reopened.Document.Actions.SingleOrDefault(a => a.ActionId == actionId) ??
            throw new InvalidOperationException("该动作已从工程资源移除。");
        var list = saved.Steps.ToArray();
        if (!_hardware.IsConnected && list.Any(step => !step.WaitOnly))
            throw new IOException("此动作需要真实 KEY 输出，请先连接主控 USB。");
        // A catalog without a legacy Controller pin is a direct 20 KEY script.
        // A pinned catalog must still satisfy the exact saved hardware policy.
        if (reopened.Document.Controller is { } pin)
        {
            await VerifyControllerFreshAsync();
            if (pin.AssetId != _loadedControllerAsset ||
                pin.Revision != _loadedControllerRevision ||
                !string.Equals(pin.Sha256, _loadedControllerSha,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("既有 KEY 限制规则已变更；请重新载入动作库。");
            var policy = _controller?.KeyHardwarePolicy ??
                throw new InvalidOperationException("旧工程 KEY 硬件限制未加载，不允许绕过。");
            policy.Validate();
            foreach (var step in list)
            {
                if ((step.KeyMask & ~policy.SupportedMask) != 0 ||
                    policy.ForbiddenPairs.Any(pair =>
                        (step.KeyMask & pair.A) == pair.A && (step.KeyMask & pair.B) == pair.B))
                    throw new InvalidOperationException("动作 " + saved.Name +
                        " 超出工程已批准的 KEY 组合限制。");
            }
        }
        // Only actual KEY transitions are authorized. Wait-only segments are
        // not encoded as zero masks and never cause a physical output change.
        var frames = list.Where(step => !step.WaitOnly).Select(step => step.KeyMask == 0
            ? KeypadCommandCodec.EncodeReleaseAll() : KeypadCommandCodec.EncodeMask(step.KeyMask)).ToArray();
        var epoch = _hardware.CurrentStationEpoch;
        if (frames.Length > 0 && (epoch == 0 || !_hardware.IsConnected))
            throw new InvalidOperationException("USB 未连接。");
        var fingerprintText = reopened.FileSha256 + ":" + saved.ActionId + ":" + epoch + ":" +
            string.Join("|", list.Select(s => s.StepId + ":" + s.KeyMask + ":" +
                s.HoldMs + ":" + s.WaitOnly));
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintText)))
            .ToLowerInvariant();
        stop.Token.ThrowIfCancellationRequested();
        int completed = 0;
        int sentFrames = 0;
        var authorized = false;
        try
        {
            if (frames.Length > 0)
            {
                await _hardware.AuthorizeManualKeyActionAsync(sha,
                    "AUTHORIZE KEY ACTION " + sha, frames, stop.Token);
                authorized = true;
            }
            for (var i = 0; i < list.Length; i++)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (frames.Length > 0 && (!_hardware.IsConnected ||
                    _hardware.CurrentStationEpoch != epoch))
                    throw new IOException("USB 已重连或断开；禁止继续使用旧连接授权。");
                var segment = list[i];
                if (segment.WaitOnly)
                {
                    KeyActionRunStatus = "步骤 " + (i + 1) + "/" + list.Length +
                        " · 纯等待 " + segment.HoldMs + " ms，不发送 KEY CAN";
                    Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                        "KEY SIM", "WAIT", "", "",
                        saved.Name + " · 纯等待，不改变此前 KEY 请求组合"));
                }
                else
                {
                    var frame = frames[sentFrames++];
                    KeyActionRunStatus = "步骤 " + (i + 1) + "/" + list.Length +
                        " · 发送 KEY " + (segment.KeyMask == 0
                            ? "全部断开" : "0x" + segment.KeyMask.ToString("X5"));
                    await _hardware.SendManualKeyStepAsync(frame, sha, stop.Token);
                    Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                        "KEY CAN", "TX ACK", "0x730",
                        Convert.ToHexString(frame.Data.AsSpan()),
                        saved.Name + " · STEP " + segment.StepId +
                        " · 主控 ACK（非实际五路电压）"));
                    KeyActionRunStatus = "步骤 " + (i + 1) + "/" + list.Length +
                        " · 主控 ACK；等待 " + segment.HoldMs + "ms，组合不会自动释放";
                }
                if (segment.HoldMs > 0)
                    await Task.Delay(segment.HoldMs, stop.Token);
                completed++;
            }
            KeyActionRunStatus = "完成 " + completed + "/" + list.Length +
                " 段；" + _hardware.KeyRequestStatus;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            KeyActionRunStatus = "操作员已停止（" + completed + "/" + list.Length +
                "）；没有自动释放 KEY。 " + _hardware.KeyRequestStatus;
        }
        catch
        {
            KeyActionRunStatus = "动作异常中止（" + completed + "/" + list.Length +
                "）；没有自动释放 KEY。 " + _hardware.KeyRequestStatus;
            throw;
        }
        finally
        {
            if (authorized)
            {
                try { await _hardware.RevokeManualKeyActionAsync(); }
                catch (Exception error)
                {
                    KeyActionRunStatus += " 撤销授权异常：" + error.Message;
                }
            }
        }
       });
      }
      finally
      {
        _keyActionStop?.Dispose();
        _keyActionStop = null;
        IsKeyActionRunning = false;
        MarkActiveKeyPad(null);
      }
    }

    public Task SaveKeyActionsForCloseAsync() => SaveKeyActionsAsync();

    public async Task StopKeyActionAsync()
    {
        // Invalidate queued clicks before requesting stop; no queued action
        // may silently start after the operator presses Stop or closes the tool.
        Interlocked.Increment(ref _keyActionRequest);
        try { _keyActionStop?.Cancel(); }
        catch (ObjectDisposedException) { }
        var task = _keyActionTask;
        if (task is not null && !task.IsCompleted) await task;
    }

    private void DisposeKeyActions()
    {
        Interlocked.Increment(ref _keyActionRequest);
        try { _keyActionStop?.Cancel(); }
        catch (ObjectDisposedException) { }
        foreach (var key in StepKeys) key.PropertyChanged -= OnStepKeyChecked;
        foreach (var action in KeyActions) UnwireAction(action);
    }
}
