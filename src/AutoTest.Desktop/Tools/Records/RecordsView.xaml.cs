using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.Records;

/// <summary>Uses the existing RunRecord reader and report service via an injected ViewModel.</summary>
public partial class RecordsView : UserControl
{
    public RecordsView(RecordsViewModel model)
    {
        InitializeComponent();
        DataContext = model ?? throw new ArgumentNullException(nameof(model));
    }
}
