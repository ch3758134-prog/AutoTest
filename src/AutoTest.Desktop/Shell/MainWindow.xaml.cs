using System.ComponentModel;
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
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _closeAttemptPending;
    private bool _bottomVisible = true;
    private bool _inspectorVisible = true;
    private bool _restoringDocumentTab;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) => await _viewModel.RestoreLastProjectAsync();
        Closing += OnWindowClosing;
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
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

    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        _bottomVisible = !_bottomVisible;
        BottomRow.Height = new GridLength(_bottomVisible ? 180 : 0);
        BottomSplitterRow.Height = new GridLength(_bottomVisible ? 4 : 0);
    }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        _inspectorVisible = !_inspectorVisible;
        InspectorColumn.Width = new GridLength(_inspectorVisible ? 320 : 0);
        InspectorSplitterColumn.Width = new GridLength(_inspectorVisible ? 4 : 0);
    }
}
