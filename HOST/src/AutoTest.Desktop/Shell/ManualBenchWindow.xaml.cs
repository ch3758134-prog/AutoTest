using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Shell;

public partial class ManualBenchWindow : Window
{
    private readonly ManualBenchViewModel _model;
    private bool _closeAfterCanStopped;
    private readonly DispatcherTimer _keyAutosaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _keyAutosaveBusy;
    private ManualBenchKeyActionWindow? _keyActionDialog;
    private readonly Dictionary<ManualBenchViewModel.BenchCanRow,
        ManualBenchCanPropertiesWindow> _canPropertyWindows = new();
    private bool _benchClosePending;
    private bool _benchCloseScheduled;
    private DispatcherTimer? _keyPadClickTimer;
    private ManualBenchViewModel.KeyActionPadItem? _pendingKeyPad;
    private DateTime _ignoreKeyPadClickUntilUtc;
    private long _keyPadIntentGeneration;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void CancelPendingKeyPadClick()
    {
        // Invalidate in-flight asynchronous save-before-execute on any newer click,
        // edit double-click, right-click, stop or window shutdown.
        _keyPadIntentGeneration++;
        _keyPadClickTimer?.Stop();
        _pendingKeyPad = null;
    }

    private void KeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.UtcNow <= _ignoreKeyPadClickUntilUtc) return;
        if (sender is not Button { Tag: ManualBenchViewModel.KeyActionPadItem pad }) return;
        // Button.Click normally fires on the FIRST click of a double click.
        // A direct hardware command here would unintentionally execute when
        // the operator only wants to edit. Dispatch only after the OS double
        // click interval has expired without a second click.
        CancelPendingKeyPadClick();
        _pendingKeyPad = pad;
        _keyPadClickTimer ??= new DispatcherTimer(DispatcherPriority.Normal);
        _keyPadClickTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp((int)GetDoubleClickTime() + 50, 300, 1100));
        _keyPadClickTimer.Tick -= DispatchPendingKeyPad;
        _keyPadClickTimer.Tick += DispatchPendingKeyPad;
        _keyPadClickTimer.Start();
    }

    private async void DispatchPendingKeyPad(object? sender, EventArgs e)
    {
        var pad = _pendingKeyPad;
        CancelPendingKeyPadClick();
        var intent = _keyPadIntentGeneration;
        if (pad is null || _benchClosePending || _benchCloseScheduled || !IsLoaded)
            return;
        // One click means execute the CURRENT edit, not silently execute an
        // earlier saved KEY mask while the operator's latest changes are pending.
        if (_model.KeyActionsDirty || _model.ControllerBindingNeedsSave)
        {
            try
            {
                await _model.SaveKeyActionsForCloseAsync();
                if (_model.KeyActionsDirty || _model.ControllerBindingNeedsSave)
                {
                    _model.ReportKeyPadSaveFailure("保存期间还有新的修改，请重试。");
                    return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _model.ReportKeyPadSaveFailure(error.Message);
                return;
            }
        }
        if (intent != _keyPadIntentGeneration || _benchClosePending ||
            _benchCloseScheduled || !IsLoaded || _model.KeyActionsDirty ||
            _model.ControllerBindingNeedsSave) return;
        var command = _model.PressKeyPadCommand;
        if (command.CanExecute(pad)) command.Execute(pad);
        else _model.ReportKeyPadUnavailable();
    }

    private void KeyPad_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        CancelPendingKeyPadClick();
        // Consume the second mouse-down so ButtonBase never dispatches the
        // second click after the editing overlay appears.
        e.Handled = true;
        _ignoreKeyPadClickUntilUtc = DateTime.UtcNow.AddMilliseconds(
            Math.Clamp((int)GetDoubleClickTime() + 50, 300, 1100));
        if (sender is Button { Tag: ManualBenchViewModel.KeyActionPadItem pad } &&
            _model.KeyActions.FirstOrDefault(x => x.ActionId == pad.ActionId) is { } action)
            OpenKeyActionEditor(action);
    }

    private void KeyPad_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Opening the edit menu must cancel a pending single-click hardware action.
        CancelPendingKeyPadClick();
    }

    public ManualBenchWindow(ManualBenchViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = model;
        InitializeComponent();
        Loaded += (_, _) => DesktopWindowSizing.FitToVisibleWorkspace(this);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                _ = Dispatcher.BeginInvoke(
                    new Action(() => DesktopWindowSizing.FitToVisibleWorkspace(this)),
                    DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) => ApplyBenchAdaptiveLayout();
        Loaded += (_, _) => ApplyBenchAdaptiveLayout();
        Closing += OnClosing;
        _keyAutosaveTimer.Tick += OnKeyAutosaveTick;
        Loaded += (_, _) => _keyAutosaveTimer.Start();
        PreviewMouseDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewTextInput += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        Closed += (_, _) =>
        {
            CancelPendingKeyPadClick();
            _keyAutosaveTimer.Stop();
            _model.Dispose();
        };
    }

    private int _benchLayoutMode = -1;
    private int _compactTabIndex;

    /// <summary>
    /// Wide: CAN / KEY / RX parallel. Medium: CAN & KEY plus RX below.
    /// Compact: full-width task panes, so a 1366x768 @150% DPI station
    /// can still read complete CAN data without hiding its send actions.
    /// All panes keep the SAME real device session, running periodic CAN
    /// tasks and persistent KEY state when their visual tab is hidden.
    /// </summary>
    private void ApplyBenchAdaptiveLayout()
    {
        if (!IsInitialized || ActualWidth <= 0) return;
        var mode = ActualWidth < 1060 || ActualHeight < 625 ? 2
            : ActualWidth < 1200 ? 1 : 0;
        if (_benchLayoutMode == mode) return;
        _benchLayoutMode = mode;

        BenchCompactNavigation.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        BenchBaudStatusPanel.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        BenchSendColumn.MinWidth = BenchKeyColumn.MinWidth = BenchMonitorColumn.MinWidth = 0;

        if (mode == 2)
        {
            BenchSendColumn.Width = new GridLength(1, GridUnitType.Star);
            BenchSendSplitterColumn.Width = new GridLength(0);
            BenchKeyColumn.Width = new GridLength(0);
            BenchMonitorSplitterColumn.Width = new GridLength(0);
            BenchMonitorColumn.Width = new GridLength(0);
            BenchPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            BenchMonitorRow.Height = new GridLength(0);
            BenchSendSplitter.Visibility = Visibility.Collapsed;
            BenchMonitorSplitter.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(BenchSendSplitter, 1);
            Grid.SetRowSpan(BenchKeyPanel, 1);
            Grid.SetColumn(BenchSendPanel, 0);
            Grid.SetColumn(BenchKeyPanel, 0);
            Grid.SetColumn(BenchMonitorPanel, 0);
            Grid.SetRow(BenchSendPanel, 0);
            Grid.SetRow(BenchKeyPanel, 0);
            Grid.SetRow(BenchMonitorPanel, 0);
            BenchSendPanel.Margin = BenchKeyPanel.Margin = BenchMonitorPanel.Margin = new Thickness(0);
            ShowCompactBenchPane();
            return;
        }

        BenchSendPanel.Visibility = BenchKeyPanel.Visibility =
            BenchMonitorPanel.Visibility = Visibility.Visible;
        BenchSendSplitter.Visibility = Visibility.Visible;
        Grid.SetColumn(BenchSendPanel, 0);
        Grid.SetRow(BenchSendPanel, 0);
        Grid.SetColumn(BenchKeyPanel, 2);
        Grid.SetRow(BenchKeyPanel, 0);
        BenchSendPanel.Margin = BenchKeyPanel.Margin = new Thickness(0, 0, 4, 0);
        BenchMonitorPanel.Margin = new Thickness(0);

        if (mode == 1)
        {
            BenchSendColumn.MinWidth = 370;
            BenchKeyColumn.MinWidth = 320;
            BenchSendColumn.Width = new GridLength(55, GridUnitType.Star);
            BenchKeyColumn.Width = new GridLength(45, GridUnitType.Star);
            BenchSendSplitterColumn.Width = new GridLength(6);
            BenchMonitorColumn.Width = new GridLength(0);
            BenchMonitorSplitterColumn.Width = new GridLength(0);
            BenchPrimaryRow.Height = new GridLength(3, GridUnitType.Star);
            BenchMonitorRow.Height = new GridLength(2, GridUnitType.Star);
            Grid.SetRowSpan(BenchKeyPanel, 2);
            Grid.SetRowSpan(BenchSendSplitter, 2);
            Grid.SetRow(BenchMonitorPanel, 1);
            Grid.SetColumn(BenchMonitorPanel, 0);
            BenchMonitorSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            BenchSendColumn.MinWidth = 420;
            BenchKeyColumn.MinWidth = 335;
            BenchMonitorColumn.MinWidth = 330;
            BenchSendColumn.Width = new GridLength(37, GridUnitType.Star);
            BenchKeyColumn.Width = new GridLength(29, GridUnitType.Star);
            BenchMonitorColumn.Width = new GridLength(34, GridUnitType.Star);
            BenchSendSplitterColumn.Width = new GridLength(6);
            BenchMonitorSplitterColumn.Width = new GridLength(6);
            BenchPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            BenchMonitorRow.Height = new GridLength(0);
            Grid.SetRowSpan(BenchKeyPanel, 1);
            Grid.SetRowSpan(BenchSendSplitter, 1);
            Grid.SetRow(BenchMonitorPanel, 0);
            Grid.SetColumn(BenchMonitorPanel, 4);
            BenchMonitorSplitter.Visibility = Visibility.Visible;
        }
    }

    private void BenchShowCanTab_Click(object sender, RoutedEventArgs e)
    {
        _compactTabIndex = 0;
        ShowCompactBenchPane();
    }
    private void BenchShowKeyTab_Click(object sender, RoutedEventArgs e)
    {
        _compactTabIndex = 1;
        ShowCompactBenchPane();
    }
    private void BenchShowMonitorTab_Click(object sender, RoutedEventArgs e)
    {
        _compactTabIndex = 2;
        ShowCompactBenchPane();
    }

    private void ShowCompactBenchPane()
    {
        if (_benchLayoutMode != 2) return;
        BenchSendPanel.Visibility = _compactTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        BenchKeyPanel.Visibility = _compactTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        BenchMonitorPanel.Visibility = _compactTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        BenchShowCanTab.FontWeight = _compactTabIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal;
        BenchShowKeyTab.FontWeight = _compactTabIndex == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        BenchShowMonitorTab.FontWeight = _compactTabIndex == 2 ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private async void OnKeyAutosaveTick(object? sender, EventArgs e)
    {
        if (_keyAutosaveBusy || !IsActive || !IsEnabled || _benchClosePending || _closeAfterCanStopped ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(2)) return;
        if (!_model.CanRowsDirty && !_model.CanSaveKeyActions) return;
        _keyAutosaveBusy = true;
        try
        {
            if (_model.CanRowsDirty && !_model.IsCanBatchRunning)
                await _model.SaveCanRowsForCloseAsync();
            if (_model.CanSaveKeyActions && !_model.IsKeyActionRunning)
                await _model.SaveKeyActionsForCloseAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _model.ReportBenchAutosaveFailure(error.Message);
            System.Diagnostics.Trace.TraceWarning("Manual bench draft autosave error: " + error);
        }
        finally { _keyAutosaveBusy = false; }
    }

    /// <summary>
    /// Parent IDE closes this owned tool before disposing WinUSB. Real KEY and
    /// periodic CAN tasks are stopped and pending KEY edits are durably saved.
    /// Failure retains the bench so the operator can correct it.
    /// </summary>
    public async Task<bool> PrepareForParentShutdownAsync()
    {
        if (_benchClosePending) return false;
        _benchClosePending = true;
        IsEnabled = false;
        try
        {
            // A timer-triggered asynchronous save can still be persisting even
            // when Dirty has just been cleared. Do not dispose its ViewModel.
            var pendingSaveDeadline = DateTime.UtcNow.AddSeconds(8);
            while (_keyAutosaveBusy && DateTime.UtcNow < pendingSaveDeadline)
                await Task.Delay(40);
            if (_keyAutosaveBusy)
                throw new TimeoutException("等待后台自动保存超时，已保留工作台窗口和草稿。");
            await _model.StopKeyActionAsync();
            await _model.StopCanBatchForCloseAsync();
            if (_keyActionDialog is { IsVisible: true } keyEditor &&
                !await keyEditor.PrepareForParentShutdownAsync())
                throw new InvalidOperationException(
                    "KEY 动作浮窗仍有待保存或正在进行的编辑，不能关闭工位。");
            // Close CAN property windows while the backing project and
            // CAN row objects are still alive; their Closed callbacks remove
            // dictionary entries, so iterate an immutable snapshot.
            foreach (var canEditor in _canPropertyWindows.Values.ToArray())
            {
                if (canEditor.IsVisible &&
                    !await canEditor.PrepareForParentShutdownAsync())
                    throw new InvalidOperationException(
                        "CAN 属性浮窗还有未保存配置，不能结束手动工位。");
            }
            await _model.SaveCanRowsForCloseAsync();
            if (_model.CanRowsDirty)
                throw new InvalidOperationException("CAN 报文配置未能持久化，已阻止退出。");
            if (_model.KeyActionsDirty)
            {
                await _model.SaveKeyActionsForCloseAsync();
                if (_model.KeyActionsDirty)
                    throw new InvalidOperationException(
                        "KEY 动作仍有未保存修改，已阻止主界面退出。");
            }
            _model.IsKeyEditorExpanded = false;
            _closeAfterCanStopped = true;
            Close();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _closeAfterCanStopped = false;
            System.Diagnostics.Trace.TraceError("Manual bench safe shutdown failed: " + error);
            MessageBox.Show(this, "手动台架无法安全退出：\n" + error.Message +
                "\n请核对主控真实输出，状态未确认时视为 UNKNOWN。",
                "手动台架收尾失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            if (!_closeAfterCanStopped)
            {
                _benchClosePending = false;
                IsEnabled = true;
            }
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CancelPendingKeyPadClick();
        if (_closeAfterCanStopped) return;
        e.Cancel = true;
        if (_benchClosePending || _benchCloseScheduled) return;
        _benchCloseScheduled = true;
        // Even if every cleanup Task completes synchronously (USB disconnected),
        // the second Close must run AFTER the original Closing event unwinds.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
        {
            _benchCloseScheduled = false;
            if (!_benchClosePending && !_closeAfterCanStopped)
                await PrepareForParentShutdownAsync();
        }));
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var chooser = new SaveFileDialog
        {
            Title = "导出真实手动台架操作记录",
            Filter = "JSON Lines (*.jsonl)|*.jsonl",
            FileName = "AutoTest_Bench_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl"
        };
        if (chooser.ShowDialog(this) != true) return;
        try
        {
            var lines = _model.SnapshotMonitorTimeline()
                .Select(entry => JsonSerializer.Serialize(new { Kind = "CAN_RX", Entry = entry }))
                .Concat(_model.Logs.Select(entry => JsonSerializer.Serialize(new
                {
                    Kind = "STATION_EVENT", Entry = entry
                }))).ToArray();
            File.WriteAllLines(chooser.FileName, lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyCanConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.CanExpectedConsent);

    private void CopyKeyConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.KeyExpectedConsent);

    private void CopyClampConfirmation_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.ClampExpectedConsent);

    private async void IgnOn_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyIgnSelectionAsync(true);
    private async void IgnOff_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyIgnSelectionAsync(false);

    private void OpenKeyActionEditor(ManualBenchViewModel.KeyActionEditor action)
    {
        if (!_model.CanEditKeyActions)
        {
            MessageBox.Show(this, "请先打开工程并停止正在运行的 KEY 动作。",
                "KEY 编辑不可用", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.SelectedKeyAction = action;
        if (_keyActionDialog is { IsVisible: true } existing)
        {
            existing.Activate();
            existing.Focus();
            return;
        }
        // The KEY editor is a persistent owned tool window, not a hover
        // overlay. Double-click and Edit both focus the SAME real editor.
        var popup = new ManualBenchKeyActionWindow(_model) { Owner = this };
        _keyActionDialog = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_keyActionDialog, popup))
                _keyActionDialog = null;
        };
        popup.Show();
        popup.Activate();
    }

    private void AddKeyAction_Click(object sender, RoutedEventArgs e)
    {
        var add = (ICommand)_model.AddKeyActionCommand;
        if (!add.CanExecute(null)) return;
        add.Execute(null);
        if (_model.SelectedKeyAction is { } action) OpenKeyActionEditor(action);
    }

    private void ManageKeyActions_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedKeyAction is { } selected)
            OpenKeyActionEditor(selected);
        else if (_model.KeyActions.FirstOrDefault() is { } first)
            OpenKeyActionEditor(first);
        else
            AddKeyAction_Click(sender, e);
    }

    private ManualBenchViewModel.KeyActionEditor? ActionForPadMenu(object sender)
    {
        if (sender is not MenuItem
            { Tag: ManualBenchViewModel.KeyActionPadItem pad }) return null;
        return _model.KeyActions.FirstOrDefault(x => x.ActionId == pad.ActionId);
    }

    private void EditKeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (ActionForPadMenu(sender) is { } action)
            OpenKeyActionEditor(action);
    }

    private void CopyKeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (ActionForPadMenu(sender) is not { } action) return;
        if (!_model.CanEditKeyActions)
        {
            MessageBox.Show(this, "请先停止当前操作，再复制 KEY 动作。",
                "KEY 动作编辑", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.SelectedKeyAction = action;
        var copyCommand = (ICommand)_model.CopyKeyActionCommand;
        if (!copyCommand.CanExecute(null)) return;
        copyCommand.Execute(null);
        if (_model.SelectedKeyAction is { } copy)
            OpenKeyActionEditor(copy);
    }

    private void PinKeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem
            { Tag: ManualBenchViewModel.KeyActionPadItem pad }) return;
        if (!_model.PinKeyPadCommand.CanExecute(pad))
        {
            MessageBox.Show(this,
                "先保存当前编辑草稿、停止 KEY 任务，再调整已保存按钮的顺序。",
                "KEY 按钮置顶", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.PinKeyPadCommand.Execute(pad);
    }

    private void DeleteKeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (ActionForPadMenu(sender) is not { } action) return;
        if (!_model.CanEditKeyActions)
        {
            MessageBox.Show(this, "请先停止当前操作，再删除动作。",
                "KEY 动作管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, "确定删除动作“" + action.Name +
                "”？删除后还需要保存动作库。",
                "删除 KEY 动作", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _model.SelectedKeyAction = action;
        var deleteCommand = (ICommand)_model.DeleteKeyActionCommand;
        if (deleteCommand.CanExecute(null)) deleteCommand.Execute(null);
    }

    private async void ApplyNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation action })
            await _model.ApplyNamedOperationAsync(action);
    }

    private void EditNamedOperation_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation action })
        {
            _model.SelectNamedOperation(action);
            e.Handled = true;
        }
    }

    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target) return target;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void ShowCanProperties(ManualBenchViewModel.BenchCanRow row)
    {
        if (!_model.CanEditCanRows || !row.CanEditFields)
        {
            MessageBox.Show(this, "当前 CAN 发送任务尚未结束，不能修改配置。",
                "CAN 编辑已锁定", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.SelectedCanRow = row;
        if (_canPropertyWindows.TryGetValue(row, out var existing) &&
            existing.IsVisible)
        {
            existing.Activate();
            existing.Focus();
            return;
        }
        // Each popup keeps its own pinned row. Selecting another CAN message
        // in the background must not redirect an in-progress DATA edit.
        var popup = new ManualBenchCanPropertiesWindow(_model, row)
        {
            Owner = this
        };
        _canPropertyWindows[row] = popup;
        popup.Closed += (_, _) =>
        {
            if (_canPropertyWindows.TryGetValue(row, out var current) &&
                ReferenceEquals(current, popup))
                _canPropertyWindows.Remove(row);
        };
        popup.Show();
        popup.Activate();
    }

    private void AddCanRow_Click(object sender, RoutedEventArgs e)
    {
        var add = (ICommand)_model.AddCanRowCommand;
        if (!add.CanExecute(null)) return;
        add.Execute(null);
        if (_model.SelectedCanRow is { } row)
            ShowCanProperties(row);
    }

    private void OpenCanQuickEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ManualBenchViewModel.BenchCanRow row })
            ShowCanProperties(row);
    }

    private void CanSendGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<DataGridRow>(e.OriginalSource as DependencyObject) is not
            { Item: ManualBenchViewModel.BenchCanRow row } container)
            return;
        if (Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        ShowCanProperties(row);
        e.Handled = true;
    }

    private void CanSendGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<DataGridRow>(e.OriginalSource as DependencyObject) is
            { Item: ManualBenchViewModel.BenchCanRow row })
            _model.SelectedCanRow = row;
    }

    private void CanProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCanRow is { } row)
            ShowCanProperties(row);
    }

    private void CanQuickEditMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCanRow is { } row)
            ShowCanProperties(row);
    }

    private void MonitorGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<DataGridRow>(e.OriginalSource as DependencyObject) is
            { Item: ManualBenchViewModel.MonitorEntry row })
            CanMonitorGrid.SelectedItem = row;
    }

    private void CopyMonitorFrame_Click(object sender, RoutedEventArgs e)
    {
        if (CanMonitorGrid.SelectedItem is ManualBenchViewModel.MonitorEntry entry)
            Clipboard.SetText(entry.FullFrame);
    }

    private void CopyMonitorData_Click(object sender, RoutedEventArgs e)
    {
        if (CanMonitorGrid.SelectedItem is ManualBenchViewModel.MonitorEntry entry)
            Clipboard.SetText(entry.Data);
    }

    private async void FixturePress_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyFixtureAsync(true);
    private async void FixtureRelease_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyFixtureAsync(false);

    private void SelectNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation operation })
            _model.SelectNamedOperation(operation);
    }
}
