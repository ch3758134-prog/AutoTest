using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Tools.Testing;

public partial class LabTestWorkspaceView : UserControl
{
    // DI/Prism composition remains with INTEGRATION; this is a standalone testable view.
    public LabTestWorkspaceView() : this(new LabTestWorkspaceViewModel()) { }
    public LabTestWorkspaceView(LabTestWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private void ProgramTree_OnSelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is LabTestWorkspaceViewModel vm)
            vm.Select(args.NewValue as LabTreeRow);
    }
    private void ProgramTree_OnRightClick(object sender, MouseButtonEventArgs e)
    {
        // A right-click must operate on the row under the pointer, not on a stale selection.
        // Walk to the nearest nested TreeViewItem so children do not select a parent.
        for (DependencyObject? visual = e.OriginalSource as DependencyObject;
             visual is not null;
             visual = VisualTreeHelper.GetParent(visual))
        {
            if (visual is not TreeViewItem item) continue;
            item.IsSelected = true;
            item.Focus();
            if (DataContext is LabTestWorkspaceViewModel vm)
                vm.Select(item.DataContext as LabTreeRow);
            break;
        }
    }

}
