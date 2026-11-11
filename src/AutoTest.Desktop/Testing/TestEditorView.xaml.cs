using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTest.Desktop.Shell;
namespace AutoTest.Desktop.Testing;
public partial class TestEditorView : UserControl
{
 public TestEditorView(AutoTest.Desktop.Shell.MainWindowViewModel vm) { InitializeComponent(); DataContext = vm; }
 private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
 private void OnStepRightClick(object sender, MouseButtonEventArgs e)
 {
     // WPF does not reliably select a ListBox row on right-click.
     // Operate on the item underneath the pointer, never a prior selection.
     if (sender is not ListBox list || e.OriginalSource is not DependencyObject source) return;
     if (ItemsControl.ContainerFromElement(list, source) is ListBoxItem row)
         list.SelectedItem = row.DataContext;
 }

 private void OnDoubleClick(object sender, MouseButtonEventArgs e) { if (Vm?.SelectedStep is not null) Vm.FocusNameCommand.Execute(); }
 private void InsertBefore_Click(object sender, RoutedEventArgs e) => Vm?.InsertBeforeCommand.Execute();
 private void InsertAfter_Click(object sender, RoutedEventArgs e) => Vm?.InsertAfterCommand.Execute();
 private void InsertChild_Click(object sender, RoutedEventArgs e) => Vm?.InsertChildCommand.Execute();
 private void InsertElse_Click(object sender, RoutedEventArgs e) => Vm?.InsertElseCommand.Execute();
 private void MoveUp_Click(object sender, RoutedEventArgs e) => Vm?.MoveUpCommand.Execute();
 private void MoveDown_Click(object sender, RoutedEventArgs e) => Vm?.MoveDownCommand.Execute();
 private void Copy_Click(object sender, RoutedEventArgs e) => Vm?.CopyCommand.Execute();
 private void Delete_Click(object sender, RoutedEventArgs e) => Vm?.DeleteCommand.Execute();
}
