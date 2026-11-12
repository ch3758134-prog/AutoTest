using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace AutoTest.Desktop.Dbc;

public partial class DbcWorkbenchWindow : Window
{
    private readonly DbcWorkbenchViewModel _viewModel;

    public DbcWorkbenchWindow(DbcWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose()) e.Cancel = true;
    }

    private void MessageList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CanEdit && !_viewModel.IsBusy) _viewModel.EditMessage();
    }

    private void SignalGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CanEdit && !_viewModel.IsBusy) _viewModel.EditSignal();
    }
}
