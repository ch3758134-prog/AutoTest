using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Operations;

/// <summary>ViewModel is injected from one application-wide audit store, never a new DB/USB link.</summary>
public partial class OperationsView : UserControl
{
    public OperationsView(OperationsViewModel model)
    {
        InitializeComponent();
        DataContext = model ?? throw new ArgumentNullException(nameof(model));
    }
}
