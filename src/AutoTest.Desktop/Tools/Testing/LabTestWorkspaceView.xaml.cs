using System.Windows;
using System.Windows.Controls;

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
}
