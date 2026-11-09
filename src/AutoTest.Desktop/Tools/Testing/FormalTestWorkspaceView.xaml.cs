using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Testing;

public partial class FormalTestWorkspaceView : UserControl
{
    // The host/Prism Region registration is owned by INTEGRATION.
    public FormalTestWorkspaceView() : this(new FormalTestWorkspaceViewModel()) { }
    public FormalTestWorkspaceView(FormalTestWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is FormalTestWorkspaceViewModel vm)
            vm.Select(args.NewValue as FormalTestTreeRow);
    }
}
