using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>P11 uses P10's actual project-managed test document and runner, not a second Mock workspace.</summary>
public partial class R1ProjectRunView : UserControl
{
    public R1ProjectRunView(LabTestWorkspaceViewModel model)
    {
        InitializeComponent();
        DataContext = model ?? throw new ArgumentNullException(nameof(model));
    }

    private void StepTree_SelectionChanged(object sender,
        RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is LabTestWorkspaceViewModel vm)
            vm.Select(args.NewValue as LabTreeRow);
    }
}
