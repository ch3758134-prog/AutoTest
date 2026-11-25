using System.Collections.ObjectModel;
using System.Globalization;
using Prism.Commands;
using AutoTest.Projects.KeyActions;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Operator action pad: only saved engineering actions appear here. Draft
/// edits never change the meaning of a button before a successful save.
/// </summary>
public sealed partial class ManualBenchViewModel
{
    public sealed class KeyActionPadItem : BindableBase
    {
        private bool _isExecuting;
        public Guid ActionId { get; }
        public string Name { get; }
        public string Subtitle { get; }
        public string RunLabel => IsExecuting ? "正在执行" : "点击执行";
        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (SetProperty(ref _isExecuting, value))
                    RaisePropertyChanged(nameof(RunLabel));
            }
        }
        public KeyActionPadItem(KeyActionDefinition saved)
        {
            ActionId = saved.ActionId;
            Name = saved.Name;
            var duration = saved.Steps.Sum(x => (long)x.HoldMs);
            Subtitle = saved.Steps.Length.ToString(CultureInfo.InvariantCulture) +
                " 段 · " + duration.ToString(CultureInfo.InvariantCulture) + " ms";
        }
    }

    private readonly ObservableCollection<KeyActionPadItem> _allSavedKeyPads = new();
    public ObservableCollection<KeyActionPadItem> VisibleKeyPads { get; } = new();
    private string _keyPadSearch = "";
    private Guid? _executingKeyAction;
    private readonly SemaphoreSlim _keyActionTransition = new(1, 1);
    private long _keyActionRequest;
    public DelegateCommand<KeyActionPadItem> PressKeyPadCommand { get; private set; } = null!;
    public DelegateCommand<KeyActionPadItem> PinKeyPadCommand { get; private set; } = null!;

    public string KeyPadSearchText
    {
        get => _keyPadSearch;
        set
        {
            if (SetProperty(ref _keyPadSearch, value ?? "")) FilterKeyPad();
        }
    }
    public bool IsKeyPadEmpty => VisibleKeyPads.Count == 0;
    public string KeyPadCountLabel => VisibleKeyPads.Count + " / " +
        _allSavedKeyPads.Count + " 个已保存动作";
    public string KeyPadEmptyMessage => _allSavedKeyPads.Count == 0
        ? "还没有已保存的按键动作。点击“新建动作”配置后保存。"
        : "没有找到匹配的按键动作。";

    public bool CanPressKeyPad => !_disposed && !_savingKeyActions &&
        _keyCatalogSession is not null && (!IsBusy || IsKeyActionRunning);
    public void ReportKeyPadUnavailable()
    {
        KeyActionRunStatus = _keyCatalogSession is null
            ? "当前没有已加载的 KEY 动作库；可以双击编辑，不能向硬件发送。"
            : "当前工位尚不可执行该 KEY 动作；可双击编辑或在主窗口查看连接状态。";
    }

    private void InitializeKeyPad()
    {
        PressKeyPadCommand = new DelegateCommand<KeyActionPadItem>(
            async item =>
            {
                if (item is not null) await SwitchSavedKeyActionAsync(item.ActionId);
            },
            item => CanPressKeyPad && item is not null &&
                _keyCatalogSession?.Document.Actions.Any(x => x.ActionId == item.ActionId) == true);

        PinKeyPadCommand = new DelegateCommand<KeyActionPadItem>(
            async item =>
            {
                if (item is not null) await PinSavedKeyActionAsync(item.ActionId);
            },
            item => item is not null && CanEditKeyActions && !KeyActionsDirty &&
                KeyActions.Any(x => x.ActionId == item.ActionId));
    }

    private void RefreshSavedKeyPads()
    {
        _allSavedKeyPads.Clear();
        if (_keyCatalogSession is { } session)
            foreach (var saved in session.Document.Actions.OrderBy(x => x.Order))
                _allSavedKeyPads.Add(new KeyActionPadItem(saved)
                {
                    IsExecuting = saved.ActionId == _executingKeyAction
                });
        FilterKeyPad();
        NotifyKeyPadCommands();
    }

    private void FilterKeyPad()
    {
        var search = KeyPadSearchText.Trim();
        VisibleKeyPads.Clear();
        foreach (var item in _allSavedKeyPads)
            if (search.Length == 0 ||
                item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                VisibleKeyPads.Add(item);
        RaisePropertyChanged(nameof(KeyPadCountLabel));
        RaisePropertyChanged(nameof(KeyPadEmptyMessage));
        RaisePropertyChanged(nameof(IsKeyPadEmpty));
    }

    private void MarkActiveKeyPad(Guid? actionId)
    {
        _executingKeyAction = actionId;
        foreach (var item in _allSavedKeyPads)
            item.IsExecuting = actionId.HasValue && item.ActionId == actionId;
    }

    private void NotifyKeyPadCommands()
    {
        RaisePropertyChanged(nameof(CanPressKeyPad));
        PressKeyPadCommand?.RaiseCanExecuteChanged();
        PinKeyPadCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Latest click wins. Cancel only uncompleted segments, await the prior
    /// physical USB task and its command-fence revocation, then begin the next
    /// saved action. Never interleave two KEY 0x730 transactions.
    /// </summary>
    private async Task SwitchSavedKeyActionAsync(Guid actionId)
    {
        var request = Interlocked.Increment(ref _keyActionRequest);
        try { _keyActionStop?.Cancel(); }
        catch (ObjectDisposedException) { }
        await _keyActionTransition.WaitAsync();
        try
        {
            if (_disposed || request != Interlocked.Read(ref _keyActionRequest)) return;
            if (_keyActionTask is { IsCompleted: false } previous)
                await previous;
            if (_disposed || request != Interlocked.Read(ref _keyActionRequest)) return;
            if (!CanPressKeyPad ||
                _keyCatalogSession?.Document.Actions.Any(x => x.ActionId == actionId) != true)
            {
                Status = "该动作尚未保存或工位正在执行其他操作，未下发 KEY。";
                return;
            }
            _keyActionStop = new CancellationTokenSource();
            MarkActiveKeyPad(actionId);
            IsKeyActionRunning = true;
            var name = _keyCatalogSession.Document.Actions.First(x => x.ActionId == actionId).Name;
            KeyActionRunStatus = "正在执行：" + name + " · 点击其他动作将先停止本动作剩余步骤，再按顺序切换";
            _keyActionTask = RunKeyActionAsync(actionId);
            await _keyActionTask;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "KEY 动作切换失败：" + error.Message;
            KeyActionRunStatus = Status + " · " + _hardware.KeyRequestStatus;
        }
        finally
        {
            _keyActionTransition.Release();
        }
    }

    private async Task PinSavedKeyActionAsync(Guid actionId)
    {
        if (!CanEditKeyActions || KeyActionsDirty)
        {
            Status = "有未保存编辑或动作正在执行，请先完成保存再调整按钮顺序。";
            return;
        }
        var item = KeyActions.FirstOrDefault(x => x.ActionId == actionId);
        if (item is null) return;
        var index = KeyActions.IndexOf(item);
        if (index <= 0) return;
        KeyActions.Move(index, 0);
        MarkKeyDirty();
        await SaveKeyActionsAsync();
    }
}
