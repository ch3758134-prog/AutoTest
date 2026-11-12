using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _shutdownStarted;
    private bool _shutdownComplete;

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
        try
        {
            // Blocking USB/native cleanup must never leave the WPF shell permanently open.
            await _viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(6));
        }
        catch (TimeoutException error)
        {
            Trace.TraceError("AutoTest shutdown timed out: " + error);
        }
        catch (Exception error)
        {
            Trace.TraceError("AutoTest shutdown failed: " + error);
        }
        finally
        {
            _shutdownComplete = true;
            // Close owned WPF windows even if a device's cleanup exceeded the deadline.
            Application.Current.Shutdown(0);
        }
    }

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

    private async void ResourceTree_Create_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: ResourceTreeNode node })
            await _viewModel.CreateFromCategoryAsync(node);
    }

    private void ResourceTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectResource(e.NewValue as ResourceTreeNode);
    }

    private void ResourceTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedResourceIsTestProgram &&
            ((ICommand)_viewModel.OpenTestProgramCommand).CanExecute(null))
        {
            ((ICommand)_viewModel.OpenTestProgramCommand).Execute(null);
            e.Handled = true;
            return;
        }
        if (_viewModel.SelectedResourceIsConfiguration &&
            ((ICommand)_viewModel.OpenConfigurationCommand).CanExecute(null))
        {
            ((ICommand)_viewModel.OpenConfigurationCommand).Execute(null);
            e.Handled = true;
            return;
        }
        if (_viewModel.SelectedResourceIsDbc &&
            ((ICommand)_viewModel.OpenDbcCommand).CanExecute(null))
        {
            ((ICommand)_viewModel.OpenDbcCommand).Execute(null);
            e.Handled = true;
        }
    }

    private void ResourceTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not TreeViewItem)
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        if (node is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }
}
