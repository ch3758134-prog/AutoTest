using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AutoTest.Desktop.Diagnostics;
using AutoTest.Desktop.Projects;

namespace AutoTest.Desktop.Shell.Navigation;

/// <summary>
/// The single ordered TEST list, not a general project file explorer.
/// Project resources remain reachable through specialist engineering tools.
/// </summary>
public partial class ProjectExplorerView : UserControl
{
    public ProjectExplorerView() => InitializeComponent();

    private async void CaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            CaseList.SelectedItem is not ResourceTreeNode { Asset: not null } node)
            return;
        vm.SelectResource(node);
        try { await vm.OpenSelectedResourceAsync(); }
        catch (Exception error)
        {
            UiExceptionReporter.Record("TEST case navigation", error);
            MessageBox.Show("测试用例打开失败：" + error.Message,
                "AutoTest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CaseSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CaseList is null) return;
        var view = CollectionViewSource.GetDefaultView(CaseList.ItemsSource);
        if (view is null) return;
        var query = CaseSearch.Text.Trim();
        view.Filter = item => item is ResourceTreeNode node &&
            (query.Length == 0 ||
             node.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private async void PublicFlow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ResourceTreeNode { Asset: not null } node } ||
            DataContext is not MainWindowViewModel vm) return;
        vm.SelectResource(node);
        try { await vm.OpenSelectedResourceAsync(); }
        catch (Exception error)
        {
            UiExceptionReporter.Record("Public flow navigation", error);
            MessageBox.Show("公共流程打开失败：" + error.Message,
                "AutoTest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CaseList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || CaseList.SelectedItem is not ResourceTreeNode
            { Asset: not null } node || DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        vm.SelectResource(node);
        if (vm.RenameResourceCommand.CanExecute())
            vm.RenameResourceCommand.Execute();
    }
}