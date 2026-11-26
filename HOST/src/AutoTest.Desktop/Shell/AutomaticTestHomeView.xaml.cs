using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
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
    private AutomaticTestHomeViewModel? _observedModel;

    public AutomaticTestHomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyResponsiveLayout();
            AttachCaseChanges();
        };
        Unloaded += (_, _) => DetachCaseChanges();
        DataContextChanged += (_, _) => AttachCaseChanges();
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

    private void AttachCaseChanges()
    {
        if (!IsLoaded) return;
        var next = DataContext as AutomaticTestHomeViewModel;
        if (ReferenceEquals(_observedModel, next)) return;
        DetachCaseChanges();
        _observedModel = next;
        if (_observedModel is not null)
            _observedModel.Cases.CollectionChanged += OnCasePresentationChanged;
    }

    private void DetachCaseChanges()
    {
        if (_observedModel is not null)
            _observedModel.Cases.CollectionChanged -= OnCasePresentationChanged;
        _observedModel = null;
    }

    private void OnCasePresentationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsLoaded || OperatorFollowExecution.IsChecked != true) return;
        if (e.Action != NotifyCollectionChangedAction.Replace) return;
        _ = Dispatcher.BeginInvoke(new Action(FollowActualExecution),
            DispatcherPriority.Loaded);
    }

    private void FollowActualExecution()
    {
        if (!IsLoaded || OperatorFollowExecution.IsChecked != true ||
            _observedModel is null) return;
        var active = _observedModel.Cases.FirstOrDefault(
            item => item.State == "执行中");
        active ??= _observedModel.Cases.LastOrDefault(item =>
            item.State is "执行异常" or "已停止" ||
            item.State.StartsWith("实测", StringComparison.Ordinal) &&
            item.State != "实测通过");
        if (active is null) return;
        if (CasesTree.ItemContainerGenerator.ContainerFromItem(active)
            is not TreeViewItem caseRow) return;
        caseRow.IsExpanded = true;
        caseRow.BringIntoView();

        // The row hierarchy projects real authored StepId states. Only a
        // currently running step is focused; no result or CAN action occurs.
        static AutomaticTestStep? FindRunning(IEnumerable<AutomaticTestStep> source)
        {
            foreach (var item in source)
            {
                if (item.State == "执行中") return item;
                if (FindRunning(item.Children) is { } nested) return nested;
            }
            return null;
        }
        var target = FindRunning(active.Children);
        if (target is null) return;
        caseRow.UpdateLayout();
        static bool BringStep(ItemsControl parent,
            IEnumerable<AutomaticTestStep> children, AutomaticTestStep target)
        {
            foreach (var item in children)
            {
                var view = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (view is null) continue;
                if (ReferenceEquals(item, target))
                {
                    view.BringIntoView();
                    return true;
                }
                if (item.Children.Count == 0) continue;
                view.IsExpanded = true;
                view.UpdateLayout();
                if (BringStep(view, item.Children, target)) return true;
            }
            return false;
        }
        BringStep(caseRow, active.Children, target);
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

/// <summary>
/// Present stable executor states in operator language without changing the
/// underlying state keys used for execution, follow-navigation or evidence.
/// </summary>
public sealed class OperatorStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter,
        CultureInfo culture) => value is string state ? state switch
    {
        "未执行" => "未开始",
        "执行中" => "进行中",
        "实测通过" => "通过",
        "实测失败" => "失败",
        "引用需修复" => "资源需检查",
        "已停止" => "已停止",
        "执行异常" => "执行异常",
        _ => state
    } : "—";

    public object ConvertBack(object value, Type targetType, object parameter,
        CultureInfo culture) => Binding.DoNothing;
}
