using System.ComponentModel;
using System.Collections.Specialized;
using AutoTest.Desktop.Testing;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One WPF shell and one real device session. Chrome, keyboard focus, dock sizing and
/// shutdown are presentation concerns; service authorization remains in the VM.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _autosaveTickActive;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _closeAttemptPending;
    private bool _bottomVisible; // On entry: no real event rows => dock closed, tab remains recoverable.
    private bool _inspectorVisible = true;
    private bool _restoringDocumentTab;
    private bool _inspectorManualLayout;
    private bool _bottomManualLayout;
    private bool _treeAutoNarrow;
    private bool _projectTreeVisible = true;
    private TestEditorViewModel? _activeRunFacts;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SizeChanged += OnShellSizeChanged;
        Loaded += async (_, _) =>
        {
            await _viewModel.RestoreLastProjectAsync();
            _autosaveTimer.Start();
        };
        _autosaveTimer.Tick += OnAutosaveTick;
        PreviewMouseDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        PreviewTextInput += (_, _) => _lastInteractionUtc = DateTime.UtcNow;
        Closing += OnWindowClosing;
        _viewModel.PropertyChanged += OnVmPropertyChanged;
        Closed += (_, _) =>
        {
            _autosaveTimer.Stop();
            _viewModel.PropertyChanged -= OnVmPropertyChanged;
            if (_activeRunFacts is not null)
                _activeRunFacts.RunFacts.CollectionChanged -= OnRunFactsChanged;
        };
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_autosaveTickActive || _shutdownStarted || _closeAttemptPending || !IsActive ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(4) ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            return;
        _autosaveTickActive = true;
        try { await _viewModel.AutoSaveOpenEditorsAsync(); }
        catch (Exception error) { Trace.TraceWarning("AutoSave WPF timer error: " + error); }
        finally { _autosaveTickActive = false; }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        // WPF's Closing event is synchronous; cancel this attempt until explicit
        // draft decisions and real async saves are complete. A second click cannot
        // race the first prompt or start another hardware shutdown.
        e.Cancel = true;
        if (_shutdownStarted || _closeAttemptPending) return;
        _closeAttemptPending = true;
        try
        {
            if (!await _viewModel.PrepareEditorsForCloseAsync()) return;
            _shutdownStarted = true;
            try
            {
                await _viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(6));
            }
            catch (TimeoutException error)
            {
                Trace.TraceError("AutoTest shutdown exceeded the bounded wait: " + error);
            }
            catch (Exception error)
            {
                Trace.TraceError("AutoTest shutdown encountered a cleanup error: " + error);
            }
            finally
            {
                _shutdownComplete = true;
                Application.Current.Shutdown(0);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace.TraceError("AutoTest exit guard failure: " + error);
            MessageBox.Show(this, "退出检查发生异常，窗口未关闭：\n" + error.Message,
                "退出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!_shutdownStarted) _closeAttemptPending = false;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.SystemKey == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            e.Handled = true;
            Close();
        }
    }

    private void ShowOverview_Click(object sender, RoutedEventArgs e) => _viewModel.ShowOverview();

    private void ScannerEntry_TextInput(object sender, TextCompositionEventArgs e)
    {
        _viewModel.CaptureScannerText(e.Text);
        ScannerEntry.Text += e.Text;
        ScannerEntry.CaretIndex = ScannerEntry.Text.Length;
        e.Handled = true;
    }
    private async void ScannerEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        ScannerEntry.Clear();
        await _viewModel.FinishScannerInputAsync();
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringDocumentTab || !ReferenceEquals(e.OriginalSource, DocumentTabs)) return;
        // Binding can optimistically change the selected TabItem even when
        // the edit session rejects navigation due to invalid pending input.
        // Restore the visual selection after WPF has completed its binding pass.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(DocumentTabs.SelectedItem, _viewModel.ActiveEditorTab)) return;
            _restoringDocumentTab = true;
            try { DocumentTabs.SelectedItem = _viewModel.ActiveEditorTab; }
            finally { _restoringDocumentTab = false; }
        }), DispatcherPriority.Background);
    }

    private async void CloseEditorTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: EditorTabModel tab })
            await _viewModel.CloseEditorAsync(tab);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ActiveEditorTab)) return;
        var current = _viewModel.ActiveEditorTab?.Editor as TestEditorViewModel;
        if (ReferenceEquals(_activeRunFacts, current)) return;
        if (_activeRunFacts is not null)
            _activeRunFacts.RunFacts.CollectionChanged -= OnRunFactsChanged;
        _activeRunFacts = current;
        if (current is not null)
            current.RunFacts.CollectionChanged += OnRunFactsChanged;
        OpenDockForRealEvents();
        ApplyPanelLayout(); // no editor: do not reserve an empty 320 DIP Inspector
    }

    private void OnRunFactsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) OpenDockForRealEvents();
        else Dispatcher.BeginInvoke(new Action(OpenDockForRealEvents));
    }

    private void OpenDockForRealEvents()
    {
        if (_bottomManualLayout) return; // the operator's explicit choice wins
        var hasRealFacts = _activeRunFacts?.RunFacts.Count > 0;
        if (_bottomVisible == hasRealFacts) return;
        _bottomVisible = hasRealFacts; // only real TEST evidence; no simulated PASS
        ApplyPanelLayout();
    }

    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 1366×768 at 150% provides fewer than 920×500 effective DIPs.
        // Preserve the editing surface instead of squeezing controls/fonts.
        if (!_inspectorManualLayout) _inspectorVisible = ActualWidth >= 1120;
        if (!_bottomManualLayout)
            _bottomVisible = _activeRunFacts?.RunFacts.Count > 0;
        if (_projectTreeVisible && ActualWidth < 1050 && !_treeAutoNarrow &&
            TreeColumn.Width.Value > 235)
        {
            TreeColumn.Width = new GridLength(225);
            _treeAutoNarrow = true;
        }
        else if (_projectTreeVisible && ActualWidth >= 1150 && _treeAutoNarrow)
        {
            TreeColumn.Width = new GridLength(280);
            _treeAutoNarrow = false;
        }
        ApplyPanelLayout();
    }

    private void ApplyPanelLayout()
    {
        // The workbench has task-first navigation; only the TEST authoring task
        // needs the engineering resource tree. Other tasks can hide it entirely.
        TreeColumn.Width = new GridLength(!_projectTreeVisible ? 0 :
            (_treeAutoNarrow ? 225 : Math.Max(235, TreeColumn.Width.Value)));
        TreeSplitterColumn.Width = new GridLength(_projectTreeVisible ? 5 : 0);
        // DBC and Controller configurations already own their actual editor.
        // Only reserve the right rail when this document supplies an InspectorHost.
        var showInspector = _inspectorVisible &&
            (_viewModel.ActiveEditorTab?.Inspector is not null);
        InspectorColumn.Width = new GridLength(showInspector ? 320 : 0);
        InspectorSplitterColumn.Width = new GridLength(showInspector ? 4 : 0);
        BottomRow.Height = new GridLength(_bottomVisible ? 180 : 0);
        BottomSplitterRow.Height = new GridLength(_bottomVisible ? 4 : 0);
    }

    private void ShowTestWorkspace_Click(object sender, RoutedEventArgs e)
    {
        _projectTreeVisible = true;
        _treeAutoNarrow = ActualWidth < 1050;
        TreeColumn.Width = new GridLength(_treeAutoNarrow ? 225 : 280);
        ApplyPanelLayout();
    }

    private void ToggleProjectTree_Click(object sender, RoutedEventArgs e)
    {
        _projectTreeVisible = !_projectTreeVisible;
        if (_projectTreeVisible)
        {
            _treeAutoNarrow = ActualWidth < 1050;
            TreeColumn.Width = new GridLength(_treeAutoNarrow ? 225 : 280);
        }
        ApplyPanelLayout();
    }

    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        _bottomManualLayout = true;
        _bottomVisible = !_bottomVisible;
        ApplyPanelLayout();
    }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        _inspectorManualLayout = true;
        _inspectorVisible = !_inspectorVisible;
        ApplyPanelLayout();
    }
}
