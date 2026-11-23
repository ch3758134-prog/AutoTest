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
            // Do not clear a Setup selection before restoring it: doing so
            // can recursively fire TreeView.SelectedItemChanged.
            if (node.Role is "Root" or "Setup")
            {
                vm.SelectResource(node);
                vm.ShowProjectConfiguration();
            }
            else
                vm.SelectResource(null);
            return;
        }
        vm.SelectResource(node);
        // Each real resource opens on one click. The DBC workbench is an
        // owned nonmodal tool window, not a modal dialog.
        try { await vm.OpenSelectedResourceAsync(); }
        catch (Exception ex) { UiExceptionReporter.Record("Explorer / open asset", ex); }
    }

    // Incremental highlight only. The tree remains complete and no editor is
    // opened by typing a partial match or pressing Enter.
    private void ResourceSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.HighlightResourceSearch(ResourceSearch.Text);
    }

    private async void AddResource_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: ResourceTreeNode node } &&
            DataContext is MainWindowViewModel vm)
            await vm.CreateFromCategoryAsync(node);
    }

    private void Navigator_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || Navigator.SelectedItem is not ResourceTreeNode { Asset: not null } node ||
            DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        vm.SelectResource(node);
        if (vm.RenameResourceCommand.CanExecute())
            vm.RenameResourceCommand.Execute();
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
