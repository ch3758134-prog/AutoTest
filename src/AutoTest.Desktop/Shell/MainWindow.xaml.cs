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
        e.Cancel = true;
        if (_shutdownStarted || !_viewModel.ConfirmCloseActiveEditor()) return;
        _shutdownStarted = true;
        try { await _viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(6)); }
        catch (TimeoutException ex) { Trace.TraceError("AutoTest shutdown timed out: " + ex); }
        catch (Exception ex) { Trace.TraceError("AutoTest shutdown failed: " + ex); }
        finally
        {
            _shutdownComplete = true;
            Application.Current.Shutdown(0);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

    private void CloseEditorTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: EditorTabModel tab })
            _viewModel.CloseEditor(tab);
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
