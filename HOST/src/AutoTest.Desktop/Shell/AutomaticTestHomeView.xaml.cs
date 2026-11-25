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
    private bool? _compactOperatorLayout;

    public AutomaticTestHomeView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    /// <summary>
    /// Production is a TEST monitor, not a fixed multi-panel dashboard.
    /// Under 980 effective pixels, retain the full ordered step list and move
    /// factual details beneath it. WPF grid lengths use DIPs, not device pixels.
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        var compact = ActualWidth < 980;
        if (_compactOperatorLayout == compact) return;
        _compactOperatorLayout = compact;
        if (compact)
        {
            OperatorCasesColumn.Width = new GridLength(1, GridUnitType.Star);
            OperatorGapColumn.Width = new GridLength(0);
            OperatorDetailsColumn.Width = new GridLength(0);
            OperatorPrimaryRow.Height = new GridLength(1.75, GridUnitType.Star);
            OperatorDetailsRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(OperatorDetailsPanel, 0);
            Grid.SetRow(OperatorDetailsPanel, 1);
            OperatorDetailsPanel.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            Grid.SetColumn(OperatorDetailsPanel, 2);
            Grid.SetRow(OperatorDetailsPanel, 0);
            OperatorDetailsPanel.Margin = new Thickness(0);
            OperatorCasesColumn.Width = new GridLength(1, GridUnitType.Star);
            OperatorGapColumn.Width = new GridLength(10);
            OperatorDetailsColumn.Width = new GridLength(360);
            OperatorPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            OperatorDetailsRow.Height = new GridLength(0);
        }
    }

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
