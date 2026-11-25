using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Edits the same project-backed KEY action model as the manual bench.
/// This is an editor only: it does not issue any device writes.
/// </summary>
public partial class ManualBenchKeyActionWindow : Window
{
    private readonly ManualBenchViewModel _model;
    private readonly DispatcherTimer _autosaveTimer = new()
        { Interval = TimeSpan.FromSeconds(7) };
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _autosaveBusy;
    private Task? _autosaveTask;
    private bool _closingForSave;
    private bool _closeApproved;

    public ManualBenchKeyActionWindow(ManualBenchViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = model;
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        _model.IsKeyEditorExpanded = true;
        PreviewMouseDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewTextInput += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += (_, _) => _autosaveTimer.Start();
        Closed += (_, _) =>
        {
            _autosaveTimer.Stop();
            _model.IsKeyEditorExpanded = false;
        };
        Closing += OnClosing;
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_autosaveBusy || _closingForSave || !IsActive || !IsEnabled ||
            !_model.CanSaveKeyActions ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(4) ||
            Keyboard.FocusedElement is TextBoxBase) return;
        _autosaveBusy = true;
        try
        {
            // Closing must await this SAME persistence task, not race it with
            // a second SaveKeyActionsForCloseAsync against the same revision.
            var persistence = _model.SaveKeyActionsForCloseAsync();
            _autosaveTask = persistence;
            await persistence;
        }
        catch (Exception error)
        { System.Diagnostics.Trace.TraceWarning("KEY editor autosave failed: " + error); }
        finally { _autosaveTask = null; _autosaveBusy = false; }
    }

    /// <summary>
    /// Parent workbench must close this persistent editor BEFORE it tears
    /// down the shared KEY ViewModel and the hardware session. Await any
    /// pending timer autosave, then persist the latest valid editor draft.
    /// </summary>
    public async Task<bool> PrepareForParentShutdownAsync()
    {
        if (_closingForSave) return false;
        _closingForSave = true;
        _autosaveTimer.Stop();
        IsEnabled = false;
        try
        {
            if (_autosaveTask is { } pending) await pending;
            if (_model.KeyActionsDirty)
                await _model.SaveKeyActionsForCloseAsync();
            if (_model.KeyActionsDirty)
                throw new InvalidOperationException("KEY 动作仍有未保存修改，保留编辑窗口与工程。");
            _closeApproved = true;
            Close();
            return !IsVisible;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            MessageBox.Show(this, "KEY 动作未能完整保存：\n" + error.Message,
                "保留当前编辑", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _closingForSave = false;
            if (!_closeApproved)
            {
                IsEnabled = true;
                if (IsLoaded) _autosaveTimer.Start();
            }
        }
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e) => Close();
    private void CloseKeyEditor_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        if (!_model.KeyActionsDirty && !_autosaveBusy) return;
        e.Cancel = true;
        if (_closingForSave) return;
        _closingForSave = true;
        IsEnabled = false;
        try
        {
            _autosaveTimer.Stop();
            if (_autosaveTask is { } inFlight) await inFlight;
            if (_model.KeyActionsDirty)
                await _model.SaveKeyActionsForCloseAsync();
            if (_model.KeyActionsDirty)
                throw new InvalidOperationException("动作库仍有未保存修改，请先修正无效字段。");
            // The original WPF Closing event was cancelled. Close on the
            // next dispatcher turn, not within the continuation of Closing.
            _closeApproved = true;
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "KEY 动作保存未完成，编辑内容仍保留：\n" +
                error.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _closingForSave = false;
            if (!_closeApproved)
            {
                IsEnabled = true;
                if (IsLoaded) _autosaveTimer.Start();
            }
        }
    }
}
