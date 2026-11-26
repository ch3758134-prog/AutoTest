using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Engineering-only observer of the SAME real full-project TEST/FLASH run.
/// Does not create a second execution engine, allow a formal production START,
/// or manufacture an independent PASS/Inspection record.
/// </summary>
public partial class EngineeringVerificationWindow : Window
{
    private readonly AutomaticTestHomeViewModel _model;
    private readonly Action _openReports;

    public EngineeringVerificationWindow(AutomaticTestHomeViewModel model, Action openReports)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _openReports = openReports ?? throw new ArgumentNullException(nameof(openReports));
        InitializeComponent();
        DataContext = model;
        Loaded += (_, _) => DesktopWindowSizing.FitToVisibleWorkspace(this);
        _model.Cases.CollectionChanged += OnCasesChanged;
        _model.OperatorEvents.CollectionChanged += OnEventsChanged;
        Closed += (_, _) =>
        {
            _model.Cases.CollectionChanged -= OnCasesChanged;
            _model.OperatorEvents.CollectionChanged -= OnEventsChanged;
        };
    }

    private void OnCasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Replace ||
            FollowEngineeringRun.IsChecked != true || !IsLoaded) return;
        _ = Dispatcher.BeginInvoke(new Action(FollowActualStep), DispatcherPriority.Loaded);
    }

    private void FollowActualStep()
    {
        if (!IsLoaded || FollowEngineeringRun.IsChecked != true) return;
        var active = _model.Cases.FirstOrDefault(x => x.State == "执行中");
        if (active is null) return;
        if (EngineeringCasesTree.ItemContainerGenerator.ContainerFromItem(active)
            is not TreeViewItem row) return;
        row.IsExpanded = true;
        row.BringIntoView();

        static AutomaticTestStep? Running(IEnumerable<AutomaticTestStep> nodes)
        {
            foreach (var item in nodes)
            {
                if (item.State == "执行中") return item;
                if (Running(item.Children) is { } nested) return nested;
            }
            return null;
        }

        var current = Running(active.Children);
        if (current is null) return;
        row.UpdateLayout();

        static bool Bring(ItemsControl parent,
            IEnumerable<AutomaticTestStep> nodes, AutomaticTestStep target)
        {
            foreach (var node in nodes)
            {
                var visual = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
                if (visual is null) continue;
                if (ReferenceEquals(node, target))
                {
                    visual.BringIntoView();
                    return true;
                }
                if (node.Children.Count == 0) continue;
                visual.IsExpanded = true;
                visual.UpdateLayout();
                if (Bring(visual, node.Children, target)) return true;
            }
            return false;
        }

        Bring(row, active.Children, current);
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsLoaded || !IsVisible) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || _model.OperatorEvents.Count == 0) return;
            OperatorEventsList.ScrollIntoView(_model.OperatorEvents[^1]);
        }), DispatcherPriority.Loaded);
    }

    private void EngineeringCasesTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _model.SelectOperatorNode(e.NewValue);

    // Closing this observer does not stop CAN, FLASH or TEST. The separate
    // explicit Stop command remains the only operator stop request.
    private void OpenCurrentReport_Click(object sender, RoutedEventArgs e)
    {
        var path = _model.OperatorHtmlReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "本次尚无可打开的已保存 HTML 报告。工程执行结束后才能查看。", 
                "工程实测报告", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "无法打开已经保存的报告：" + error.Message,
                "工程实测报告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenReports_Click(object sender, RoutedEventArgs e) => _openReports();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
