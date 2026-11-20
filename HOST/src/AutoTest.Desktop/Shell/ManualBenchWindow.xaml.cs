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
        if (_keyAutosaveBusy || !IsActive || !IsEnabled || _closeAfterCanStopped ||
            _model.IsKeyActionRunning || !_model.CanSaveKeyActions ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(4) ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        _keyAutosaveBusy = true;
        try { await _model.SaveKeyActionsForCloseAsync(); }
        catch (Exception error)
        { System.Diagnostics.Trace.TraceWarning("KEY catalog autosave error: " + error); }
        finally { _keyAutosaveBusy = false; }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeAfterCanStopped ||
            (!_model.IsCanBatchRunning && !_model.IsKeyActionRunning && !_model.KeyActionsDirty)) return;
        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await _model.StopKeyActionAsync();
            await _model.StopCanBatchForCloseAsync();
            if (_model.KeyActionsDirty)
            {
                var choice = MessageBox.Show(this,
                    "按键动作有未保存的修改。\n是：保存并关闭；否：放弃草稿；取消：返回编辑。",
                    "保存 KEY 动作库", MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (choice == MessageBoxResult.Cancel) { IsEnabled = true; return; }
                if (choice == MessageBoxResult.Yes)
                {
                    await _model.SaveKeyActionsForCloseAsync();
                    if (_model.KeyActionsDirty)
                        throw new InvalidOperationException("工程动作库仍未保存，请查看工位日志。");
                }
            }
            _closeAfterCanStopped = true;
            Close();
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            MessageBox.Show(this, "关闭前收尾失败：" + ex.Message +
                "\n请核对主控真实输出，未确认时视为 UNKNOWN。",
                "手动台架状态未知", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private void SelectKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.KeyActionEditor action })
        {
            _model.SelectedKeyAction = action;
            _model.IsKeyEditorExpanded = true;
            Dispatcher.BeginInvoke(new Action(() => KeyEditorSection.BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void CloseKeyEditor_Click(object sender, RoutedEventArgs e) =>
        _model.IsKeyEditorExpanded = false;

    private void EditKeyAction_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { Tag: ManualBenchViewModel.KeyActionEditor action })
        {
            _model.SelectedKeyAction = action;
            _model.IsKeyEditorExpanded = true;
            Dispatcher.BeginInvoke(new Action(() => KeyEditorSection.BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Background);
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
        new ManualBenchCanPropertiesWindow(_model) { Owner = this }.ShowDialog();
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
