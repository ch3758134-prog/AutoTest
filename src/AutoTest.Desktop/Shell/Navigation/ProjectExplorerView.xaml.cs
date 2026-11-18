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

    // Only navigates canonical AssetEntry references; never creates search copies.
    private async void ResourceSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Return or Key.Enter) ||
            DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        var query = ResourceSearch.Text.Trim();
        if (query.Length == 0) return;
        static IEnumerable<ResourceTreeNode> All(IEnumerable<ResourceTreeNode> roots)
        {
            foreach (var root in roots)
            {
                yield return root;
                foreach (var child in All(root.Children)) yield return child;
            }
        }
        var found = All(vm.ResourceNodes).FirstOrDefault(n => n.Asset is not null &&
            (n.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             n.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        if (found is null)
        {
            ResourceSearch.ToolTip = "当前工程中没有匹配的正式资源：" + query;
            return;
        }
        ResourceSearch.ToolTip = found.Label;
        vm.SelectResource(found);
        try { await vm.OpenSelectedResourceAsync(); }
        catch (Exception error) { UiExceptionReporter.Record("Explorer / search resource", error); }
    }

    private async void AddResource_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: ResourceTreeNode node } &&
            DataContext is MainWindowViewModel vm)
            await vm.CreateFromCategoryAsync(node);
    }

    private void NodeText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2 ||
            sender is not FrameworkElement { DataContext: ResourceTreeNode node } ||
            node.Asset is null ||
            DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        vm.SelectResource(node);
        if (vm.RenameResourceCommand.CanExecute())
            vm.RenameResourceCommand.Execute();
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
