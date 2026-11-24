using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Shell;

public partial class ManualBenchWindow : Window
{
    private readonly ManualBenchViewModel _model;
    private bool _closeAfterCanStopped;
    private readonly DispatcherTimer _keyAutosaveTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _keyAutosaveBusy;
    private bool _propertyOverlayClosing;
    private bool _benchClosePending;
    private bool _benchCloseScheduled;

    public ManualBenchWindow(ManualBenchViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = model;
        InitializeComponent();
        Closing += OnClosing;
        _keyAutosaveTimer.Tick += OnKeyAutosaveTick;
        Loaded += (_, _) => _keyAutosaveTimer.Start();
        PreviewMouseDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewTextInput += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        Closed += (_, _) => { _keyAutosaveTimer.Stop(); _model.Dispose(); };
    }

    private async void OnKeyAutosaveTick(object? sender, EventArgs e)
    {
        if (_keyAutosaveBusy || !IsActive || !IsEnabled || _benchClosePending || _closeAfterCanStopped ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(4)) return;
        if (!_model.CanRowsDirty && !_model.CanSaveKeyActions) return;
        _keyAutosaveBusy = true;
        try
        {
            if (_model.CanRowsDirty && !_model.IsCanBatchRunning)
                await _model.SaveCanRowsForCloseAsync();
            if (_model.CanSaveKeyActions && !_model.IsKeyActionRunning)
                await _model.SaveKeyActionsForCloseAsync();
        }
        catch (Exception error)
        { System.Diagnostics.Trace.TraceWarning("Manual bench draft autosave error: " + error); }
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

    private void OpenPropertyOverlay(UserControl content)
    {
        PropertyOverlayCard.MaxWidth = content is ManualBenchKeyActionView ? 710 : 1120;
        PropertyOverlayCard.MaxHeight = content is ManualBenchKeyActionView ? 620 : 760;
        PropertyOverlayContent.Content = content;
        PropertyOverlay.Visibility = Visibility.Visible;
        content.Focus();
    }

    private void PropertyOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only the dimmed empty area dismisses the editor. Clicking inside the
        // form does not close it or lose focus.
        if (!ReferenceEquals(e.OriginalSource, PropertyOverlay)) return;
        e.Handled = true;
        _ = ClosePropertyOverlayAsync();
    }

    private async Task ClosePropertyOverlayAsync()
    {
        if (_propertyOverlayClosing || PropertyOverlay.Visibility != Visibility.Visible)
            return;
        _propertyOverlayClosing = true;
        try
        {
            if (PropertyOverlayContent.Content is ManualBenchKeyActionView)
            {
                if (_model.KeyActionsDirty)
                {
                    await _model.SaveKeyActionsForCloseAsync();
                    if (_model.KeyActionsDirty)
                        throw new InvalidOperationException(
                            "KEY 动作包含未通过校验的修改，无法关闭编辑区。");
                }
                _model.IsKeyEditorExpanded = false;
            }
            PropertyOverlay.Visibility = Visibility.Collapsed;
            PropertyOverlayContent.Content = null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning("Bench property overlay save failed: " + error);
            MessageBox.Show(this, "保存属性失败，内容仍保留在编辑区：\n" + error.Message,
                "属性未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _propertyOverlayClosing = false; }
    }

    private void OpenKeyActionEditor(ManualBenchViewModel.KeyActionEditor action)
    {
        if (!_model.CanEditKeyActions)
        {
            MessageBox.Show(this, "请先打开工程并停止正在运行的 KEY 动作。",
                "KEY 编辑不可用", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.SelectedKeyAction = action;
        _model.IsKeyEditorExpanded = true;
        var view = new ManualBenchKeyActionView(_model);
        view.CloseRequested += async (_, _) => await ClosePropertyOverlayAsync();
        OpenPropertyOverlay(view);
    }

    private void AddKeyAction_Click(object sender, RoutedEventArgs e)
    {
        var add = (ICommand)_model.AddKeyActionCommand;
        if (!add.CanExecute(null)) return;
        add.Execute(null);
        if (_model.SelectedKeyAction is { } action) OpenKeyActionEditor(action);
    }

    private void SelectKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ManualBenchViewModel.KeyActionEditor action })
            OpenKeyActionEditor(action);
    }

    private void EditKeyAction_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: ManualBenchViewModel.KeyActionEditor action })
        {
            OpenKeyActionEditor(action);
            e.Handled = true;
        }
    }

    private void RunKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.KeyActionEditor action })
            _model.ExecuteKeyActionCommand.Execute(action);
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
        if (!_model.CanEditCanRows)
        {
            MessageBox.Show(this, "当前 CAN 发送任务尚未结束，不能修改配置。",
                "CAN 编辑已锁定", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _model.SelectedCanRow = row;
        var view = new ManualBenchCanPropertiesView(_model);
        view.CloseRequested += async (_, _) => await ClosePropertyOverlayAsync();
        OpenPropertyOverlay(view);
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

    private async void Clamp1Press_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(1, true);
    private async void Clamp1Release_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(1, false);
    private async void Clamp2Press_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(2, true);
    private async void Clamp2Release_Click(object sender, RoutedEventArgs e) =>
        await _model.ApplyClampSelectionAsync(2, false);

    private void SelectNamedOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.NamedOperation operation })
            _model.SelectNamedOperation(operation);
    }
}
