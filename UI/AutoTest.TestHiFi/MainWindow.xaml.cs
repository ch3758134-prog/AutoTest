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
    private ObservableCollection<DemoStep> _caseSteps = [];
    private string _currentDescription = "验证外部开关、按键等输入对 ECU 输出与 CAN 信号的响应是否符合要求。";
    public string CurrentDescription { get => _currentDescription; private set { _currentDescription = value; Raise(nameof(CurrentDescription)); } }
    private string _summaryTotal = "29", _summaryNormal = "26", _summaryWarning = "1", _summaryError = "2";
    public string SummaryTotal { get => _summaryTotal; private set { _summaryTotal = value; Raise(nameof(SummaryTotal)); } }
    public string SummaryNormal { get => _summaryNormal; private set { _summaryNormal = value; Raise(nameof(SummaryNormal)); } }
    public string SummaryWarning { get => _summaryWarning; private set { _summaryWarning = value; Raise(nameof(SummaryWarning)); } }
    public string SummaryError { get => _summaryError; private set { _summaryError = value; Raise(nameof(SummaryError)); } }
    private string _phaseCountText = "准备 6 / 测试 29 / 清理 5";
    public string PhaseCountText { get => _phaseCountText; private set { _phaseCountText = value; Raise(nameof(PhaseCountText)); } }
    private string _issueHeaderText = "⌄ 问题与定位 (3)";
    public string IssueHeaderText { get => _issueHeaderText; private set { _issueHeaderText = value; Raise(nameof(IssueHeaderText)); } }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
    private bool _ready;
    private bool _issuesExpanded = true;
    private string _phase = "测试";
    private DemoCase? _selectedCase;
    private string _statusText = "高保真视觉预览 · 演示数据 · 无真实 ECU 运行结果";

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DemoCase> Cases { get; } = DemoContent.Cases();
    public ObservableCollection<DemoStep> VisibleSteps { get; } = [];
    public ObservableCollection<DemoIssue> Issues { get; } = DemoContent.Issues();
    public ObservableCollection<DemoIssue> VisibleIssues { get; } = [];
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
        PreviewInvariants.Assert(Cases, _allSteps, Issues);
        InitializeComponent();
        DataContext = this;
        _ready = true;
        // Do not open an empty case. The accepted visual baseline is the
        // populated TC-003 / TEST / CAN assertion state.
        SelectedCase = Cases.Single(c => c.Id == "TC-003");
        UpdateCaseContext(SelectedCase);
        CaseList.SelectedItem = SelectedCase;
        ShowPhase("测试", initial: true);
        var selected = VisibleSteps.FirstOrDefault(s => s.Number == 16);
        if (selected is not null) StepList.SelectedItem = selected;
        StatusText = "视觉预览 · TC-003 · 测试 29 步 · 3 条演示问题（无实际执行）";
        InitializeWorkspaceShell();
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
        UpdateCaseContext(c);
        ShowPhase(_phase, initial: true);
        var row = VisibleSteps.FirstOrDefault(s => s.Number == 16 && c.Id == "TC-003")
                  ?? VisibleSteps.FirstOrDefault();
        if (row is not null) StepList.SelectedItem = row;
        StatusText = c.Id == "TC-003"
            ? "高保真视觉基线 · TC-003 · 29 步 / 3 项演示问题"
            : c.Name + " · 已切换独立的用例视觉样本（无真实执行）。";
    }

    private void UpdateCaseContext(DemoCase selected)
    {
        _caseSteps = selected.Id == "TC-003" ? _allSteps : DemoContent.StepsForCase(selected.Id);
        var prepare = _caseSteps.Count(x => x.Phase == "准备");
        var test = _caseSteps.Count(x => x.Phase == "测试");
        var cleanup = _caseSteps.Count(x => x.Phase == "清理");
        PrepareButton.Content = $"准备 ({prepare})";
        TestButton.Content = $"测试 ({test})";
        CleanupButton.Content = $"清理 ({cleanup})";
        PhaseCountText = $"准备 {prepare}  /  测试 {test}  /  清理 {cleanup}";
        CurrentDescription = selected.Id == "TC-003"
            ? "验证外部开关、按键等输入对 ECU 输出与 CAN 信号的响应是否符合要求。"
            : selected.Name + " · 研究用例操作与视觉展示，不包含真实 ECU 执行。";
        SummaryTotal = test.ToString();
        if (selected.Id == "TC-003")
        {
            // The approved image uses a representative design-only status summary.
            SummaryNormal = "26"; SummaryWarning = "1"; SummaryError = "2";
        }
        else
        {
            SummaryError = _caseSteps.Count(x => x.Phase == "测试" && x.Status == "Error").ToString();
            SummaryWarning = _caseSteps.Count(x => x.Phase == "测试" && x.Status == "Warning").ToString();
            SummaryNormal = (test-int.Parse(SummaryWarning)-int.Parse(SummaryError)).ToString();
        }
        VisibleIssues.Clear();
        if (selected.Id == "TC-003")
            foreach (var issue in Issues) VisibleIssues.Add(issue);
        IssueHeaderText = $"⌄ 问题与定位 ({VisibleIssues.Count})";
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
        if (SelectedCase is null) return;
        foreach (var step in _caseSteps.Where(s => s.Phase == _phase &&
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
        FileShortcut(e);
        if (e.Handled) return;
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
