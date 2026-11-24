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
    /// At 125/150% DPI, a rigid three-column layout hides the very TEST
    /// steps an operator needs. Preserve all real bound controls and place
    /// status beneath the central list; never replace missing space with a
    /// mock progress card or silently hide the current DUT.
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded) return;
        var compact = ActualWidth < 1120;
        if (_compactOperatorLayout == compact) return;
        _compactOperatorLayout = compact;
        if (compact)
        {
            OperatorStageColumn.Width = new GridLength(164);
            OperatorStageGapColumn.Width = new GridLength(7);
            OperatorTestsColumn.MinWidth = 280;
            OperatorTestsColumn.Width = new GridLength(1, GridUnitType.Star);
            OperatorResultsGapColumn.Width = new GridLength(0);
            OperatorResultsColumn.MinWidth = 0;
            OperatorResultsColumn.Width = new GridLength(0);
            OperatorPrimaryRow.Height = new GridLength(2, GridUnitType.Star);
            OperatorResultsRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(OperatorStagesPanel, 0);
            Grid.SetRowSpan(OperatorStagesPanel, 2);
            Grid.SetRow(OperatorTestsPanel, 0);
            Grid.SetRow(OperatorResultsPanel, 1);
            Grid.SetColumn(OperatorResultsPanel, 2);
        }
        else
        {
            Grid.SetRowSpan(OperatorStagesPanel, 1);
            Grid.SetRow(OperatorResultsPanel, 0);
            Grid.SetColumn(OperatorResultsPanel, 4);
            OperatorStageColumn.Width = new GridLength(196);
            OperatorStageGapColumn.Width = new GridLength(8);
            OperatorTestsColumn.MinWidth = 460;
            OperatorTestsColumn.Width = new GridLength(2.2, GridUnitType.Star);
            OperatorResultsGapColumn.Width = new GridLength(8);
            OperatorResultsColumn.MinWidth = 315;
            OperatorResultsColumn.Width = new GridLength(1, GridUnitType.Star);
            OperatorPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            OperatorResultsRow.Height = new GridLength(0);
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
