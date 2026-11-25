using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One persistent, non-transmitting CAN property window pinned to its original
/// bench row. Edits auto-save into the project CAN draft, not USB hardware.
/// </summary>
public partial class ManualBenchCanPropertiesWindow : Window
{
    private readonly ManualBenchViewModel _model;
    private readonly ManualBenchViewModel.BenchCanRow _row;
    private readonly DispatcherTimer _autosave = new()
        { Interval = TimeSpan.FromSeconds(7) };
    private Task? _autosaveTask;
    private bool _autosaveBusy;
    private bool _closingForSave;
    private bool _closeApproved;
    private DateTime _lastInteractionUtc = DateTime.UtcNow;

    public ManualBenchCanPropertiesWindow(ManualBenchViewModel model,
        ManualBenchViewModel.BenchCanRow? editingRow = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _row = editingRow ?? model.SelectedCanRow ??
            throw new InvalidOperationException("请先选择 CAN 报文。");
        InitializeComponent();
        Title = "CAN 属性 · " + _row.Name;
        var editor = new ManualBenchCanPropertiesView(_model, _row);
        editor.CloseRequested += (_, _) => Close();
        EditorHost.Content = editor;
        Loaded += (_, _) => DesktopWindowSizing.FitToVisibleWorkspace(this);
        Loaded += (_, _) => _autosave.Start();
        PreviewMouseDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewTextInput += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        _autosave.Tick += OnAutoSaveTick;
        Closing += ClosingGuard;
        _model.CanSendRows.CollectionChanged += OnCanRowsChanged;
        Closed += (_, _) =>
        {
            _autosave.Stop();
            _model.CanSendRows.CollectionChanged -= OnCanRowsChanged;
        };
    }

    public ManualBenchViewModel.BenchCanRow EditingRow => _row;

    private void OnCanRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_closingForSave || _closeApproved || _model.CanSendRows.Contains(_row)) return;
        // The row was deleted/reloaded by the parent workbench. Do not allow
        // an orphaned editor to masquerade as an active CAN send configuration.
        _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
    }

    private async void OnAutoSaveTick(object? sender, EventArgs e)
    {
        if (_autosaveBusy || _closingForSave || !IsActive || !IsEnabled ||
            !_model.CanRowsDirty ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(3)) return;
        _autosaveBusy = true;
        try
        {
            var pending = _model.SaveCanRowsForCloseAsync();
            _autosaveTask = pending;
            await pending;
            SaveFailureText.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace.TraceWarning("CAN editor autosave failed: " + error);
            SaveFailureText.Text = "CAN 配置自动保存失败：" + error.Message;
            SaveFailureText.Visibility = Visibility.Visible;
        }
        finally { _autosaveTask = null; _autosaveBusy = false; }
    }

    private async Task PersistEditorAsync()
    {
        _autosave.Stop();
        if (_autosaveTask is { } inFlight) await inFlight;
        if (_model.CanRowsDirty) await _model.SaveCanRowsForCloseAsync();
        if (_model.CanRowsDirty)
            throw new InvalidOperationException("CAN 报文配置仍有未保存修改。");
    }

    public async Task<bool> PrepareForParentShutdownAsync()
    {
        if (_closingForSave) return false;
        _closingForSave = true;
        IsEnabled = false;
        try
        {
            await PersistEditorAsync();
            _closeApproved = true;
            Close();
            return !IsVisible;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SaveFailureText.Text = "CAN 编辑未保存，工作台未关闭：" + error.Message;
            SaveFailureText.Visibility = Visibility.Visible;
            return false;
        }
        finally
        {
            _closingForSave = false;
            if (!_closeApproved)
            {
                IsEnabled = true;
                if (IsLoaded) _autosave.Start();
            }
        }
    }

    private async void ClosingGuard(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        if (_closingForSave) return;
        _closingForSave = true;
        IsEnabled = false;
        try
        {
            await PersistEditorAsync();
            _closeApproved = true;
            // Do not recursively close while WPF is dispatching Closing.
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SaveFailureText.Text = "CAN 配置未保存，请修正并重试：" + error.Message;
            SaveFailureText.Visibility = Visibility.Visible;
        }
        finally
        {
            _closingForSave = false;
            if (!_closeApproved)
            {
                IsEnabled = true;
                if (IsLoaded) _autosave.Start();
            }
        }
    }
}
