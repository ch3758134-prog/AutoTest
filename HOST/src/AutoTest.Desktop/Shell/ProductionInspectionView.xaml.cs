using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

/// <summary>Production display is read-only. Selection never changes executable TEST data.</summary>
public partial class ProductionInspectionView : UserControl
{
    public ProductionInspectionView()
    {
        InitializeComponent();
    }

    private void Cases_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductionInspectionViewModel model &&
            e.NewValue is ProductionInspectionNode node)
            model.Selected = node;
    }
}
