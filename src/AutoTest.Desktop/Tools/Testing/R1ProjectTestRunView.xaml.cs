using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>The authoritative R1 P11 run pane shares P10's project-bound LAB model.</summary>
public partial class R1ProjectTestRunView : UserControl
{
    public R1ProjectTestRunView(LabTestWorkspaceViewModel model)
    {
        InitializeComponent();
        DataContext = model ?? throw new ArgumentNullException(nameof(model));
    }
}
