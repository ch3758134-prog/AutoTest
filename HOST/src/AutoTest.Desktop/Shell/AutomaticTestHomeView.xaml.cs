using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only production operator hierarchy. Clicking a node displays
/// authoring structure only; no step is executed or modified by selection.
/// </summary>
public partial class AutomaticTestHomeView : UserControl
{
    public AutomaticTestHomeView() => InitializeComponent();

    private void CasesTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is AutomaticTestHomeViewModel vm)
            vm.SelectOperatorNode(e.NewValue);
    }
}
