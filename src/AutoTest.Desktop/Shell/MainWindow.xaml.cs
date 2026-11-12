using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.RestoreLastProjectAsync();
    }

    private void ResourceTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectResource(e.NewValue as ResourceTreeNode);
    }

    private void ResourceTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
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
