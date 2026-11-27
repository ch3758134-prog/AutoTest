using System.ComponentModel;
using System.Collections.Specialized;
using AutoTest.Desktop.Testing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _autosaveTickActive;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private Task? _shutdownTask;
    private bool _closeAttemptPending;
    private bool _bottomVisible; // On entry: no real event rows => dock closed, tab remains recoverable.
    private bool _inspectorVisible; // Full-width test table first; inspector is opt-in.
    private bool _restoringDocumentTab;
    private bool _inspectorManualLayout;
    private bool _bottomManualLayout;
    private bool _treeAutoNarrow;
    private TestEditorViewModel? _activeRunFacts;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnShellSizeChanged;
        Loaded += async (_, _) =>
        {
            await _viewModel.RestoreAutomaticTestAtStartupAsync();
            _viewModel.StartAutomaticUsbConnection();
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
        if (_autosaveTickActive || _shutdownStarted || _closeAttemptPending ||
            DateTime.UtcNow - _lastInteractionUtc < TimeSpan.FromSeconds(3))
            return;
        _autosaveTickActive = true;
        try { await _viewModel.AutoSaveOpenEditorsAsync(); }
        catch (Exception error) { Trace.TraceWarning("AutoSave WPF timer error: " + error); }
        finally { _autosaveTickActive = false; }
    }

    // WindowStyle=None + WindowChrome does not guarantee work-area maximization
    // on every DPI/taskbar configuration. Windows supplies physical monitor
    // coordinates here, so never convert them to WPF DIPs.
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    private HwndSource? _windowSource;
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WorkAreaWindowProc);
    }

    private IntPtr WorkAreaWindowProc(IntPtr hwnd, int message, IntPtr wParam,
        IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var target = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
        target.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        target.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        target.MaxSize.X = info.Work.Right - info.Work.Left;
        target.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        Marshal.StructureToPtr(target, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        // Closing is synchronous in WPF. Do not destroy the window, USB session,
        // or evidence state while an async physical cleanup is still pending.
        e.Cancel = true;
        if (_closeAttemptPending) return;
        _closeAttemptPending = true;
        try
        {
            if (_shutdownTask is null)
            {
                _viewModel.BeginUsbShutdownPreparation();
                if (!await _viewModel.PrepareEditorsForCloseAsync()) return;
                if (!await _viewModel.PrepareToolWindowsForCloseAsync(exitingApplication: true)) return;
                _shutdownStarted = true;
                _autosaveTimer.Stop();
                // Keep ONE task. On a timeout a second click observes this same
                // shutdown, rather than trying to dispose the USB twice.
                _shutdownTask = _viewModel.ShutdownAsync();
            }
            try
            {
                await _shutdownTask.WaitAsync(TimeSpan.FromSeconds(6));
            }
            catch (TimeoutException error)
            {
                Trace.TraceError("AutoTest shutdown still pending after six seconds: " + error);
                MessageBox.Show(this,
                    "设备和日志清理尚未完成，不能确认 USB 已安全释放。\n" +
                    "主程序将保持打开，不会强制退出或冒称设备已经断开。\n" +
                    "请检查工位实际状态后，再点击关闭以继续等待同一收尾任务。",
                    "退出等待设备收尾", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Trace.TraceError("AutoTest cleanup failed; exit blocked: " + error);
                MessageBox.Show(this,
                    "主控或日志收尾失败，设备状态 UNKNOWN；没有强制结束进程。\n" +
                    error.Message + "\n请先确认现场安全，保留错误日志供诊断。",
                    "无法确认安全退出", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _shutdownComplete = true;
            global::System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace.TraceError("AutoTest exit guard failure: " + error);
            MessageBox.Show(this, "退出检查发生异常，窗口未关闭：\n" + error.Message,
                "退出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (_shutdownTask is null && !_shutdownStarted)
                _viewModel.CancelUsbShutdownPreparation();
            _closeAttemptPending = false;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The default automatic-test surface is read-only. Hidden engineer
        // Ctrl+N/O/S bindings must not create/open/write resources from here.
        // The full shortcut set continues to work after switching to Studio.
        if (_viewModel.IsProductionWorkspace &&
            ((Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
              e.Key is Key.N or Key.O or Key.S or Key.Z or Key.Y or Key.Space) ||
             e.Key is Key.F2 or Key.F9 or Key.Insert or Key.Delete))
        {
            e.Handled = true;
            return;
        }
        if (e.SystemKey == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            e.Handled = true;
            Close();
        }
    }

    private void ShowOverview_Click(object sender, RoutedEventArgs e) => _viewModel.ShowOverview();

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
        var hasRealFacts = ActualHeight >= 690 && _activeRunFacts?.RunFacts.Count > 0;
        if (_bottomVisible == hasRealFacts) return;
        _bottomVisible = hasRealFacts; // only real TEST evidence; no simulated PASS
        ApplyPanelLayout();
    }

    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Dynamic toolbar is optional chrome. Every command remains in the
        // native File/Edit/Run/Tools menu at high DPI or limited desktop height.
        _viewModel.SetCompactShell(ActualWidth < 1150 || ActualHeight < 625);
        // 1366×768 at 150% provides fewer than 920×500 effective DIPs.
        // Preserve the editing surface instead of squeezing controls/fonts.
        if (!_inspectorManualLayout) _inspectorVisible = false;
        if (!_bottomManualLayout)
            _bottomVisible = ActualHeight >= 690 && _activeRunFacts?.RunFacts.Count > 0;
        if (ActualWidth < 1050 && !_treeAutoNarrow && TreeColumn.Width.Value > 240)
        {
            TreeColumn.Width = new GridLength(230);
            _treeAutoNarrow = true;
        }
        else if (ActualWidth >= 1150 && _treeAutoNarrow)
        {
            TreeColumn.Width = new GridLength(266);
            _treeAutoNarrow = false;
        }
        ApplyPanelLayout();
    }

    private void ApplyPanelLayout()
    {
        // Inspection preparation already contains its own full-width editors
        // (scanner, FLASH entry and fixture procedure). A redundant shell-side
        // Inspector would unnecessarily compress those configuration tabs.
        var content = _viewModel.ActiveEditorTab?.View;
        // Project home/resources are full-width task surfaces, not step editors.
        // They must not inherit an empty 410px inspector or an irrelevant CAN
        // observation tray from the last TEST editor.
        var isProjectLanding = content is ProjectOverviewView or
            EngineeringResourcesView or ProjectConfigurationOverviewView;
        var showInspector = _inspectorVisible && !isProjectLanding &&
            (_viewModel.ActiveEditorTab?.Inspector is not null);
        InspectorColumn.Width = new GridLength(
            showInspector ? Math.Clamp(ActualWidth * 0.28, 350, 440) : 0);
        InspectorSplitterColumn.Width = new GridLength(showInspector ? 4 : 0);
        var showObservation = _bottomVisible && !isProjectLanding;
        BottomRow.Height = new GridLength(showObservation ? 180 : 0);
        BottomSplitterRow.Height = new GridLength(showObservation ? 4 : 0);
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
