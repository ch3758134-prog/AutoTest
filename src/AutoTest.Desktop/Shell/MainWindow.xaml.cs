using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.RestoreLastProjectAsync();
        Closing += OnWindowClosing;
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
