using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AutoTest.TestHiFi;

/// <summary>
/// A faithful visual implementation of the user-approved TEST screenshot.
/// This code deliberately cannot call hardware or the formal TEST executor.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<DemoStep> _allSteps = DemoContent.Steps();
    private bool _ready;
    private bool _issuesExpanded = true;
    private string _phase = "测试";
    private DemoCase? _selectedCase;
    private string _statusText = "高保真视觉预览 · 演示数据 · 无真实 ECU 运行结果";

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DemoCase> Cases { get; } = DemoContent.Cases();
    public ObservableCollection<DemoStep> VisibleSteps { get; } = [];
    public ObservableCollection<DemoIssue> Issues { get; } = DemoContent.Issues();
    public PreviewInspector Inspector { get; } = new();
    public DemoCase? SelectedCase
    {
        get => _selectedCase;
        private set { _selectedCase = value; PropertyChanged?.Invoke(this, new(nameof(SelectedCase))); }
    }
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; PropertyChanged?.Invoke(this, new(nameof(StatusText))); }
    }
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _ready = true;
        // Do not open an empty case. The accepted visual baseline is the
        // populated TC-003 / TEST / CAN assertion state.
        CaseList.SelectedItem = Cases.Single(c => c.Id == "TC-003");
        SelectedCase = Cases.Single(c => c.Id == "TC-003");
        ShowPhase("测试", initial: true);
        var selected = VisibleSteps.FirstOrDefault(s => s.Number == 16);
        if (selected is not null) StepList.SelectedItem = selected;
        StatusText = "视觉预览 · TC-003 · 测试 29 步 · 3 条演示问题（无实际执行）";
    }

    private void FilterCases()
    {
        if (!_ready) return;
        string search = CaseSearch.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(Cases);
        view.Filter = item => item is DemoCase c &&
            (search.Length == 0 || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             c.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void CaseSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CaseSearchHint is not null)
            CaseSearchHint.Visibility = string.IsNullOrWhiteSpace(CaseSearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        FilterCases();
    }

    private void CaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || CaseList.SelectedItem is not DemoCase c) return;
        SelectedCase = c;
        if (c.Id != "TC-003")
        {
            VisibleSteps.Clear();
            Inspector.Heading = "选择步骤查看参数";
            Inspector.Description = "其他用例仅提供导航外观。完整高保真步骤样本位于 TC-003。";
            StatusText = c.Name + " · 此处为导航状态预览。选择 TC-003 可返回完整示例。";
            return;
        }
        ShowPhase(_phase, initial: true);
        var row = VisibleSteps.FirstOrDefault(s => s.Number == 16) ??
                  VisibleSteps.FirstOrDefault();
        if (row is not null) StepList.SelectedItem = row;
    }

    private void Phase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string phase }) return;
        ShowPhase(phase);
    }

    private void ShowPhase(string phase, bool initial = false)
    {
        _phase = phase;
        PrepareButton.Style = (Style)FindResource(phase == "准备" ? "SelectedButton" : "OutlineButton");
        TestButton.Style = (Style)FindResource(phase == "测试" ? "SelectedButton" : "OutlineButton");
        CleanupButton.Style = (Style)FindResource(phase == "清理" ? "SelectedButton" : "OutlineButton");
        RebuildSteps(initial && phase == "测试" ? 16 : null);
    }

    private void StepSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (StepSearchHint is not null)
            StepSearchHint.Visibility = string.IsNullOrWhiteSpace(StepSearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        if (_ready) RebuildSteps(null);
    }

    private void RebuildSteps(int? selectNumber)
    {
        if (!_ready) return;
        var previous = StepList.SelectedItem as DemoStep;
        var query = StepSearch.Text.Trim();
        VisibleSteps.Clear();
        if (SelectedCase?.Id != "TC-003") return;
        foreach (var step in _allSteps.Where(s => s.Phase == _phase &&
                 (query.Length == 0 ||
                  s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                  s.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                  s.Number.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            VisibleSteps.Add(step);
        }
        var chosen = VisibleSteps.FirstOrDefault(s => s.Number == selectNumber && selectNumber.HasValue)
            ?? VisibleSteps.FirstOrDefault(s => ReferenceEquals(s, previous))
            ?? VisibleSteps.FirstOrDefault();
        if (chosen is not null) StepList.SelectedItem = chosen;
    }

    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || StepList.SelectedItem is not DemoStep step) return;
        Inspector.Show(step);
        StatusText = "当前对象：步骤 " + step.Number + " · " + step.Title +
                     "  │  演示参数，未连接正式 TEST 执行器";
    }

    private void Navigate(DemoIssue issue)
    {
        if (SelectedCase?.Id != "TC-003")
        {
            CaseList.SelectedItem = Cases.Single(c => c.Id == "TC-003");
            SelectedCase = Cases.Single(c => c.Id == "TC-003");
        }
        StepSearch.Clear();
        ShowPhase("测试");
        var step = VisibleSteps.FirstOrDefault(s => s.Number == issue.StepNumber);
        if (step is not null)
        {
            StepList.SelectedItem = step;
            StepList.ScrollIntoView(step);
        }
        StatusText = "已定位演示问题：步骤 " + issue.StepNumber + " · " + issue.Description;
    }

    private void LocateIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DemoIssue issue })
        {
            Navigate(issue);
            e.Handled = true;
        }
    }

    private void IssueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || IssueList.SelectedItem is not DemoIssue issue) return;
        Navigate(issue);
    }

    private void CollapseProblems_Click(object sender, RoutedEventArgs e)
    {
        _issuesExpanded = !_issuesExpanded;
        IssuesRow.Height = _issuesExpanded ? new GridLength(165) : new GridLength(38);
        IssueHeadersRow.Height = _issuesExpanded ? new GridLength(26) : new GridLength(0);
        IssueItemsRow.Height = _issuesExpanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        IssueToggleButton.Content = _issuesExpanded ? "×" : "▢";
        IssueToggleButton.ToolTip = _issuesExpanded ? "收起问题列表" : "展开问题列表";
    }

    private void ShowSignalPicker_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SignalPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedSignal))
        {
            Inspector.Signal = picker.SelectedSignal;
            Inspector.Warning = "已在界面预览中选择 " + picker.SelectedSignal + "；未验证真实 DBC。";
            StatusText = "DBC 信号选择器视觉演示：" + picker.SelectedSignal + "（没有修改任何正式工程）。";
        }
    }

    private void PreviewCommand_Click(object sender, RoutedEventArgs e)
    {
        var label = sender is Button button ? button.Content?.ToString() ?? "此操作" : "此操作";
        StatusText = label + "：本版仅验证界面视觉与导航，不更改正式测试工程。";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.F)
        {
            StepSearch.Focus();
            StepSearch.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Q)
        {
            CaseSearch.Focus();
            e.Handled = true;
        }
    }
}
