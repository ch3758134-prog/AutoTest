using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;

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

/// <summary>Reserve room for TreeView's expander and scrollbar while allowing
/// the full-width operator status column to align across every case row.</summary>
public sealed class OperatorRowWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double width && double.IsFinite(width)
            ? Math.Max(300d, width - 50d) : 600d;
    }
    public object ConvertBack(object value, Type targetType, object parameter,
        CultureInfo culture) => Binding.DoNothing;
}
