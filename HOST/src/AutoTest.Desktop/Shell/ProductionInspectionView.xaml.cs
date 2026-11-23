using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Fixed read-only automatic-test operator display. Actual TEST observations
/// update the view model; the user does not select or expand steps to drive it.
/// </summary>
public partial class ProductionInspectionView : UserControl
{
    public ProductionInspectionView() => InitializeComponent();
}
