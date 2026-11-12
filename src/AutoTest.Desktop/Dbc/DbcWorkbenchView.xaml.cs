using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Dbc;

public partial class DbcWorkbenchView : UserControl
{
    private readonly DbcWorkbenchViewModel _viewModel;

    public DbcWorkbenchView(DbcWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }


    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T matched) return matched;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private void MessageList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null &&
            _viewModel.CanEdit && !_viewModel.IsBusy)
            _viewModel.EditMessage();
    }

    private void SignalGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not null &&
            _viewModel.CanEdit && !_viewModel.IsBusy)
            _viewModel.EditSignal();
    }
}
