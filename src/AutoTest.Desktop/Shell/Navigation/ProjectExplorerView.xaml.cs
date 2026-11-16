using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell.Navigation;

/// <summary>HTML-parity project-first navigation with canonical AssetEntry dispatch.</summary>
public partial class ProjectExplorerView : UserControl
{
    public ProjectExplorerView() => InitializeComponent();

    private async void Navigator_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            e.NewValue is not ResourceTreeNode node) return;
        if (node.Asset is null)
        {
            // Section/root selections must not leave a stale asset selected:
            // the context menu could otherwise rename/remove the previous resource.
            vm.SelectResource(null);
            if (node.Role == "Root") vm.ShowOverview();
            return;
        }
        vm.SelectResource(node);
        try { await vm.OpenSelectedResourceAsync(); }
        catch (Exception ex) { UiExceptionReporter.Record("Explorer / open asset", ex); }
    }

    private async void AddResource_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: ResourceTreeNode node } &&
            DataContext is MainWindowViewModel vm)
            await vm.CreateFromCategoryAsync(node);
    }

    private void Navigator_RightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not TreeViewItem)
            current = current is Visual ? VisualTreeHelper.GetParent(current) :
                LogicalTreeHelper.GetParent(current);
        if (current is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }
}
