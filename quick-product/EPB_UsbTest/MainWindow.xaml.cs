using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EPB.Core.Configuration;
using EPB.Core.Communication;
using EPB.Core.Testing;
using Microsoft.Win32;

namespace EPB_UsbTest;

public static class ConfigurationStepKindValues
{
    public static Array All { get; } = Enum.GetValues<ConfigurationStepKind>();
}

public enum FlowEditorKind
{
    Step,
    If,
    When,
    Switch,
    Loop,
    Body,
    Then,
    Else,
    Hit,
    Timeout,
    Case,
    Default,
    Section
}

public sealed class FlowEditorRow : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isInsertAnchor;
    private string _number = "";
    private int _depth;

    private FlowEditorRow(string id, FlowEditorKind kind, string name, string parameterText, string pathText, string noteText, TestFlowNode? sourceNode, bool presentationOnly)
    {
        Id = id;
        ControlKind = kind;
        Name = name;
        ParameterText = parameterText;
        PathText = pathText;
        NoteText = noteText;
        SourceNode = sourceNode;
        IsPresentationOnly = presentationOnly;
        IsContainer = kind is FlowEditorKind.If or FlowEditorKind.When or FlowEditorKind.Switch or FlowEditorKind.Loop or FlowEditorKind.Section || sourceNode?.Children.Count > 0 || sourceNode?.Kind == TestFlowNodeKind.组合;
        IsControl = kind is FlowEditorKind.If or FlowEditorKind.When or FlowEditorKind.Switch or FlowEditorKind.Loop;
        DisplayKind = kind switch
        {
            FlowEditorKind.Step => "◆",
            FlowEditorKind.If => "IF",
            FlowEditorKind.When => "WHEN",
            FlowEditorKind.Switch => "SWITCH",
            FlowEditorKind.Loop => "LOOP",
            FlowEditorKind.Body => "BODY",
            FlowEditorKind.Then or FlowEditorKind.Else or FlowEditorKind.Hit or FlowEditorKind.Timeout => "└",
            FlowEditorKind.Case => "CASE",
            FlowEditorKind.Default => "DEFAULT",
            FlowEditorKind.Section => "◆",
            _ => ""
        };
        KindBrush = kind switch
        {
            FlowEditorKind.If => Brush("#2563EB"),
            FlowEditorKind.When => Brush("#0F8B8D"),
            FlowEditorKind.Switch or FlowEditorKind.Case or FlowEditorKind.Default => Brush("#B7791F"),
            FlowEditorKind.Loop => Brush("#6D4BA8"),
            FlowEditorKind.Body => Brush("#8C9AAF"),
            FlowEditorKind.Section => Brush("#0F8B8D"),
            FlowEditorKind.Then or FlowEditorKind.Else or FlowEditorKind.Hit or FlowEditorKind.Timeout => Brush("#8C9AAF"),
            _ => Brush("#4B6B88")
        };
        KindBackground = kind switch
        {
            FlowEditorKind.If => Brush("#EEF5FF"),
            FlowEditorKind.When => Brush("#EAF8F7"),
            FlowEditorKind.Switch or FlowEditorKind.Case or FlowEditorKind.Default => Brush("#FFF7E8"),
            FlowEditorKind.Loop => Brush("#F5F0FF"),
            FlowEditorKind.Section => Brush("#EAF8F7"),
            _ => Brush("Transparent")
        };
        ResultReferenceText = parameterText;
        VerdictText = "未配置";
        SignalDefinitionText = parameterText;
        PeriodMsText = "20";
        TimeoutMsText = "1000";
    }

    public string Id { get; }
    public FlowEditorKind ControlKind { get; }
    public string DisplayKind { get; }
    public string Name { get; }
    public string NoteText { get; }
    public TestFlowNode? SourceNode { get; }
    public bool IsPresentationOnly { get; }
    public bool IsContainer { get; }
    public bool IsControl { get; }
    public bool IsStructural => ControlKind is FlowEditorKind.Body or FlowEditorKind.Then or FlowEditorKind.Else or FlowEditorKind.Hit or FlowEditorKind.Timeout or FlowEditorKind.Case or FlowEditorKind.Default;
    public Brush KindBrush { get; }
    public Brush KindBackground { get; }
    public ObservableCollection<FlowEditorRow> Children { get; } = [];
    public ObservableCollection<int> IndentGuides { get; } = [];
    public FlowEditorRow? Parent { get; set; }
    public string Number { get => _number; set => Set(ref _number, value); }
    public int Depth { get => _depth; set => Set(ref _depth, value); }
    public string ParameterText { get; set; }
    public string PathText { get; set; }
    public string ResultReferenceText { get; set; }
    public string ResultReferenceKey { get; set; } = "";
    public string VerdictText { get; set; }
    public string SignalDefinitionText { get; set; }
    public string SignalDefinitionKey { get; set; } = "";
    public string PeriodMsText { get; set; }
    public string TimeoutMsText { get; set; }
    public string LoopCountText { get; set; } = "2";
    public string LoopDelayMsText { get; set; } = "0";
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpandGlyph));
            OnPropertyChanged(nameof(CollapsedSummary));
        }
    }
    public bool IsInsertAnchor { get => _isInsertAnchor; set => Set(ref _isInsertAnchor, value); }
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public string CollapsedSummary
    {
        get
        {
            if (!IsContainer) return "";
            if (ControlKind == FlowEditorKind.Switch)
                return $"{Children.Count(x => x.ControlKind == FlowEditorKind.Case)} CASE + {(Children.Any(x => x.ControlKind == FlowEditorKind.Default) ? "DEFAULT" : "")}";
            return $"{CountSteps(this)} steps";
        }
    }

    public static FlowEditorRow FromNodeTree(TestFlowNode source)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectNodeLabels(source, labels);
        var row = FromNode(source, labels);
        foreach (var child in source.Children) AddChild(row, BuildNode(child, labels));
        return row;
    }

    public static IReadOnlyList<FlowEditorRow> FromNodeTrees(IEnumerable<TestFlowNode> sources)
    {
        var roots = sources.ToList();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in roots) CollectNodeLabels(source, labels);
        return roots.Select(source => BuildNode(source, labels)).ToList();
    }

    public static FlowEditorRow FromNode(TestFlowNode source)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectNodeLabels(source, labels);
        return FromNode(source, labels);
    }

    private static FlowEditorRow FromNode(TestFlowNode source, IReadOnlyDictionary<string, string> labels)
    {
        if (source.FlowControl is { } control)
        {
            var resultReference = control.ResultReference is { } reference
                ? labels.GetValueOrDefault(reference.ProducerNodeId, reference.ProducerNodeId)
                : "请选择已有步骤";
            var verdict = control.RequiredVerdict == ResultVerdict.Passed ? "通过" : "未通过";
            return control.Kind switch
            {
                TestFlowControlKind.Condition => new(source.Id, FlowEditorKind.If, string.IsNullOrWhiteSpace(source.Name) ? "IF" : source.Name, $"{resultReference} · {verdict}", "分支选择", source.StatusSummary, source, false)
                {
                    ResultReferenceText = resultReference,
                    ResultReferenceKey = control.ResultReference?.ProducerNodeId ?? "",
                    VerdictText = verdict
                },
                TestFlowControlKind.WaitForVerdict => new(source.Id, FlowEditorKind.When, string.IsNullOrWhiteSpace(source.Name) ? "WHEN" : source.Name, $"{resultReference} · {verdict} · {control.PeriodMs} ms", $"超时 {control.TimeoutMs} ms", source.StatusSummary, source, false)
                {
                    ResultReferenceText = resultReference,
                    ResultReferenceKey = control.ResultReference?.ProducerNodeId ?? "",
                    VerdictText = verdict,
                    PeriodMsText = control.PeriodMs.ToString(CultureInfo.InvariantCulture),
                    TimeoutMsText = control.TimeoutMs.ToString(CultureInfo.InvariantCulture)
                },
                TestFlowControlKind.ValueBranch => new(source.Id, FlowEditorKind.Switch, string.IsNullOrWhiteSpace(source.Name) ? "SWITCH" : source.Name, SwitchDefinitionText(control), "按值分支", source.StatusSummary, source, false)
                {
                    SignalDefinitionText = SwitchDefinitionText(control),
                    SignalDefinitionKey = SwitchDefinitionKey(control),
                    TimeoutMsText = control.TimeoutMs.ToString(CultureInfo.InvariantCulture)
                },
                TestFlowControlKind.Repeat => new(source.Id, FlowEditorKind.Loop, string.IsNullOrWhiteSpace(source.Name) ? "LOOP" : source.Name,
                    $"重复 {control.RepeatCount} 次 · 间隔 {control.RepeatDelayMs} ms", "BODY", source.StatusSummary, source, false)
                {
                    LoopCountText = control.RepeatCount.ToString(CultureInfo.InvariantCulture),
                    LoopDelayMsText = control.RepeatDelayMs.ToString(CultureInfo.InvariantCulture)
                },
                _ => new(source.Id, FlowEditorKind.Step, source.Name, source.DisplaySummary, "流程控制", source.StatusSummary, source, false)
            };
        }

        if (source.PathKind != TestFlowPathKind.None)
        {
            var (pathKind, pathName, pathParameter, pathText) = source.PathKind switch
            {
                TestFlowPathKind.Then => (FlowEditorKind.Then, "THEN", "", ""),
                TestFlowPathKind.Else => (FlowEditorKind.Else, "ELSE", "", ""),
                TestFlowPathKind.Hit => (FlowEditorKind.Hit, "HIT", "条件满足", "继续"),
                TestFlowPathKind.Timeout => (FlowEditorKind.Timeout, "TIMEOUT", "超时", "结束或失败"),
                TestFlowPathKind.Case => (FlowEditorKind.Case, $"CASE {source.PathValue}", "", ""),
                TestFlowPathKind.Default => (FlowEditorKind.Default, "DEFAULT", "", ""),
                TestFlowPathKind.Body => (FlowEditorKind.Body, "BODY", "循环体", ""),
                _ => (FlowEditorKind.Step, source.Name, source.DisplaySummary, "")
            };
            return new(source.Id, pathKind, pathName, pathParameter, pathText, source.StatusSummary, source, false);
        }

        var parameter = source.Operation?.Summary ?? source.DisplaySummary;
        if (source.Operation?.Kind == ConfigurationStepKind.报文解析 && source.ParseAssertion is { } assertion && !string.IsNullOrWhiteSpace(assertion.Expectation))
            parameter = $"{source.Operation.Name} · 判定 {assertion.Expectation}";
        var path = source.Operation?.DisplayKind ?? (source.Kind == TestFlowNodeKind.组合 ? "步骤组合" : "引用资源");
        return new(source.Id, FlowEditorKind.Step, source.Name, parameter, path, source.StatusSummary, source, false);
    }

    private static FlowEditorRow BuildNode(TestFlowNode source, IReadOnlyDictionary<string, string> labels)
    {
        var row = FromNode(source, labels);
        foreach (var child in source.Children) AddChild(row, BuildNode(child, labels));
        return row;
    }

    private static void CollectNodeLabels(TestFlowNode node, IDictionary<string, string> labels)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            labels[node.Id] = node.Name;
            if (node.Operation is not null) labels[node.Operation.Id] = node.Name;
        }
        foreach (var child in node.Children) CollectNodeLabels(child, labels);
    }

    private static string SwitchDefinitionText(TestFlowControlDefinition control)
        => control.SignalDefinition is { } signal
            ? $"{signal.MessageDefinitionId} > {signal.SignalDefinitionId}"
            : "请选择通信数据库信号";

    private static string SwitchDefinitionKey(TestFlowControlDefinition control)
        => control.SignalDefinition is { } signal
            ? $"{signal.MessageDefinitionId}|{signal.SignalDefinitionId}"
            : "";

    public static FlowEditorRow FromNodeOrPlaceholder(TestFlowNode? source, string id, string name, string parameter, string path, string note)
        => source is null ? Step(id, name, parameter, path, note) : FromNode(source);

    public static FlowEditorRow Step(string id, string name, string parameter, string path, string note)
        => new(id, FlowEditorKind.Step, name, parameter, path, note, null, true);

    public static FlowEditorRow If(string id, string resultReference, string verdict)
        => new(id, FlowEditorKind.If, "IF", $"{resultReference} · {verdict}", "分支选择", "", null, true)
        {
            ResultReferenceText = resultReference,
            VerdictText = verdict
        };

    public static FlowEditorRow When(string id, string resultReference, string verdict, string period, string timeout)
    {
        var row = new FlowEditorRow(id, FlowEditorKind.When, "WHEN", $"{resultReference} · {verdict} · {period} ms", $"超时 {timeout} ms", "", null, true)
        {
            ResultReferenceText = resultReference,
            VerdictText = verdict,
            PeriodMsText = period,
            TimeoutMsText = timeout
        };
        return row;
    }

    public static FlowEditorRow Switch(string id, string signalDefinition)
        => new(id, FlowEditorKind.Switch, "SWITCH", signalDefinition, "按值分支", "", null, true)
        {
            SignalDefinitionText = signalDefinition
        };
    public static FlowEditorRow Then(string id) => new(id, FlowEditorKind.Then, "THEN", "", "", "", null, true);
    public static FlowEditorRow Else(string id) => new(id, FlowEditorKind.Else, "ELSE", "", "", "", null, true);
    public static FlowEditorRow Hit(string id) => new(id, FlowEditorKind.Hit, "HIT", "条件满足", "继续", "", null, true);
    public static FlowEditorRow Timeout(string id, string text) => new(id, FlowEditorKind.Timeout, "TIMEOUT", "超时", text, "", null, true);
    public static FlowEditorRow Case(string id, string value) => new(id, FlowEditorKind.Case, value, "", "", "", null, true);
    public static FlowEditorRow Default(string id) => new(id, FlowEditorKind.Default, "DEFAULT", "", "", "", null, true);
    public static FlowEditorRow Section(string id, string name) => new(id, FlowEditorKind.Section, name, "", "", "", null, true);

    private static void AddChild(FlowEditorRow parent, FlowEditorRow child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }

    private static int CountSteps(FlowEditorRow row)
        => row.Children.Sum(child => child.IsStructural ? CountSteps(child) : 1 + (child.IsContainer ? CountSteps(child) : 0));

    private static SolidColorBrush Brush(string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}

public partial class MainWindow : Window
{
    private readonly UsbLink _usb = new();
    // CAN 帧可能远高于 WPF 的安全刷新频率。接收侧只合并最新状态，
    // 由 UI 定时批量取走，不能让每帧都进入 Dispatcher 队列。
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly object _rawGate = new();
    private readonly Dictionary<(bool Ide, uint Id), PendingRaw> _pendingRawById = [];
    private readonly Dictionary<(bool Ide, uint Id), RawRow> _rawById = [];
    private readonly Dictionary<(bool Ide, uint Id), CanFrame> _latestCanById = [];
    private readonly WaveformFrameBuffer _waveformFrames = new();
    private readonly ObservableCollection<RawRow> _rawRows = [];
    private readonly ObservableCollection<LiveRow> _liveRows = [];
    // 保留一个可供空态绑定使用的窗口级状态；当前列表仍由 RawGrid 直接绑定 _rawRows。
    public static readonly DependencyProperty Can1EmptyProperty = DependencyProperty.Register(
        nameof(Can1Empty), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));
    public bool Can1Empty
    {
        get => (bool)GetValue(Can1EmptyProperty);
        private set => SetValue(Can1EmptyProperty, value);
    }
    private bool _rawPaused;
    private readonly List<string> _logEntries = [];
    private readonly ObservableCollection<UiLogEntry> _visibleLogEntries = [];
    private string? _lastDisplayedLog;
    private DateTime _lastDisplayedLogAt;
    private readonly ObservableCollection<RunTimelineRow> _timelineEntries = [];
    private readonly string _layoutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EPB_UsbTest", "ui-layout.json");
    private readonly string _configPathMemory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EPB_UsbTest", "last-config.txt");
    private readonly DispatcherTimer _autoSave = new() { Interval = TimeSpan.FromSeconds(4) };
    private EcuConfiguration _configuration = EcuConfiguration.CreateNew();
    private ConfigurationTreeItem? _selectedItem;
    private ConfigurationSequence? _selectedSequence;
    private ConfigurationStep? _selectedStep;
    private ConfigurationStep? _propertyStep;
    private ConfigurationStep? _propertyAnchorStep;
    // 属性编辑器打开时绑定的测试节点。保存资源不能依赖易变的当前树选中项。
    private string? _propertyTestNodeId;
    private string? _path;
    private int _checkingUsbProtocol;
    private string? _clipboardStepJson;
    private ConfigurationStep? _deletedStep;
    private ConfigurationSequence? _deletedFromSequence;
    private ObservableCollection<ConfigurationStep>? _deletedStepOwner;
    private int _deletedStepIndex = -1;
    private ConfigurationTreeItem? _deletedTreeItem;
    private ConfigurationTreeItem? _deletedTreeParent;
    private int _deletedTreeIndex = -1;
    private TestFlowNode? _deletedFlowNode;
    private TestCaseDefinition? _deletedFlowCase;
    private ObservableCollection<TestFlowNode>? _deletedFlowOwner;
    private int _deletedFlowIndex = -1;
    private Point _stepDragStart;
    private ConfigurationStep? _stepTreeDragCandidate;
    private Point _testNodeDragStart;
    private TestFlowNode? _testNodeDragCandidate;
    private Point _treeDragStart;
    private ConfigurationTreeItem? _treeDragCandidate;
    private TreeViewItem? _contextTreeItem;
    private TestFlowNode? _contextTestNode;
    private ConfigurationTreeItem? _atomicOperationParent;
    private readonly Dictionary<string, CommunicationTreeEntry> _communicationTreeEntries = [];
    private readonly ConfigurationDefinitionBindingService _definitionBindingService = new();
    private readonly LinkedList<string> _recentDefinitionKeys = new();
    private double? _configurationTreeScrollOffset;
    private bool _updatingRecentSelector;
    private bool _rebuildingBroadcastEditor;
    private bool _restoredLastConfig;
    private TestCaseDefinition? _selectedTestCase;
    private TestFlowNode? _selectedTestNode;
    private string? _selectedTestNodeId;
    private readonly HashSet<string> _selectedTestNodeIds = [];
    private readonly ObservableCollection<FlowEditorRow> _flowEditorRows = [];
    private readonly List<FlowEditorRow> _flowEditorRoots = [];
    private FlowEditorRow? _selectedFlowEditorRow;
    private FlowEditorRow? _flowInsertAnchor;
    private string? _flowInsertKind;
    private bool _canPanelCollapsed;
    private bool _logPanelCollapsed;
    private readonly Dictionary<string, string?> _lastTestNodeByCase = [];
    private TestCaseDefinition? _pendingTestCaseSelection;
    private TestFlowNode? _pendingTestNodeSelection;
    private bool _restoringWorkspaceSelection;
    private WorkspaceSelectionSnapshot? _runWorkspaceSelection;
    private int _selectionRestoreGeneration;
    private readonly ObservableCollection<TestCaseResult> _testCaseResults = [];
    // 所有执行入口共用同一运行状态，避免“资源执行”和“自动测试”并行占用 USB。
    private readonly object _runStateGate = new();
    private CancellationTokenSource? _runCts;
    private bool _runActive;

    public MainWindow()
    {
        InitializeComponent();
        // TreeViewItem 的类处理器可能先把 BringIntoView 标记为已处理；用 handledEventsToo
        // 确保普通点击不会偷偷改变用户正在浏览的滚动位置。
        ConfigurationTree.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(Tree_RequestBringIntoView), true);
        RawGrid.ItemsSource = _rawRows;
        LogRows.ItemsSource = _visibleLogEntries;
        FlowEditorList.ItemsSource = _flowEditorRows;
        UpdateCan1EmptyState();
        _usb.Log += x => Ui(() => AppendLog(x));
        _usb.ConnectionChanged += x => Ui(() => ConnectionChanged(x));
        _usb.CanFrameReceived += ReceiveCan;
        _refresh.Tick += (_, _) => { FlushRawCan(); RefreshLive(); }; _refresh.Start();
        _autoSave.Tick += async (_, _) => await AutoSaveAsync(); _autoSave.Start();
        RebuildTree(); UpdateDatabaseStatus(); ProjectText.Text = "新 ECU 配置（未保存）"; LoadRecentConfigSelector();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_layoutPath))
            {
                var layout = System.Text.Json.JsonSerializer.Deserialize<UiLayout>(File.ReadAllText(_layoutPath));
                if (layout is not null)
                {
                    if (layout.LeftWidth is >= 210 and <= 600) WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(layout.LeftWidth);
                    if (layout.RightWidth is >= 420 and <= 1000) RootGrid().ColumnDefinitions[3].Width = new GridLength(layout.RightWidth);
                    if (layout.LogHeight is >= 140 and <= 700)
                        AutomaticWorkspaceGrid.RowDefinitions[2].Height = new GridLength(layout.LogHeight);
                }
            }
        }
        catch { }
        await RestoreLastConfigAsync();
        // 启动默认进入自动测试；资源配置仍保留为同一窗口中的后端编辑工作区。
        WorkspaceTabs.SelectedIndex = 1;
    }

    private Grid RootGrid() => (Grid)Content;

    private void SaveLayout()
    {
        try
        {
            var directory = Path.GetDirectoryName(_layoutPath)!;
            Directory.CreateDirectory(directory);
            var layout = new UiLayout(WorkspaceGrid.ColumnDefinitions[0].ActualWidth, RootGrid().ColumnDefinitions[3].ActualWidth, AutomaticWorkspaceGrid.RowDefinitions[2].ActualHeight);
            File.WriteAllText(_layoutPath, System.Text.Json.JsonSerializer.Serialize(layout));
        }
        catch { }
    }

    private void RebuildTree()
    {
        NormalizeReferenceDisplayNames(_configuration.Root);
        var selectedId = _selectedItem?.Id;
        ConfigurationTree.Items.Clear();
        var root = BuildTreeItem(_configuration.Root);
        _communicationTreeEntries.Clear();
        // 通信数据库不再混入资源导航树；通过顶部“通信数据库”入口独立查看和编辑。
        ConfigurationTree.Items.Add(root);
        if (selectedId is not null) SelectTreeItem(root, selectedId);
        RebuildTestCaseTree();
    }

    private static void NormalizeReferenceDisplayNames(ConfigurationTreeItem item)
    {
        if (item.Sequence is { } sequence)
            foreach (var step in sequence.Steps) NormalizeReferenceDisplayNames(step);
        foreach (var child in item.Children) NormalizeReferenceDisplayNames(child);
    }

    private static void NormalizeReferenceDisplayNames(ConfigurationStep step)
    {
        if (step.Kind == ConfigurationStepKind.引用序列)
        {
            if (step.Name == "调用序列") step.Name = "引用";
            else if (step.Name.StartsWith("调用：", StringComparison.Ordinal)) step.Name = step.Name[3..];
        }
        foreach (var child in step.Children) NormalizeReferenceDisplayNames(child);
    }

    private void RebuildTestCaseTree()
    {
        var snapshot = CaptureWorkspaceSelection();
        _restoringWorkspaceSelection = true;
        try
        {
            TestCaseTree.ItemsSource = null;
            var query = TestCaseSearchBox?.Text?.Trim() ?? string.Empty;
            TestCaseTree.ItemsSource = _configuration.TestPlan.Cases
                .Where(x => string.IsNullOrWhiteSpace(query) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Order)
                .ToList();
        }
        finally { _restoringWorkspaceSelection = false; }
        RestoreWorkspaceSelection(snapshot);
    }

    private void TestCaseSearch_TextChanged(object sender, TextChangedEventArgs e) => RebuildTestCaseTree();

    private void RestoreTestCaseSelection(TestCaseDefinition testCase, int? generation = null)
    {
        var restoreGeneration = generation ?? ++_selectionRestoreGeneration;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (restoreGeneration != _selectionRestoreGeneration) return;
            TestCaseTree.UpdateLayout();
            if (restoreGeneration != _selectionRestoreGeneration) return;
            if (TestCaseTree.ItemContainerGenerator.ContainerFromItem(testCase) is TreeViewItem item)
            {
                var wasRestoring = _restoringWorkspaceSelection;
                _restoringWorkspaceSelection = true;
                try { item.IsSelected = true; }
                finally { _restoringWorkspaceSelection = wasRestoring; }
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private sealed record WorkspaceSelectionSnapshot(
        string? TestCaseId,
        string? TestNodeId,
        IReadOnlyList<string> TestNodeIds,
        string? PropertyStepId,
        string? PropertyAnchorId,
        double InspectorOffset);

    private WorkspaceSelectionSnapshot CaptureWorkspaceSelection() => new(
        _selectedTestCase?.Id,
        _selectedTestNodeId ?? _selectedTestNode?.Id,
        _selectedTestNodeIds.ToArray(),
        _propertyStep?.Id,
        _propertyAnchorStep?.Id,
        StepInspectorScrollViewer?.VerticalOffset ?? 0);

    private void RestoreWorkspaceSelection(WorkspaceSelectionSnapshot snapshot)
    {
        var generation = ++_selectionRestoreGeneration;
        var wasRestoring = _restoringWorkspaceSelection;
        _restoringWorkspaceSelection = true;
        try
        {
            var testCase = _configuration.TestPlan.Cases.FirstOrDefault(x => x.Id == snapshot.TestCaseId);
            if (testCase is null) return;

            _selectedTestCase = testCase;
            _pendingTestCaseSelection = testCase;
            TestFlowTree.ItemsSource = testCase.Flow;
            RebuildFlowEditor();
            TestCaseTitle.Text = testCase.Name;
            TestCaseHint.Text = string.Empty;
            TestFlowTree.Visibility = Visibility.Visible;
            StepsTree.Visibility = Visibility.Collapsed;
            AutomaticHeaderControls.Visibility = Visibility.Visible;
            ResourceHeaderControls.Visibility = Visibility.Collapsed;
            if (TestJudgementLogicBox is not null)
            {
                TestJudgementLogicBox.ItemsSource = Enum.GetValues<TestJudgementLogic>();
                TestJudgementLogicBox.SelectedItem = testCase.JudgementLogic;
            }

            var node = snapshot.TestNodeId is null ? null : FindTestFlowNode(testCase.Flow, snapshot.TestNodeId);
            _selectedTestNode = node;
            _selectedTestNodeId = node?.Id;
            _selectedTestNodeIds.Clear();
            foreach (var id in snapshot.TestNodeIds)
                if (FindTestFlowNode(testCase.Flow, id) is not null) _selectedTestNodeIds.Add(id);
            RestoreTestCaseSelection(testCase, generation);
            if (node is not null) RestoreTestFlowSelection(node, generation: generation);

            if (snapshot.PropertyStepId is not null)
            {
                var step = FindConfigurationStep(_configuration.Root, snapshot.PropertyStepId);
                var anchor = snapshot.PropertyAnchorId is null ? step : FindConfigurationStep(_configuration.Root, snapshot.PropertyAnchorId);
                if (step is not null && ReferenceEquals(_propertyStep, step))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        StepInspectorScrollViewer.ScrollToVerticalOffset(Math.Min(snapshot.InspectorOffset, StepInspectorScrollViewer.ScrollableHeight));
                    }), DispatcherPriority.Loaded);
                }
                else if (step is not null)
                    ShowEditor(step, anchor);
            }
        }
        finally { _restoringWorkspaceSelection = wasRestoring; }
    }

    private TreeViewItem BuildTreeItem(ConfigurationTreeItem source)
    {
        var item = new TreeViewItem
        {
            Header = BuildTreeHeader(source),
            DataContext = source,
            IsExpanded = true,
            ToolTip = source.Kind switch
            {
                ConfigurationItemKind.序列 => "双击打开；F2 或右键重命名；Delete 删除",
                ConfigurationItemKind.原子操作 => "双击编辑；F2 或右键重命名；Delete 删除",
                _ => "右键新建对象或文件夹；F2 重命名；Delete 删除"
            }
        };
        // 拦截单击节点时 WPF 自动发出的 BringIntoView，避免用户浏览树时滚动位置被抢走。
        item.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(Tree_RequestBringIntoView), true);
        foreach (var child in source.Children) item.Items.Add(BuildTreeItem(child));
        return item;
    }

    private TreeViewItem BuildCommunicationTree(CanDatabase database)
    {
        var dbItem = SyntheticItem($"通信数据库 · {database.Name}", "database", database);
        var framesFolder = SyntheticItem("报文定义", "folder", database);
        var signalsFolder = SyntheticItem("信号定义", "folder", database);
        foreach (var message in database.Messages.OrderBy(x => x.Id))
        {
            var frame = SyntheticItem($"{message.Name} · 0x{message.Id:X}", "message", message);
            framesFolder.Children.Add(frame);
            foreach (var signal in message.Signals)
            {
                var signalItem = SyntheticItem($"{signal.Name} · 位{signal.StartBit} / {signal.BitLength} bit", "signal", new CommunicationSignalDefinition(message.Id, message.Extended, signal));
                frame.Children.Add(signalItem);
                var globalSignal = SyntheticItem($"{signal.Name} · 0x{message.Id:X}", "signal", new CommunicationSignalDefinition(message.Id, message.Extended, signal)); signalsFolder.Children.Add(globalSignal);
            }
        }
        dbItem.Children.Add(framesFolder); dbItem.Children.Add(signalsFolder);
        var dbTree = BuildTreeItem(dbItem);
        return dbTree;
    }

    private ConfigurationTreeItem SyntheticItem(string name, string kind, object value)
    {
        var item = new ConfigurationTreeItem { Id = $"synthetic-{Guid.NewGuid():N}", Name = name, Kind = ConfigurationItemKind.文件夹 };
        _communicationTreeEntries[item.Id] = new CommunicationTreeEntry(kind, value);
        return item;
    }

    private sealed record CommunicationTreeEntry(string Kind, object Value);
    private sealed record CommunicationSignalDefinition(uint Id, bool Extended, CanSignalDefinition Signal);

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != WorkspaceTabs) return;
        CloseStepEditor();
        if (WorkspaceTabs.SelectedIndex == 1) RebuildTestCaseTree();
        RefreshLogView();
    }

    private void Tree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 选择节点只改变选中状态，不偷偷重置用户当前的滚动位置。
        // 视图移动由滚轮、滚动条以及明确的拖放/上下移动操作触发。
        e.Handled = true;
    }

    private void ConfigurationTreeExpander_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && FindTreeViewItem(toggle) is { } item)
        {
            // 明确把箭头的状态写回容器，避免自定义模板只改变 ToggleButton 而不折叠子项。
            item.IsExpanded = toggle.IsChecked == true;
            e.Handled = true;
        }
    }

    private void TestCaseTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_restoringWorkspaceSelection) return;
        if (e.NewValue is TestCaseDefinition testCase)
            SwitchTestCase(testCase);
    }

    private void TestCaseTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext is not TestCaseDefinition testCase)
            return;
        // 自定义 TreeViewItem 模板不再决定点击是否能切换；预览阶段同步切换，
        // 随后的 WPF SelectedItemChanged 只会重复进入同一幂等入口。
        SwitchTestCase(testCase);
    }

    private void SwitchTestCase(TestCaseDefinition testCase)
    {
        ++_selectionRestoreGeneration;
        _pendingTestCaseSelection = null;
        if (_selectedTestCase is not null && _selectedTestNode is not null)
            _lastTestNodeByCase[_selectedTestCase.Id] = _selectedTestNode.Id;

        var changed = !ReferenceEquals(_selectedTestCase, testCase);
        if (changed) CloseStepEditor();
        _selectedTestCase = testCase;
        var rememberedNodeId = _lastTestNodeByCase.TryGetValue(testCase.Id, out var remembered) ? remembered : null;
        _selectedTestNode = rememberedNodeId is null
            ? testCase.Flow.FirstOrDefault()
            : FindTestFlowNode(testCase.Flow, rememberedNodeId) ?? testCase.Flow.FirstOrDefault();
        _selectedTestNodeId = _selectedTestNode?.Id;
        _selectedTestNodeIds.Clear();
        if (_selectedTestNode is not null) _selectedTestNodeIds.Add(_selectedTestNode.Id);
        TestFlowTree.Visibility = Visibility.Visible;
        StepsTree.Visibility = Visibility.Collapsed;
        AutomaticHeaderControls.Visibility = Visibility.Visible;
        ResourceHeaderControls.Visibility = Visibility.Collapsed;
        RefreshTestFlowSummaries();
        TestFlowTree.ItemsSource = testCase.Flow;
        RebuildFlowEditor();
        TestCaseTitle.Text = testCase.Name;
        TestCaseHint.Text = string.Empty;
        if (TestJudgementLogicBox is not null)
        {
            TestJudgementLogicBox.ItemsSource = Enum.GetValues<TestJudgementLogic>();
            TestJudgementLogicBox.SelectedItem = testCase.JudgementLogic;
        }
        if (_selectedTestNode is not null)
            ShowTestFlowNodeDetails(_selectedTestNode);
        AppendLog($"已切换测试用例：{testCase.Name}。");
        if (_runWorkspaceSelection is not null) _runWorkspaceSelection = CaptureWorkspaceSelection();
    }

    private void RefreshTestFlowSummaries()
    {
        foreach (var testCase in _configuration.TestPlan.Cases)
            foreach (var node in testCase.Flow)
                RefreshTestFlowSummary(node);
    }

    private void RefreshTestFlowSummary(TestFlowNode node)
    {
        if (node.Operation is { } operation)
        {
            node.DisplayKindOverride = operation.DisplayKind;
            if (operation.Kind == ConfigurationStepKind.报文解析 && node.ParseAssertion is { } assertion)
            {
                var resource = FindConfigurationStep(_configuration.Root, assertion.ParseResourceId);
                var resourceName = resource?.Name ?? operation.Parse.SignalName;
                // 解析步骤的属性名称是操作对象的名称源。旧配置可能还保留了
                // “信号名 + 期望值”的节点自动名称，打开或刷新时先统一到操作名称。
                if (!node.NameIsUserDefined && !string.IsNullOrWhiteSpace(operation.Name))
                    node.Name = operation.Name;
                var condition = string.IsNullOrWhiteSpace(assertion.Expectation)
                    ? node.LastStatus == TestResultStatus.仅观察 ? "仅观察（已执行，无判定条件）" : "无判定条件"
                    : $"期望 {assertion.Expectation}";
                var generatedName = string.IsNullOrWhiteSpace(assertion.Expectation)
                    ? resourceName
                    : $"{resourceName} {assertion.Expectation}";
                if (!node.NameIsUserDefined && string.IsNullOrWhiteSpace(operation.Name))
                    node.Name = generatedName;
                node.DisplaySummary = $"{resourceName} · 0x{(resource?.Parse.Id ?? operation.Parse.Id):X}，{condition}，超时 {assertion.TimeoutMs} ms";
            }
            else
                node.DisplaySummary = $"{DisplayStepKind(operation.Kind)}：{operation.Summary}";
        }
        else
        {
            var relation = new ResourceRelationService().Resolve(_configuration, node);
            if (relation.IsUsableReference)
            {
                var target = relation.Sequence!;
                var resourceChoice = AllResources().FirstOrDefault(x => x.Id == node.ResourceId || x.SequenceId == node.SequenceId);
                var resourceName = target.Steps.Count == 1 ? target.Steps[0].Name : resourceChoice?.Name ?? target.Name;
                node.Name = resourceName;
                node.DisplayKindOverride = target.Steps.Count == 1 ? target.Steps[0].DisplayKind : "组合";
                node.DisplaySummary = $"共享资源 · {ReferenceSummaryResolver.Resolve(target)}";
            }
            else if (node.Children.Count > 0)
            {
                node.DisplayKindOverride = "组合";
                node.DisplaySummary = $"组合，包含 {node.Children.Count} 个节点";
            }
            else if (relation.State == ResourceRelationState.空引用)
            {
                node.DisplayKindOverride = "空引用";
                node.DisplaySummary = "未选择资源 · 请使用“插入资源”选择";
            }
            else
            {
                node.DisplayKindOverride = "引用失效";
                node.DisplaySummary = "资源不可用 · 引用已失效，请重新选择资源";
            }
        }
        foreach (var child in node.Children) RefreshTestFlowSummary(child);
    }

    private static bool ShouldSyncGeneratedParseName(TestFlowNode node, ConfigurationStep operation, string resourceName)
    {
        if (node.NameIsUserDefined) return false;
        if (string.IsNullOrWhiteSpace(node.Name) || node.Name == operation.Name || node.Name == "报文解析") return true;
        if (!node.Name.StartsWith(resourceName, StringComparison.Ordinal)) return false;
        return node.Name.Contains('=') || node.Name.Contains('>') || node.Name.Contains('<') || node.Name.Contains("仅观察", StringComparison.Ordinal);
    }

    private static bool ShouldSyncGeneratedReferenceName(TestFlowNode node) =>
        string.IsNullOrWhiteSpace(node.Name) ||
        node.Name is "引用" or "引用序列" or "调用序列" ||
        node.Name.StartsWith("调用：", StringComparison.Ordinal);

    private void TestFlowTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_restoringWorkspaceSelection) return;
        ++_selectionRestoreGeneration;
        _selectedTestNode = e.NewValue as TestFlowNode;
        if (_selectedTestNode is not null) _selectedTestNodeId = _selectedTestNode.Id;
        if (_selectedTestCase is not null && _selectedTestNode is not null)
            _lastTestNodeByCase[_selectedTestCase.Id] = _selectedTestNode.Id;
        if (ReferenceEquals(_selectedTestNode, _pendingTestNodeSelection)) _pendingTestNodeSelection = null;
        if (_selectedTestNode is null) CloseStepEditor();
        else ShowTestFlowNodeDetails(_selectedTestNode);
        if (_runWorkspaceSelection is not null) _runWorkspaceSelection = CaptureWorkspaceSelection();
    }

    private void RebuildFlowEditor()
    {
        var selectedId = _selectedFlowEditorRow?.Id;
        _flowEditorRoots.Clear();
        _flowInsertAnchor = null;
        _flowInsertKind = null;
        var wasRestoring = _restoringWorkspaceSelection;
        _restoringWorkspaceSelection = true;
        try
        {
            if (_selectedTestCase is not null)
            {
                // QUICK always renders persisted nodes; synthetic demo rows were not executable.
                _flowEditorRoots.AddRange(FlowEditorRow.FromNodeTrees(_selectedTestCase.Flow));
            }
            RebuildVisibleFlowEditor();
            FlowEditorList.SelectedItem = _flowEditorRows.FirstOrDefault(x => x.Id == selectedId)
                ?? _flowEditorRows.FirstOrDefault(x => x.SourceNode?.Id == _selectedTestNodeId);
        }
        finally { _restoringWorkspaceSelection = wasRestoring; }
    }

    private static bool ContainsFlowControl(IEnumerable<TestFlowNode> nodes)
        => nodes.Any(node => node.FlowControl is not null || ContainsFlowControl(node.Children));

    private void BuildStaticFlowEditor()
    {
        var sourceNodes = _selectedTestCase!.Flow;
        TestFlowNode? FindSource(string name) => FindTestFlowNode(sourceNodes, sourceNodes.FirstOrDefault(x => x.Name == name)?.Id ?? "");
        var communication = FlowEditorRow.Step("ui:static:communication", "检测 ECU 通信", "Heartbeat / 通信状态", "输出 Communication", "通信门禁");
        var communicationIf = FlowEditorRow.If("ui:static:communication-if", "检测 ECU 通信", "未通过");
        AddThen(communicationIf, FlowEditorRow.Step("ui:static:communication-fail", "结束测试", "ECU 无法通信", "FAIL / STOP", "通信失败"));

        var debugRead = FlowEditorRow.Step("ui:static:debug-read", "读取 ECU 调试状态", "DebugMode", "输出 DebugMode", "");
        var debugIf = FlowEditorRow.If("ui:static:debug-if", "读取 ECU 调试状态", "未通过");
        var debugThen = AddThenContainer(debugIf);
        AddChild(debugThen, FlowEditorRow.Step("ui:static:activate-debug", "发送激活调试报文", "ActivateDebug", "发送完成", ""));
        var debugWhen = FlowEditorRow.When("ui:static:debug-when", "读取 ECU 调试状态", "通过", "20", "1000");
        AddHitTimeout(debugWhen, "继续主线", "结束测试");
        AddChild(debugThen, debugWhen);

        var ignRead = FlowEditorRow.Step("ui:static:ign-read", "检查 IGN", "IGN Voltage", "输出 IGNState", "");
        var ignIf = FlowEditorRow.If("ui:static:ign-if", "检查 IGN", "未通过");
        var ignThen = AddThenContainer(ignIf);
        AddChild(ignThen, FlowEditorRow.Step("ui:static:ign-on", "打开 IGN", "IGN = ON", "执行", ""));
        var ignWhen = FlowEditorRow.When("ui:static:ign-when", "检查 IGN", "通过", "20", "500");
        AddHitTimeout(ignWhen, "继续主线", "结束测试");
        AddChild(ignThen, ignWhen);

        var switchRow = FlowEditorRow.Switch("ui:static:caliper-switch", "EPB_Status > CaliperState");
        var released = FlowEditorRow.Case("ui:static:caliper-released", "已释放");
        AddChild(released, FlowEditorRow.Step("ui:static:released-continue", "直接继续", "状态满足", "继续主线", ""));
        var parked = FlowEditorRow.Case("ui:static:caliper-parked", "已驻车");
        AddChild(parked, FlowEditorRow.Step("ui:static:release-key", "执行释放按键动作", "ReleaseKey", "发送完成", ""));
        var releaseWhen = FlowEditorRow.When("ui:static:release-when", "读取卡钳状态", "通过", "20", "1000");
        AddHitTimeout(releaseWhen, "继续主线", "结束测试");
        AddChild(parked, releaseWhen);
        var defaultCase = FlowEditorRow.Default("ui:static:caliper-default");
        AddChild(defaultCase, FlowEditorRow.Step("ui:static:caliper-unknown", "结束测试", "状态未知", "FAIL / STOP", "默认分支"));
        AddChild(switchRow, released);
        AddChild(switchRow, parked);
        AddChild(switchRow, defaultCase);

        var formal = FlowEditorRow.Section("ui:static:formal", "正式测试 · 静态驻车");
        AddChild(formal, FlowEditorRow.FromNodeOrPlaceholder(FindSource("驻车按键"), "ui:static:park-key", "驻车按键", "ParkKey", "发送完成", "正式动作"));
        var parkWhen = FlowEditorRow.When("ui:static:park-when", "按键状态", "通过", "20", "1000");
        var hit = FlowEditorRow.Hit("ui:static:park-hit");
        AddChild(hit, FlowEditorRow.Step("ui:static:waveform", "左右电机驻车波形判定", "MotorLeft / MotorRight", "波形通过", "判定点"));
        AddChild(parkWhen, hit);
        AddChild(parkWhen, FlowEditorRow.Timeout("ui:static:park-timeout", "测试失败"));
        AddChild(formal, parkWhen);

        _flowEditorRoots.AddRange([communication, communicationIf, debugRead, debugIf, ignRead, ignIf, switchRow, formal]);
    }

    private static FlowEditorRow AddThenContainer(FlowEditorRow parent)
    {
        var then = FlowEditorRow.Then($"{parent.Id}:then");
        AddChild(parent, then);
        return then;
    }

    private static void AddThen(FlowEditorRow parent, FlowEditorRow child) => AddChild(AddThenContainer(parent), child);

    private static void AddHitTimeout(FlowEditorRow parent, string hitText, string timeoutText)
    {
        var hit = FlowEditorRow.Hit($"{parent.Id}:hit");
        AddChild(hit, FlowEditorRow.Step($"{parent.Id}:hit-step", hitText, "条件满足", "继续主线", ""));
        AddChild(parent, hit);
        AddChild(parent, FlowEditorRow.Timeout($"{parent.Id}:timeout", timeoutText));
    }

    private static void AddChild(FlowEditorRow parent, FlowEditorRow child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }

    private void RebuildVisibleFlowEditor()
    {
        _flowEditorRows.Clear();
        var rootIndex = 1;
        foreach (var root in _flowEditorRoots)
        {
            FlattenFlowEditorRow(root, rootIndex.ToString(), 0, rootIndex);
            rootIndex++;
        }
    }

    private void FlattenFlowEditorRow(FlowEditorRow row, string prefix, int depth, int rootIndex)
    {
        row.Number = depth == 0 ? rootIndex.ToString() : row.IsStructural ? "" : prefix;
        row.Depth = depth;
        row.IndentGuides.Clear();
        for (var index = 0; index < depth; index++) row.IndentGuides.Add(index);
        _flowEditorRows.Add(row);
        if (!row.IsExpanded) return;
        var childNumber = 1;
        foreach (var child in row.Children)
        {
            var childPrefix = row.IsStructural ? prefix : row.Number;
            if (!child.IsStructural) childPrefix = $"{prefix}.{childNumber++}";
            FlattenFlowEditorRow(child, childPrefix, depth + 1, rootIndex);
        }
    }

    private void FlowRowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FlowEditorRow row } || !row.IsContainer) return;
        row.IsExpanded = !row.IsExpanded;
        RebuildVisibleFlowEditor();
        FlowEditorList.SelectedItem = row;
        e.Handled = true;
    }

    private void FlowEditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringWorkspaceSelection || FlowEditorList.SelectedItem is not FlowEditorRow row) return;
        _selectedFlowEditorRow = row;
        RefreshFlowActionAvailability(row);
        if (row.SourceNode is { } node)
        {
            _selectedTestNode = node;
            _selectedTestNodeId = node.Id;
            ShowTestFlowNodeDetails(node);
        }
        else if (row.IsControl)
        {
            _selectedTestNode = null;
            _selectedTestNodeId = null;
            ShowFlowControlDetails(row);
        }
        else
        {
            _selectedTestNode = null;
            _selectedTestNodeId = null;
            ShowFlowPlaceholderDetails(row);
        }
    }

    private void RefreshFlowActionAvailability(FlowEditorRow? row)
    {
        var genericAllowed = row?.IsControl != true;
        AddTestFlowNodeButton.IsEnabled = genericAllowed;
        AddFlowIfButton.IsEnabled = genericAllowed;
        AddFlowWhenButton.IsEnabled = genericAllowed;
        AddFlowSwitchButton.IsEnabled = genericAllowed;
        AddFlowLoopButton.IsEnabled = genericAllowed;
        AddFlowElseButton.IsEnabled = row?.ControlKind == FlowEditorKind.If &&
            !row.Children.Any(x => x.ControlKind == FlowEditorKind.Else);
        AddFlowCaseButton.IsEnabled = row?.ControlKind == FlowEditorKind.Switch;
    }

    private void ShowFlowPlaceholderDetails(FlowEditorRow row)
    {
        InspectorTitle.Text = row.Name;
        StepEditor.Content = new Border
        {
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(255, 249, 237)),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "当前为流程示例行", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(138, 101, 0)) },
                    new TextBlock { Text = "该行用于验证流程阅读和插入位置；真实普通操作参数请在已绑定的操作行中编辑。", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(138, 101, 0)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        OpenInspector();
    }

    private void ShowFlowControlDetails(FlowEditorRow row)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = row.ControlKind switch
            {
                FlowEditorKind.If => "IF · 结果引用",
                FlowEditorKind.When => "WHEN · 重复检查结果",
                FlowEditorKind.Loop => "LOOP · 重复执行步骤",
                _ => "SWITCH · 通信数据库信号"
            },
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = row.KindBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(header);

        if (row.ControlKind == FlowEditorKind.Loop)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "每轮依次执行 BODY 内的真实步骤；循环不自动释放 KEY、复位 IGN 或改变夹具。出现执行错误立即停止，取消可中断等待。",
                Foreground = Brushes.SlateGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var repeatCount = new TextBox { Text = row.LoopCountText, Width = 75 };
            var repeatDelay = new TextBox { Text = row.LoopDelayMsText, Width = 95 };
            var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
            void CommitLoop()
            {
                if (!int.TryParse(repeatCount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 100 ||
                    !int.TryParse(repeatDelay.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var delay) || delay is < 0 or > 60000)
                {
                    error.Text = "次数必须为 1～100，轮间等待必须为 0～60000 ms。未保存非法值。";
                    return;
                }
                error.Text = "";
                row.LoopCountText = count.ToString(CultureInfo.InvariantCulture);
                row.LoopDelayMsText = delay.ToString(CultureInfo.InvariantCulture);
                row.ParameterText = $"重复 {count} 次 · 间隔 {delay} ms";
                SyncFlowControlSource(row);
                InspectorTitle.Text = "LOOP · " + row.ParameterText;
                FlowEditorList.Items.Refresh();
            }
            repeatCount.LostFocus += (_, _) => CommitLoop();
            repeatDelay.LostFocus += (_, _) => CommitLoop();
            repeatCount.KeyDown += (_, args) => { if (args.Key == Key.Enter) { CommitLoop(); args.Handled = true; } };
            repeatDelay.KeyDown += (_, args) => { if (args.Key == Key.Enter) { CommitLoop(); args.Handled = true; } };
            panel.Children.Add(CreateLabeledControl("循环次数", repeatCount));
            panel.Children.Add(CreateLabeledControl("轮间等待", repeatDelay,
                new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center }));
            panel.Children.Add(error);
            InspectorTitle.Text = "LOOP · " + row.ParameterText;
            StepEditor.Content = panel;
            OpenInspector();
            return;
        }

        if (row.ControlKind == FlowEditorKind.Switch)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "SWITCH 直接读取通信数据库定义的当前 Value；这里不编辑比较符或期望值。",
                Foreground = Brushes.SlateGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var signalBox = new ComboBox
            {
                ItemsSource = AvailableSwitchSignals(row),
                SelectedItem = row.SignalDefinitionText,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 210
            };
            signalBox.SelectionChanged += (_, _) =>
            {
                if (signalBox.SelectedItem is not string signal) return;
                row.SignalDefinitionText = signal;
                row.SignalDefinitionKey = FindSignalDefinitionKey(signal);
                SyncFlowControlSource(row);
                row.ParameterText = signal;
                RebuildVisibleFlowEditor();
                FlowEditorList.SelectedItem = row;
            };
            panel.Children.Add(CreateLabeledControl("信号定义", signalBox));
            var switchTimeout = new TextBox { Text = row.TimeoutMsText, Width = 95 };
            var timeoutError = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
            void CommitSwitchTimeout()
            {
                if (!int.TryParse(switchTimeout.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms is < 1 or > 60000)
                {
                    timeoutError.Text = "等待时限必须为 1～60000 ms；非法值未保存。";
                    return;
                }
                timeoutError.Text = "";
                row.TimeoutMsText = ms.ToString(CultureInfo.InvariantCulture);
                if (row.SourceNode?.FlowControl is { } control) control.TimeoutMs = ms;
            }
            switchTimeout.LostFocus += (_, _) => CommitSwitchTimeout();
            switchTimeout.KeyDown += (_, args) => { if (args.Key == Key.Enter) { CommitSwitchTimeout(); args.Handled = true; } };
            panel.Children.Add(CreateLabeledControl("等待时限", switchTimeout,
                new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center }));
            panel.Children.Add(timeoutError);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = row.ControlKind == FlowEditorKind.When
                    ? "WHEN 只重复执行已有判断步骤并等待它的 Verdict，不在这里重新配置报文解析。"
                    : "IF 只消费已有显式步骤产生的 Verdict，不采集、不解码、不重新配置判断条件。",
                Foreground = Brushes.SlateGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var resultBox = new ComboBox
            {
                ItemsSource = AvailableFlowResultReferences(row),
                SelectedItem = row.ResultReferenceText,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 210
            };
            var verdictBox = new ComboBox
            {
                ItemsSource = new[] { "通过", "未通过" },
                SelectedItem = row.VerdictText,
                Width = 100,
                Margin = new Thickness(6, 0, 0, 0)
            };
            void CommitReference()
            {
                if (resultBox.SelectedItem is string reference) row.ResultReferenceText = reference;
                if (verdictBox.SelectedItem is string verdict) row.VerdictText = verdict;
                row.ResultReferenceKey = FindResultReferenceKey(row, row.ResultReferenceText);
                SyncFlowControlSource(row);
                row.ParameterText = $"{row.ResultReferenceText} · {row.VerdictText}"
                    + (row.ControlKind == FlowEditorKind.When ? $" · {row.PeriodMsText} ms" : "");
                RebuildVisibleFlowEditor();
                FlowEditorList.SelectedItem = row;
            }
            resultBox.SelectionChanged += (_, _) => CommitReference();
            verdictBox.SelectionChanged += (_, _) => CommitReference();
            panel.Children.Add(CreateLabeledControl("结果步骤", resultBox, verdictBox));

            if (row.ControlKind == FlowEditorKind.When)
            {
                var period = new TextBox { Text = row.PeriodMsText, Width = 80 };
                var timeout = new TextBox { Text = row.TimeoutMsText, Width = 90 };
                void CommitWhen()
                {
                    row.PeriodMsText = period.Text.Trim();
                    row.TimeoutMsText = timeout.Text.Trim();
                    SyncFlowControlSource(row);
                    row.ParameterText = $"{row.ResultReferenceText} · {row.VerdictText} · {row.PeriodMsText} ms";
                    row.PathText = $"超时 {row.TimeoutMsText} ms";
                    RebuildVisibleFlowEditor();
                    FlowEditorList.SelectedItem = row;
                }
                period.LostFocus += (_, _) => CommitWhen();
                timeout.LostFocus += (_, _) => CommitWhen();
                period.KeyDown += (_, args) =>
                {
                    if (args.Key != Key.Enter) return;
                    CommitWhen();
                    args.Handled = true;
                };
                timeout.KeyDown += (_, args) =>
                {
                    if (args.Key != Key.Enter) return;
                    CommitWhen();
                    args.Handled = true;
                };
                panel.Children.Add(CreateLabeledControl("周期 / 超时", period, new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center }, timeout, new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center }));
            }
        }

        InspectorTitle.Text = $"{row.DisplayKind} · {row.ParameterText}";
        StepEditor.Content = panel;
        OpenInspector();
    }

    private void ShowCasePathDetails(TestFlowNode node)
    {
        var value = new TextBox
        {
            Text = node.PathValue,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        void Commit()
        {
            node.PathValue = value.Text.Trim();
            node.Name = string.IsNullOrWhiteSpace(node.PathValue) ? "CASE 未配置值" : $"CASE {node.PathValue}";
            RefreshTestFlowSummary(node);
            RefreshTestFlowTreePreservingSelection(node.Id);
        }
        value.LostFocus += (_, _) => Commit();
        value.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            Commit();
            args.Handled = true;
        };
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "CASE · 匹配值", FontWeight = FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(new TextBlock
        {
            Text = "配置通信数据库信号解码后的 Value；CASE 不创建另一份读取操作。",
            Foreground = Brushes.SlateGray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 8)
        });
        panel.Children.Add(CreateLabeledControl("匹配值", value));
        InspectorTitle.Text = $"CASE · {node.PathValue}";
        StepEditor.Content = panel;
        OpenInspector();
    }

    private static StackPanel CreateLabeledControl(string label, params UIElement[] controls)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        line.Children.Add(new TextBlock { Text = label, Width = 74, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center });
        foreach (var control in controls) line.Children.Add(control);
        return line;
    }

    private IReadOnlyList<string> AvailableFlowResultReferences(FlowEditorRow current)
    {
        var values = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }

        Add(current.ResultReferenceText);
        foreach (var row in EnumerateFlowRows(_flowEditorRoots))
        {
            if (row.Id == current.Id) break;
            if (row.ControlKind != FlowEditorKind.Step) continue;
            if (row.SourceNode?.Operation?.Kind == ConfigurationStepKind.报文解析 &&
                !string.IsNullOrWhiteSpace(row.SourceNode.ParseAssertion?.Expectation ?? row.SourceNode.Operation.Parse.Expectation))
                Add(row.Name);
        }
        return values.Count == 0 ? ["请选择已有步骤"] : values;
    }

    private IReadOnlyList<string> AvailableSwitchSignals(FlowEditorRow current)
    {
        var values = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }

        Add(current.SignalDefinitionText);
        foreach (var message in _configuration.CommunicationDatabase?.Messages ?? [])
        foreach (var signal in message.Signals ?? [])
            Add($"{CanDefinitionDisplay.MessagePrimaryName(message)} > {CanDefinitionDisplay.SignalPrimaryName(signal)}");
        return values.Count == 0 ? ["请选择通信数据库信号"] : values;
    }

    private string FindResultReferenceKey(FlowEditorRow current, string display)
    {
        if (!string.IsNullOrWhiteSpace(current.ResultReferenceKey) &&
            string.Equals(current.ResultReferenceText, display, StringComparison.Ordinal))
            return current.ResultReferenceKey;
        var matches = EnumerateFlowRows(_flowEditorRoots)
            .Where(x => x.ControlKind == FlowEditorKind.Step && x.SourceNode is not null &&
                string.Equals(x.Name, display, StringComparison.Ordinal))
            .Select(x => x.SourceNode!.Id).Distinct(StringComparer.Ordinal).ToArray();
        // Duplicate display names are ambiguous: fail preflight instead of binding silently.
        return matches.Length == 1 ? matches[0] : "";
    }

    private string FindSignalDefinitionKey(string display)
    {
        foreach (var message in _configuration.CommunicationDatabase?.Messages ?? [])
        foreach (var signal in message.Signals ?? [])
        {
            var text = $"{CanDefinitionDisplay.MessagePrimaryName(message)} > {CanDefinitionDisplay.SignalPrimaryName(signal)}";
            if (string.Equals(text, display, StringComparison.Ordinal))
                return $"{message.DefinitionId}|{signal.DefinitionId}";
        }
        return display.Contains('|', StringComparison.Ordinal) ? display : "";
    }

    private static void SyncFlowControlSource(FlowEditorRow row)
    {
        if (row.SourceNode?.FlowControl is not { } control) return;
        if (row.ControlKind == FlowEditorKind.Loop)
        {
            if (int.TryParse(row.LoopCountText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is >= 1 and <= 100)
                control.RepeatCount = count;
            if (int.TryParse(row.LoopDelayMsText, NumberStyles.None, CultureInfo.InvariantCulture, out var delay) && delay is >= 0 and <= 60000)
                control.RepeatDelayMs = delay;
        }
        else if (row.ControlKind is FlowEditorKind.If or FlowEditorKind.When)
        {
            if (!string.IsNullOrWhiteSpace(row.ResultReferenceKey))
                control.ResultReference = new ResultReference(row.ResultReferenceKey, ResultChannel.Verdict);
            control.RequiredVerdict = string.Equals(row.VerdictText, "未通过", StringComparison.Ordinal)
                ? ResultVerdict.Failed
                : ResultVerdict.Passed;
            if (row.ControlKind == FlowEditorKind.When)
            {
                if (int.TryParse(row.PeriodMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var period))
                    control.PeriodMs = Math.Max(1, period);
                if (int.TryParse(row.TimeoutMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
                    control.TimeoutMs = Math.Max(1, timeout);
            }
        }
        else if (row.ControlKind == FlowEditorKind.Switch &&
                 row.SignalDefinitionKey.Split('|') is [var messageId, var signalId] &&
                 !string.IsNullOrWhiteSpace(messageId) &&
                 !string.IsNullOrWhiteSpace(signalId))
        {
            control.SignalDefinition = new SignalDefinitionReference(messageId, signalId);
        }
    }

    private static IEnumerable<FlowEditorRow> EnumerateFlowRows(IEnumerable<FlowEditorRow> roots)
    {
        foreach (var row in roots)
        {
            yield return row;
            foreach (var child in EnumerateFlowRows(row.Children)) yield return child;
        }
    }

    private void PrepareFlowInsert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } || _selectedTestCase is null)
        {
            AppendLog("请先选择测试用例，再添加流程控制。");
            return;
        }
        // Empty TEST program: let the first IF/WHEN/SWITCH/LOOP become a real root node.
        if (_selectedFlowEditorRow is null)
        {
            var rootControl = CreateTestFlowControlNode(kind);
            if (rootControl is null) return;
            InsertTestFlowNode(rootControl);
            AppendLog($"已在测试用例根流程添加 {kind}；请在右侧配置并在路径内添加步骤。");
            return;
        }
        if (_selectedFlowEditorRow.IsControl)
        {
            AppendLog("控制主行不能直接插入通用内容，请选择 THEN、ELSE、HIT、TIMEOUT、CASE、DEFAULT 或普通步骤。");
            return;
        }
        if (_flowInsertAnchor is not null) _flowInsertAnchor.IsInsertAnchor = false;
        _flowInsertAnchor = _selectedFlowEditorRow;
        _flowInsertKind = kind;
        _flowInsertAnchor.IsInsertAnchor = true;
        FlowInsertPathText.Text = BuildFlowInsertPath(_flowInsertAnchor);
        FlowInsertKindText.Text = $"＋ {kind}";
        FlowInsertPreviewPanel.Visibility = Visibility.Visible;
    }

    private void ConfirmFlowInsert_Click(object sender, RoutedEventArgs e)
    {
        if (_flowInsertAnchor is null || string.IsNullOrWhiteSpace(_flowInsertKind)) return;
        if (_flowInsertAnchor.SourceNode is { } source && _selectedTestCase is not null)
        {
            var insertedNode = CreateTestFlowControlNode(_flowInsertKind);
            if (insertedNode is null)
            {
                CancelFlowInsert_Click(sender, e);
                return;
            }

            var owner = source.PathKind != TestFlowPathKind.None
                ? source.Children
                : FindTestFlowOwner(_selectedTestCase.Flow, source.Id);
            if (owner is null)
            {
                AppendLog("无法确定流程插入位置。");
                CancelFlowInsert_Click(sender, e);
                return;
            }

            var index = source.PathKind != TestFlowPathKind.None
                ? owner.Count
                : Math.Max(0, owner.IndexOf(source) + 1);
            owner.Insert(Math.Clamp(index, 0, owner.Count), insertedNode);
            var insertionPath = BuildFlowInsertPath(_flowInsertAnchor);
            var insertionKind = _flowInsertKind;
            _selectedTestNode = insertedNode;
            _selectedTestNodeId = insertedNode.Id;
            _selectedTestNodeIds.Clear();
            _pendingTestNodeSelection = insertedNode;
            CancelFlowInsert_Click(sender, e);
            RefreshTestFlowTreePreservingSelection(insertedNode.Id);
            AppendLog($"已在 {insertionPath} 插入 {insertionKind}。");
            return;
        }

        var inserted = CreateFlowEditorControl(_flowInsertKind);
        IList<FlowEditorRow> presentationOwner = _flowInsertAnchor.Parent?.Children is { } children ? children : _flowEditorRoots;
        var presentationIndex = presentationOwner.IndexOf(_flowInsertAnchor);
        inserted.Parent = _flowInsertAnchor.Parent;
        presentationOwner.Insert(presentationIndex < 0 ? presentationOwner.Count : presentationIndex + 1, inserted);
        CancelFlowInsert_Click(sender, e);
        RebuildVisibleFlowEditor();
        FlowEditorList.SelectedItem = inserted;
    }

    private static TestFlowNode? CreateTestFlowControlNode(string kind)
    {
        var node = new TestFlowNode
        {
            Kind = TestFlowNodeKind.组合,
            Name = kind,
            FlowControl = kind switch
            {
                "IF" => new TestFlowControlDefinition { Kind = TestFlowControlKind.Condition },
                "WHEN" => new TestFlowControlDefinition { Kind = TestFlowControlKind.WaitForVerdict },
                "SWITCH" => new TestFlowControlDefinition { Kind = TestFlowControlKind.ValueBranch },
                "LOOP" => new TestFlowControlDefinition { Kind = TestFlowControlKind.Repeat, RepeatCount = 2, RepeatDelayMs = 0 },
                _ => null
            }
        };
        if (node.FlowControl is null) return null;

        switch (kind)
        {
            case "IF":
                node.Children.Add(new TestFlowNode { Name = "THEN", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Then });
                break;
            case "WHEN":
                node.Children.Add(new TestFlowNode { Name = "HIT", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Hit });
                node.Children.Add(new TestFlowNode { Name = "TIMEOUT", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Timeout });
                break;
            case "SWITCH":
                node.Children.Add(new TestFlowNode { Name = "DEFAULT", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Default });
                break;
            case "LOOP":
                node.Children.Add(new TestFlowNode { Name = "BODY", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Body });
                break;
        }
        return node;
    }

    private static FlowEditorRow CreateFlowEditorControl(string kind) => kind switch
    {
        "IF" => FlowEditorRow.If($"ui:new:if:{Guid.NewGuid():N}", "请选择已有步骤", "请选择 Verdict"),
        "WHEN" => FlowEditorRow.When($"ui:new:when:{Guid.NewGuid():N}", "请选择已有步骤", "通过", "20", "1000"),
        "SWITCH" => FlowEditorRow.Switch($"ui:new:switch:{Guid.NewGuid():N}", "请选择通信数据库信号"),
        "LOOP" => FlowEditorRow.Section($"ui:new:loop:{Guid.NewGuid():N}", "LOOP"),
        _ => FlowEditorRow.Step($"ui:new:step:{Guid.NewGuid():N}", "新建步骤", "未配置", "待配置", "")
    };

    private void CancelFlowInsert_Click(object sender, RoutedEventArgs e)
    {
        if (_flowInsertAnchor is not null) _flowInsertAnchor.IsInsertAnchor = false;
        _flowInsertAnchor = null;
        _flowInsertKind = null;
        FlowInsertPreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void AddFlowElse_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedFlowEditorRow?.ControlKind == FlowEditorKind.If ? _selectedFlowEditorRow : null;
        if (parent is null || parent.Children.Any(x => x.ControlKind == FlowEditorKind.Else)) return;
        if (parent.SourceNode?.FlowControl?.Kind == TestFlowControlKind.Condition)
        {
            var elsePath = new TestFlowNode { Name = "ELSE", Kind = TestFlowNodeKind.组合, PathKind = TestFlowPathKind.Else };
            parent.SourceNode.Children.Add(elsePath);
            _selectedTestNode = elsePath;
            _selectedTestNodeId = elsePath.Id;
            _selectedTestNodeIds.Clear();
            _pendingTestNodeSelection = elsePath;
            RefreshTestFlowTreePreservingSelection(elsePath.Id);
            AppendLog("已添加 ELSE，并切换到 ELSE 路径。");
            return;
        }
        var row = FlowEditorRow.Else($"{parent.Id}:else");
        row.Parent = parent;
        parent.Children.Add(row);
        RebuildVisibleFlowEditor();
        FlowEditorList.SelectedItem = row;
    }

    private void AddFlowCase_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedFlowEditorRow?.ControlKind == FlowEditorKind.Switch ? _selectedFlowEditorRow : null;
        if (parent is null) return;
        if (parent.SourceNode?.FlowControl?.Kind == TestFlowControlKind.ValueBranch)
        {
            var casePath = new TestFlowNode
            {
                Name = "CASE 未配置值",
                Kind = TestFlowNodeKind.组合,
                PathKind = TestFlowPathKind.Case,
                PathValue = ""
            };
            var sourceChildren = parent.SourceNode!.Children;
            var sourceDefaultIndex = sourceChildren.ToList().FindIndex(x => x.PathKind == TestFlowPathKind.Default);
            sourceChildren.Insert(sourceDefaultIndex < 0 ? sourceChildren.Count : sourceDefaultIndex, casePath);
            _selectedTestNode = casePath;
            _selectedTestNodeId = casePath.Id;
            _selectedTestNodeIds.Clear();
            _pendingTestNodeSelection = casePath;
            RefreshTestFlowTreePreservingSelection(casePath.Id);
            AppendLog("已添加 CASE，并切换到 CASE 路径。");
            return;
        }
        var row = FlowEditorRow.Case($"{parent.Id}:case:{Guid.NewGuid():N}", "未配置值");
        row.Parent = parent;
        var presentationDefaultIndex = parent.Children.ToList().FindIndex(x => x.ControlKind == FlowEditorKind.Default);
        parent.Children.Insert(presentationDefaultIndex < 0 ? parent.Children.Count : presentationDefaultIndex, row);
        RebuildVisibleFlowEditor();
        FlowEditorList.SelectedItem = row;
    }

    private static string BuildFlowInsertPath(FlowEditorRow row)
    {
        var parts = new List<string>();
        for (var current = row; current is not null; current = current.Parent)
            if (current.IsStructural || current.IsControl) parts.Insert(0, current.DisplayKind);
        return parts.Count == 0 ? "主流程" : string.Join(" > ", parts);
    }

    private void LogPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        _logPanelCollapsed = !_logPanelCollapsed;
        AutomaticWorkspaceGrid.RowDefinitions[2].Height = _logPanelCollapsed
            ? new GridLength(34)
            : new GridLength(1, GridUnitType.Star);
        LogRows.Visibility = _logPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LogEmptyState.Visibility = _logPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LogPanelToggleButton.Content = _logPanelCollapsed ? "⌃" : "⌄";
    }

    private void CanPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        _canPanelCollapsed = !_canPanelCollapsed;
        RootGrid().ColumnDefinitions[3].Width = _canPanelCollapsed
            ? new GridLength(34)
            : new GridLength(420);
        CanPanelContentGrid.Visibility = _canPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CanPanelExpandButton.Visibility = _canPanelCollapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TestJudgementLogic_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedTestCase is not null && TestJudgementLogicBox.SelectedItem is TestJudgementLogic logic)
        {
            _selectedTestCase.JudgementLogic = logic;
            AppendLog($"测试用例 [{_selectedTestCase.Name}] 判定逻辑：{logic}。 ");
        }
    }

    private void TestFlowTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject) is { } item && item.DataContext is TestFlowNode node)
        {
            _selectedTestNode = node;
            // 等双击输入完成后再打开属性框，避免第二次鼠标事件把刚打开的框关闭。
            _ = Dispatcher.BeginInvoke(new Action(() => ShowTestFlowNodeDetails(node)), DispatcherPriority.ContextIdle);
            e.Handled = true;
        }
    }

    private void TestFlowTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _testNodeDragStart = e.GetPosition(TestFlowTree);
        _testNodeDragCandidate = FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext as TestFlowNode;
    }

    private void TestFlowTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _testNodeDragCandidate = null;

    private void TestFlowTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _testNodeDragCandidate is not TestFlowNode node) return;
        var current = e.GetPosition(TestFlowTree);
        if (Math.Abs(current.X - _testNodeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _testNodeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _testNodeDragCandidate = null;
        DragDrop.DoDragDrop(TestFlowTree, node, DragDropEffects.Move);
    }

    private void TestFlowTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _selectedTestCase is not null && e.Data.GetDataPresent(typeof(TestFlowNode))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TestFlowTree_Drop(object sender, DragEventArgs e)
    {
        if (_selectedTestCase is null || e.Data.GetData(typeof(TestFlowNode)) is not TestFlowNode source) return;
        var targetItem = FindTreeViewItem(e.OriginalSource as DependencyObject);
        var target = targetItem?.DataContext as TestFlowNode;
        var sourceOwner = FindTestFlowOwner(_selectedTestCase.Flow, source.Id);
        if (sourceOwner is null || ReferenceEquals(source, target) || (target is not null && ContainsTestFlowNode(source, target.Id))) return;

        if (target is null)
        {
            if (ReferenceEquals(sourceOwner, _selectedTestCase.Flow)) return;
            sourceOwner.Remove(source);
            _selectedTestCase.Flow.Add(source);
        }
        else
        {
            var targetOwner = FindTestFlowOwner(_selectedTestCase.Flow, target.Id);
            if (targetOwner is null) return;
            var y = targetItem is null ? 0 : e.GetPosition(targetItem).Y;
            var height = targetItem?.ActualHeight ?? 0;
            if (target.Kind == TestFlowNodeKind.组合 && y > height * .25 && y < height * .75)
            {
                sourceOwner.Remove(source);
                target.Children.Add(source);
            }
            else
            {
                var index = targetOwner.IndexOf(target) + (y > height / 2 ? 1 : 0);
                var oldIndex = sourceOwner.IndexOf(source);
                if (ReferenceEquals(sourceOwner, targetOwner) && oldIndex < index) index--;
                sourceOwner.Remove(source);
                targetOwner.Insert(Math.Clamp(index, 0, targetOwner.Count), source);
            }
        }

        _selectedTestNode = source;
        _selectedTestNodeId = source.Id;
        _pendingTestNodeSelection = source;
        RefreshTestFlowTreePreservingSelection(source.Id);
        e.Handled = true;
    }

    private static bool ContainsTestFlowNode(TestFlowNode root, string id) =>
        root.Children.Any(child => child.Id == id || ContainsTestFlowNode(child, id));

    private void TestFlowStepCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TestFlowNode node }) return;

        // 卡片内部的复选框/按钮会截断 TreeViewItem 的默认选择路由，
        // 因此单击卡片时显式同步选中项；双击仍只负责打开中间 Inspector。
        if (FindTreeViewItem(sender as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            _selectedTestNode = node;
            _selectedTestNodeId = node.Id;
            UpdateTestFlowMultiSelection(node, Keyboard.Modifiers);
        }

        // Inspector 已固定停靠，单击步骤即打开属性；双击保留为兼容路径。
        if (e.ClickCount >= 1)
        {
            // Selecting/editing a disabled step must never silently re-enable it.
            _selectedTestNode = node;
            Dispatcher.BeginInvoke(new Action(() => ShowTestFlowNodeDetails(node)), DispatcherPriority.ContextIdle);
            if (e.ClickCount >= 2) e.Handled = true;
        }
    }

    private void TestFlowNodeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TestFlowNode node } && _selectedTestCase is not null)
        {
            RemoveTestFlowNode(node);
            e.Handled = true;
        }
    }

    private void InsertTestFlowNode(TestFlowNode node)
    {
        if (_selectedTestCase is null) return;
        ObservableCollection<TestFlowNode> owner;
        var selectedIndex = -1;
        if (_selectedTestNode is { PathKind: not TestFlowPathKind.None } path)
            owner = path.Children;
        else if (_selectedTestNode is null)
            owner = _selectedTestCase.Flow;
        else
        {
            owner = FindTestFlowOwner(_selectedTestCase.Flow, _selectedTestNode.Id) ?? _selectedTestCase.Flow;
            selectedIndex = owner.IndexOf(_selectedTestNode);
        }
        var index = selectedIndex < 0 ? owner.Count : selectedIndex + 1;
        owner.Insert(Math.Clamp(index, 0, owner.Count), node);
        _selectedTestNode = node;
        _selectedTestNodeId = node.Id;
        _selectedTestNodeIds.Clear();
        _pendingTestNodeSelection = node;
        RefreshTestFlowSummary(node);
        RefreshTestFlowTreePreservingSelection(node.Id);
    }

    private void RemoveTestFlowNode(TestFlowNode node)
    {
        if (_selectedTestCase is null) return;
        if (node.PathKind is TestFlowPathKind.Body or TestFlowPathKind.Then or TestFlowPathKind.Hit or TestFlowPathKind.Timeout or TestFlowPathKind.Default)
        {
            AppendLog($"{node.Name} 是固定路径，不能删除。");
            return;
        }
        var owner = FindTestFlowOwner(_selectedTestCase.Flow, node.Id);
        var index = owner?.IndexOf(node) ?? -1;
        if (index < 0) return;
        _deletedStep = null; _deletedFromSequence = null; _deletedStepOwner = null; _deletedStepIndex = -1;
        _deletedTreeItem = null; _deletedTreeParent = null; _deletedTreeIndex = -1;
        _deletedFlowNode = node; _deletedFlowCase = _selectedTestCase; _deletedFlowOwner = owner; _deletedFlowIndex = index;
        owner!.RemoveAt(index);
        _selectedTestNodeIds.Remove(node.Id);
        CloseStepEditor();
        var next = owner.Count == 0
            ? null
            : owner[Math.Min(index, owner.Count - 1)];
        _selectedTestNode = next;
        _selectedTestNodeId = next?.Id;
        _pendingTestNodeSelection = next;
        RefreshTestFlowTreePreservingSelection(next?.Id);
        AppendLog($"已删除流程节点“{node.Name}”，可按 Ctrl+Z 撤销。");
        UndoDeleteButton.Visibility = Visibility.Visible;
    }

    private void NewTestCase_Click(object sender, RoutedEventArgs e)
    {
        var testCase = new TestCaseDefinition
        {
            Name = $"测试用例 {_configuration.TestPlan.Cases.Count + 1}",
            Order = _configuration.TestPlan.Cases.Count + 1
        };
        _configuration.TestPlan.Cases.Add(testCase);
        _selectedTestCase = testCase;
        RebuildTestCaseTree();
        RestoreTestCaseSelection(testCase);
        TestFlowTree.ItemsSource = testCase.Flow;
        TestCaseTitle.Text = testCase.Name;
        AppendLog($"已新建测试用例：{testCase.Name}");
    }

    private void MoveTestCaseUp_Click(object sender, RoutedEventArgs e) => MoveTestCase(-1);
    private void MoveTestCaseDown_Click(object sender, RoutedEventArgs e) => MoveTestCase(1);
    private void MoveTestCase(int direction)
    {
        if (_selectedTestCase is null) return;
        var ordered = _configuration.TestPlan.Cases.OrderBy(x => x.Order).ToList();
        var index = ordered.IndexOf(_selectedTestCase);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count) return;
        (ordered[index].Order, ordered[target].Order) = (ordered[target].Order, ordered[index].Order);
        _pendingTestCaseSelection = _selectedTestCase;
        RebuildTestCaseTree();
        RestoreTestCaseSelection(_selectedTestCase);
    }

    private void AddTestFlowNode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFlowEditorRow?.IsControl == true)
        {
            AppendLog("控制主行不能直接插入通用内容，请选择固定路径或普通步骤。");
            return;
        }
        if (_selectedTestCase is null)
        {
            AppendLog("请先选择测试用例。");
            return;
        }
        var menu = new ContextMenu();
        foreach (var operationKind in new[]
                 {
                     ConfigurationStepKind.广播报文,
                     ConfigurationStepKind.报文解析,
                     ConfigurationStepKind.电源控制,
                     ConfigurationStepKind.动作控制,
                     ConfigurationStepKind.等待
                 })
        {
            var choices = ResourcesForKind(operationKind).ToList();
            if (operationKind == ConfigurationStepKind.等待)
            {
                var waitItem = new MenuItem { Header = "等待" };
                waitItem.Click += (_, _) => AddTestAtomicNode(ConfigurationStepKind.等待);
                menu.Items.Add(waitItem);
                continue;
            }
            var operationItem = new MenuItem
            {
                Header = DisplayStepKind(operationKind),
                IsEnabled = true
            };
            // 测试用例可以直接配置原子步骤；资源只是可选的复用入口，不能成为测试用例的前置条件。
            operationItem.Items.Add(MenuItem($"直接配置{DisplayStepKind(operationKind)}", (_, _) => AddTestAtomicNode(operationKind)));
            operationItem.Items.Add(new Separator());
             if (choices.Count == 0)
                 operationItem.Items.Add(MenuItem($"新建{DisplayStepKind(operationKind)}并加入当前用例", (_, _) => CreateAndAddResource(operationKind)));
            foreach (var choice in choices)
            {
                var resourceItem = new MenuItem { Header = choice.Name, ToolTip = choice.Summary };
                resourceItem.Click += (_, _) => AddTestResourceNode(operationKind, choice);
                operationItem.Items.Add(resourceItem);
            }
            if (choices.Count > 0)
            {
                operationItem.Items.Add(new Separator());
                 operationItem.Items.Add(MenuItem($"新建{DisplayStepKind(operationKind)}并加入当前用例", (_, _) => CreateAndAddResource(operationKind)));
            }
            if (choices.Count > 0) ConstrainMenuDropDown(operationItem);
            menu.Items.Add(operationItem);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("选择已有资源", (_, _) => AddTestFlowNode(TestFlowNodeKind.引用序列)));
        menu.Items.Add(MenuItem("组合当前选中连续步骤", (_, _) => GroupSelectedTestFlowNodes()));
        menu.IsOpen = true;
    }

    private void CreateAndAddResource(ConfigurationStepKind kind)
    {
        if (_selectedTestCase is null) return;
        var parent = ChooseResourceFolder();
        if (parent is null) return;
        var item = parent.AddAtomicOperation(kind);
        RebuildTree();
        var choice = new ResourceChoice(item.Id, item.Sequence!.Id, item.Name, "资源", ReferenceSummaryResolver.Resolve(item.Sequence), item.Sequence);
        AddTestResourceNode(kind, choice);
        // AddTestResourceNode 已经插入节点并打开属性编辑器。
        // 这里不能再次通过 _selectedTestNode 排队打开：重建树期间选择状态可能被清空，
        // 第二次回调会把空节点传入详情编辑器并触发界面线程异常。
    }

    private ConfigurationTreeItem? ChooseResourceFolder()
    {
        var dialog = new Window
        {
            Title = "选择资源保存位置",
            Owner = this,
            Width = 640,
            Height = 520,
            MinWidth = 520,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false,
            Background = Brushes.White
        };
        var folders = new ListBox
        {
            Margin = new Thickness(12, 4, 12, 8),
            MinHeight = 260,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var folderItemStyle = new Style(typeof(ListBoxItem));
        folderItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        folderItemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28.0));
        folderItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        folders.ItemContainerStyle = folderItemStyle;
        var hint = new TextBlock
        {
            Text = "资源会保存到所选文件夹。文件夹名称和层级只用于管理，不影响资源执行。",
            Margin = new Thickness(12, 10, 12, 4),
            Foreground = Brushes.SlateGray,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 590
        };
        var newFolder = new Button { Content = "新建文件夹", Width = 92, Height = 30, Margin = new Thickness(0, 0, 8, 12) };
        var ok = new Button { Content = "保存到此处", Width = 96, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 12) };
        var cancel = new Button { Content = "取消", Width = 72, Height = 30, IsCancel = true, Margin = new Thickness(0, 0, 12, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(newFolder); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons); root.Children.Add(hint); root.Children.Add(folders);
        dialog.Content = root;

        var choices = new List<FolderChoice>();
        // 模板只初始化一次。反复在已绑定的 ListBox 上切换 ItemTemplate /
        // DisplayMemberPath 会触发 WPF 的互斥属性异常，进而让“保存到资源”失败。
        folders.ClearValue(ItemsControl.DisplayMemberPathProperty);
        var folderTemplate = new DataTemplate();
        var pathText = new FrameworkElementFactory(typeof(TextBlock));
        pathText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(FolderChoice.Path)));
        pathText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        pathText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        pathText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        pathText.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(FolderChoice.Path)));
        folderTemplate.VisualTree = pathText;
        folders.ItemTemplate = folderTemplate;
        void RefreshFolders(string? selectId = null)
        {
            choices = EnumerateFolders(_configuration.Root).ToList();
            folders.ItemsSource = null;
            folders.ItemsSource = choices;
            var selected = choices.FirstOrDefault(x => x.Item.Id == selectId) ?? choices.FirstOrDefault();
            folders.SelectedItem = selected;
        }
        newFolder.Click += (_, _) =>
        {
            var parent = (folders.SelectedItem as FolderChoice)?.Item ?? _configuration.Root;
            var name = PromptForText("新建文件夹", "文件夹名称", "新文件夹");
            if (string.IsNullOrWhiteSpace(name)) return;
            var child = parent.AddFolder(name.Trim());
            RefreshFolders(child.Id);
            RebuildTree();
        };
        ok.Click += (_, _) =>
        {
            if (folders.SelectedItem is FolderChoice selected)
            {
                dialog.Tag = selected.Item;
                dialog.DialogResult = true;
            }
        };
        RefreshFolders();
        return dialog.ShowDialog() == true ? dialog.Tag as ConfigurationTreeItem : null;
    }

    private static IEnumerable<FolderChoice> EnumerateFolders(ConfigurationTreeItem root)
    {
        IEnumerable<FolderChoice> Walk(ConfigurationTreeItem item, string path)
        {
            if (item.Kind == ConfigurationItemKind.文件夹)
            {
                yield return new FolderChoice(item, path);
                foreach (var child in item.Children)
                    foreach (var folder in Walk(child, $"{path} / {child.Name}")) yield return folder;
            }
        }
        return Walk(root, root.Name);
    }

    private string? PromptForText(string title, string label, string initial)
    {
        var dialog = new Window
        {
            Title = title, Owner = this, Width = 360, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brushes.White
        };
        var box = new TextBox { Text = initial, Margin = new Thickness(12, 4, 12, 8), Height = 28 };
        var ok = new Button { Content = "确定", Width = 72, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 10) };
        var cancel = new Button { Content = "取消", Width = 72, Height = 28, IsCancel = true, Margin = new Thickness(0, 0, 12, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new StackPanel { Children = { new TextBlock { Text = label, Margin = new Thickness(12, 10, 12, 0) }, box } });
        dialog.Content = panel;
        ok.Click += (_, _) => { dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private IEnumerable<ResourceChoice> ResourcesForKind(ConfigurationStepKind kind) =>
        AllResources().Where(x => x.Sequence.Steps.Count == 1 && x.Sequence.Steps[0].Kind == kind);

    private void OpenResourcePicker(ConfigurationStepKind kind)
    {
        var all = ResourcesForKind(kind).ToList();
        if (all.Count == 0)
        {
            AppendLog($"没有可用的{DisplayStepKind(kind)}资源，请先在资源配置中创建。");
            return;
        }

        var dialog = new Window
        {
            Title = $"选择{DisplayStepKind(kind)}",
            Owner = this,
            Width = 460,
            Height = 500,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false,
            Background = Brushes.White
        };
        var search = new TextBox { Margin = new Thickness(12, 12, 12, 8), Height = 30, ToolTip = "按名称或摘要筛选" };
        var list = new ListBox { Margin = new Thickness(12, 0, 12, 8), DisplayMemberPath = nameof(ResourceChoice.Name), MinHeight = 180 };
        var hint = new TextBlock { Margin = new Thickness(12, 0, 12, 8), Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        var choose = new Button { Content = "选择", Width = 76, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 12) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 0, 12, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(choose); buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom); DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(buttons); root.Children.Add(hint); root.Children.Add(search); root.Children.Add(list);
        dialog.Content = root;

        void RefreshList()
        {
            var query = search.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? all
                : all.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = filtered;
            hint.Text = $"共 {filtered.Count} 个资源；双击或点击“选择”加入当前测试用例。";
            if (filtered.Count > 0) list.SelectedIndex = 0;
        }
        void Commit()
        {
            if (list.SelectedItem is ResourceChoice selected)
            {
                dialog.DialogResult = true;
                AddTestResourceNode(kind, selected);
            }
        }
        search.TextChanged += (_, _) => RefreshList();
        choose.Click += (_, _) => Commit();
        list.MouseDoubleClick += (_, _) => Commit();
        RefreshList();
        dialog.ShowDialog();
    }

    private void AddTestResourceNode(ConfigurationStepKind kind, ResourceChoice choice)
    {
        if (_selectedTestCase is null) return;

        // 自动测试步骤是当前用例的本地编辑对象；资源仅作为创建时的模板，
        // 后续属性编辑、组合和单步运行不再回到资源序列。
        var source = choice.Sequence.Steps.FirstOrDefault();
        var operation = source is null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<ConfigurationStep>(
                System.Text.Json.JsonSerializer.Serialize(source));
        if (operation is not null) operation.RegenerateIdsRecursively();
        var node = new TestFlowNode
        {
            Kind = TestFlowNodeKind.原子操作,
            Name = choice.Name,
            Operation = operation,
            ParseAssertion = kind == ConfigurationStepKind.报文解析
                ? new ParseAssertion()
                : null
        };

        InsertTestFlowNode(node);
        ShowTestFlowNodeDetails(node);
        AppendLog($"已添加测试步骤：{choice.Name}");
    }

    private void AddTestFlowNode(TestFlowNodeKind kind)
    {
        if (_selectedTestCase is null) return;
        var node = new TestFlowNode { Kind = kind, Name = kind switch { TestFlowNodeKind.引用序列 => "选择资源", TestFlowNodeKind.原子操作 => "测试步骤", _ => "测试组合" } };
        if (kind == TestFlowNodeKind.引用序列)
        {
            var resource = AllResources().FirstOrDefault();
            if (resource is not null)
            {
                node.ResourceId = resource.Id;
                node.SequenceId = resource.SequenceId;
                node.Name = resource.Name;
            }
        }
        InsertTestFlowNode(node);
        AppendLog($"已添加测试流程节点：{node.Name}");
    }

    private void AddTestAtomicNode(ConfigurationStepKind operationKind)
    {
        if (_selectedTestCase is null) return;
        var operation = new ConfigurationStep { Kind = operationKind, Name = operationKind.ToString() };
        var node = new TestFlowNode
        {
            Kind = TestFlowNodeKind.原子操作,
            Name = operation.Name,
            Operation = operation,
            ParseAssertion = operationKind == ConfigurationStepKind.报文解析 ? new ParseAssertion() : null
        };
        InsertTestFlowNode(node);
        ShowTestFlowNodeDetails(node);
        AppendLog($"已添加测试步骤：{operation.Name}");
    }

    private void TestCaseTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var target = FindTreeViewItem(Mouse.DirectlyOver as DependencyObject);
        if (target?.DataContext is not TestCaseDefinition testCase)
        {
            TestCaseTree.ContextMenu = new ContextMenu { Items = { MenuItem("新建测试用例", (_, _) => NewTestCase_Click(this, new RoutedEventArgs())) } };
            return;
        }
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("新建测试用例", (_, _) => NewTestCase_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem(testCase.Enabled ? "停用测试用例" : "启用测试用例", (_, _) => { testCase.Enabled = !testCase.Enabled; RebuildTestCaseTree(); }));
        menu.Items.Add(MenuItem("运行当前用例", async (_, _) => await RunTestCaseAsync(testCase)));
        menu.Items.Add(MenuItem("重命名（F2）", (_, _) => RenameTestCase(testCase)));
        menu.Items.Add(MenuItem("设置用例间等待", (_, _) => ConfigureTestCaseWait(testCase)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("删除测试用例", (_, _) => RemoveTestCase(testCase)));
        TestCaseTree.ContextMenu = menu;
    }

    private void TestCaseTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is TestCaseDefinition testCase)
        {
            item.IsSelected = true;
            _selectedTestCase = testCase;
        }
        else
        {
            _selectedTestCase = null;
        }
    }

    private void TestFlowTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var target = FindTreeViewItem(Mouse.DirectlyOver as DependencyObject);
        var node = _contextTestNode ?? target?.DataContext as TestFlowNode;
        _contextTestNode = null;
        if (node is null || _selectedTestCase is null)
        {
            e.Handled = true;
            return;
        }
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("属性", (_, _) => ShowTestFlowNodeDetails(node)));
        menu.Items.Add(MenuItem("组合当前选中连续步骤", (_, _) => GroupSelectedTestFlowNodes()));
        menu.Items.Add(MenuItem("解组当前组合", (_, _) => UngroupSelectedTestFlowNode(node)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("运行当前步骤/组合", async (_, _) => await RunSelectedTestFlowNodeAsync(node)));
        menu.Items.Add(MenuItem("运行当前用例", async (_, _) => await RunTestCaseAsync(_selectedTestCase)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("重命名（F2）", (_, _) => RenameTestNode(node)));
        menu.Items.Add(MenuItem("删除节点（Delete）", (_, _) => RemoveTestFlowNode(node)));
        menu.Items.Add(MenuItem("复制节点", (_, _) =>
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<TestFlowNode>(System.Text.Json.JsonSerializer.Serialize(node));
            if (copy is null) return;
            copy.Id = Guid.NewGuid().ToString("N");
            var owner = FindTestFlowOwner(_selectedTestCase.Flow, node.Id) ?? _selectedTestCase.Flow;
            owner.Insert(Math.Clamp(owner.IndexOf(node) + 1, 0, owner.Count), copy);
            _selectedTestNode = copy;
            RefreshTestFlowTreePreservingSelection(copy.Id);
        }));
        TestFlowTree.ContextMenu = menu;
    }

    private void ShowTestFlowNodeDetails(TestFlowNode node)
    {
        if (node is null) return;
        _selectedTestNode = node;
        _selectedTestNodeId = node.Id;
        _propertyTestNodeId = node.Id;
        if (node.FlowControl is not null)
        {
            var row = _flowEditorRows.FirstOrDefault(x => x.SourceNode?.Id == node.Id)
                ?? FlowEditorRow.FromNode(node);
            ShowFlowControlDetails(row);
            return;
        }
        if (node.PathKind == TestFlowPathKind.Case)
        {
            ShowCasePathDetails(node);
            return;
        }
        if (node.PathKind != TestFlowPathKind.None)
        {
            CloseStepEditor();
            return;
        }
        if (node.Kind == TestFlowNodeKind.组合)
        {
            CloseStepEditor();
            return;
        }
        // 资源引用保持 ResourceId/SequenceId。双击只打开资源编辑器，不得隐式复制、重生成
        // ID 或清空引用；如需脱离资源，必须由用户点击“复制为本步骤”。
        if (node.Operation is { } operation)
        {
            if (operation.Kind == ConfigurationStepKind.报文解析)
            {
                ShowTestParseEditor(node, operation);
                return;
            }
            ShowEditor(operation, onNameChanged: name =>
            {
                node.Name = name;
                RefreshTestFlowSummary(node);
                _selectedTestNodeId = node.Id;
            });
            return;
        }

        var relation = new ResourceRelationService().Resolve(_configuration, node);
        var target = relation.Sequence;
        var choices = AllResources().ToList();
        var selector = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(ResourceChoice.DisplayName),
            SelectedValuePath = nameof(ResourceChoice.Id),
            SelectedValue = !string.IsNullOrWhiteSpace(node.ResourceId)
                ? choices.FirstOrDefault(x => x.Id == node.ResourceId)?.Id
                : choices.FirstOrDefault(x => x.SequenceId == node.SequenceId)?.Id,
            IsTextSearchEnabled = false,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        ConstrainComboDropDown(selector);
        void CommitSelection()
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= choices.Count) return;
            var choice = choices[selector.SelectedIndex];
            node.ResourceId = choice.Id;
            node.SequenceId = choice.SequenceId;
            node.Name = choice.Name;
            RefreshTestFlowSummary(node);
            Dispatcher.BeginInvoke(new Action(() => ShowTestFlowNodeDetails(node)), DispatcherPriority.ContextIdle);
        }
        selector.SelectionChanged += (_, _) => CommitSelection();
        ConstrainComboDropDown(selector);
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "编辑对象", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brushes.Gray });
        panel.Children.Add(new TextBlock { Text = "选择后立即切换，下面直接显示对象内容。", FontSize = 11, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 2, 0, 6) });
        panel.Children.Add(selector);
        if (target is not null)
        {
            var sourceInfo = new TextBlock
            {
                Text = $"来源：共享资源 · {target.Name}",
                FontSize = 11,
                Foreground = ThemeBrush("AccentBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = $"共享资源引用\nResourceId={node.ResourceId}\nSequenceId={node.SequenceId}"
            };
            panel.Children.Add(sourceInfo);
            var detach = new Button
            {
                Content = "复制为本步骤（脱离资源）",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "复制当前资源内容到测试用例，保留资源本身不变；之后编辑不会再跟随资源。"
            };
            detach.Click += (_, _) => CopyReferencedNodeAsLocal(node, target);
            panel.Children.Add(detach);
        }
        if (target is null && relation.State == ResourceRelationState.引用失效)
            panel.Children.Add(new TextBlock
            {
                Text = "资源不可用 · 引用已失效。请重新选择资源；不会自动改为独立步骤。",
                Foreground = Brushes.IndianRed,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 3),
                ToolTip = relation.Diagnostic?.Message
            });
        else if (target is null && relation.State == ResourceRelationState.空引用)
            panel.Children.Add(new TextBlock
            {
                Text = "尚未选择资源。请使用下方选择器插入一个共享资源。",
                Foreground = ThemeBrush("AmberBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 3)
            });
        if (target is not null)
        {
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 10, 0, 2),
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(239, 247, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(185, 215, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = $"{target.Name} · {AllResources().FirstOrDefault(x => x.SequenceId == target.Id)?.KindLabel ?? "资源"}", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(28, 76, 118)) },
                        new TextBlock { Text = ReferenceSummaryResolver.Resolve(target), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(74, 96, 120)) }
                    }
                }
            });
            panel.Children.Add(new TextBlock { Text = $"对象内容（{target.Steps.Count} 个步骤）", Margin = new Thickness(0, 12, 0, 2), FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray });
            if (target.Steps.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "资源还没有步骤。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 0) });
            }
            else
            {
                foreach (var step in target.Steps)
                {
                    var editor = BuildEditorPanel(step, name =>
                    {
                        SyncReferencedResourceName(target, step);
                        RefreshTestFlowSummary(node);
                        InspectorTitle.Text = target.Name;
                    }, false);
                    if (target.Steps.Count > 1)
                    {
                        panel.Children.Add(new Expander
                        {
                            Header = new StackPanel
                            {
                                Children =
                                {
                                    new TextBlock { Text = step.Name, FontWeight = FontWeights.SemiBold },
                                    new TextBlock { Text = $"{step.DisplayKind} · {step.Summary}", FontSize = 11, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis }
                                }
                            },
                            IsExpanded = false,
                            Margin = new Thickness(0, 8, 0, 0),
                            Content = editor
                        });
                    }
                    else
                    {
                        panel.Children.Add(editor);
                    }
                }
            }
        }
        InspectorTitle.Text = target?.Name ?? node.Name;
        BackToReferenceButton.Visibility = Visibility.Collapsed;
        StepEditor.Content = new Border
        {
            Padding = new Thickness(8),
            Child = panel
        };
        OpenInspector();
    }

    private void CopyReferencedNodeAsLocal(TestFlowNode node, ConfigurationSequence target)
    {
        var relation = new ResourceRelationService().CopyAsLocal(_configuration, node);
        if (relation.Diagnostic is not null)
        {
            AppendLog($"复制为本步骤失败：{relation.Diagnostic.Message}");
            return;
        }
        RefreshTestFlowSummary(node);
        AppendLog($"测试步骤 [{node.Name}] 已复制为本步骤，已脱离资源引用 [{target.Id}]。后续资源修改不会影响此步骤。");
        ShowTestFlowNodeDetails(node);
    }

    private void ShowTestParseEditor(TestFlowNode node, ConfigurationStep operation)
    {
        RefreshTestFlowSummary(node);
        var assertion = node.ParseAssertion ??= new ParseAssertion();
        // 测试步骤直接使用通信数据库中的信号定义；资源只是可选的复用载体，
        // 不再作为测试步骤编辑器的主对象。
        assertion.ParseResourceId = "";
        var expression = ExpectationExpression.ParseForEditor(assertion.Expectation);
        var operatorBox = new ComboBox { ItemsSource = ExpectationExpression.OperatorOptions, SelectedItem = expression.Operator, IsTextSearchEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        ConstrainComboDropDown(operatorBox);
        var firstBox = new TextBox { Text = expression.First, HorizontalAlignment = HorizontalAlignment.Stretch };
        var secondBox = new TextBox { Text = expression.Second, HorizontalAlignment = HorizontalAlignment.Stretch };
        var secondRow = LabeledControl("最大值", secondBox);
        var timeoutBox = new TextBox { Text = assertion.TimeoutMs.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch };
        void Commit()
        {
            assertion.Expectation = ExpectationExpression.Build(operatorBox.SelectedItem?.ToString(), firstBox.Text, secondBox.Text);
            assertion.TimeoutMs = Math.Max(0, Number(timeoutBox.Text, 5000));
            secondRow.Visibility = operatorBox.SelectedItem?.ToString() == ExpectationExpression.Range ? Visibility.Visible : Visibility.Collapsed;
            RefreshTestFlowSummary(node);
        }
        operatorBox.SelectionChanged += (_, _) => Commit();
        firstBox.TextChanged += (_, _) => Commit();
        secondBox.TextChanged += (_, _) => Commit();
        timeoutBox.TextChanged += (_, _) => Commit();
        var panel = new StackPanel { Margin = new Thickness(4) };
        var resourceActions = new Button
        {
            Content = "资源操作",
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "选择另存为资源或保存并绑定，不改变当前步骤来源"
        };
        var resourceMenu = new ContextMenu();
        resourceMenu.Items.Add(MenuItem("另存为资源（保持独立）", (_, _) => SaveStepAsResource(operation, false)));
        resourceMenu.Items.Add(MenuItem("保存并绑定资源", (_, _) => SaveStepAsResource(operation, true)));
        resourceActions.ContextMenu = resourceMenu;
        resourceActions.Click += (_, _) => resourceActions.ContextMenu.IsOpen = true;
        var parseHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        parseHeader.Children.Add(new TextBlock { Text = "报文解析步骤", FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(resourceActions, Dock.Right);
        parseHeader.Children.Add(resourceActions);
        panel.Children.Add(parseHeader);
        panel.Children.Add(BuildEditorPanel(operation, _ =>
        {
            // 属性编辑器、节点卡片和日志定位共用同一个名称。
            node.Name = operation.Name;
            node.NameIsUserDefined = true;
            SyncAtomicTreeName(operation);
            InspectorTitle.Text = node.Name;
            RefreshTestFlowSummary(node);
        }, false, false, false));
        var judgementSection = InspectorSection("判定条件");
        judgementSection.Children.Add(LabeledControl("比较方式", operatorBox));
        judgementSection.Children.Add(LabeledControl("期望值", firstBox));
        judgementSection.Children.Add(secondRow);
        judgementSection.Children.Add(LabeledControl("超时 ms", timeoutBox));
        panel.Children.Add(judgementSection);
        InspectorTitle.Text = node.Name;
        StepEditor.Content = panel;
        OpenInspector();
        // 打开属性只初始化编辑器状态，不刷新整个测试树；整树刷新会让 TreeView
        // 短暂把选中项跳回第一步，造成双击时“闪回”的错觉。
        secondRow.Visibility = expression.Operator == ExpectationExpression.Range ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenInspector()
    {
        // 检查器属于工作区同一视觉树；首次才给默认宽度，随后尊重用户通过
        // GridSplitter 调整的高度，不在每次打开属性时把布局重置。
        if (RightInspectorRow.Height.Value <= 0)
        {
            var targetHeight = Math.Clamp(QuickRightToolsGrid.ActualHeight * 0.45, 250, 460);
            RightInspectorRow.Height = new GridLength(targetHeight, GridUnitType.Pixel);
        }
        RightInspectorSplitterRow.Height = new GridLength(6, GridUnitType.Pixel);
        InspectorHost.Visibility = Visibility.Visible;
        InspectorGridSplitter.Visibility = Visibility.Visible;
    }

    private void HideInspector()
    {
        InspectorHost.Visibility = Visibility.Collapsed;
        InspectorGridSplitter.Visibility = Visibility.Collapsed;
        RightInspectorSplitterRow.Height = new GridLength(0);
        RightInspectorRow.Height = new GridLength(0);
    }

    private void TestFlowTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is TestFlowNode node)
        {
            item.IsSelected = true;
            _selectedTestNode = node;
            _selectedTestNodeId = node.Id;
            _selectedTestNodeIds.Clear();
            _selectedTestNodeIds.Add(node.Id);
            _contextTestNode = node;
        }
        else
            _contextTestNode = null;
    }

    private void MoveTestNodeUp_Click(object sender, RoutedEventArgs e) => MoveTestNode(-1);
    private void MoveTestNodeDown_Click(object sender, RoutedEventArgs e) => MoveTestNode(1);
    private void MoveTestNode(int direction)
    {
        if (_selectedTestCase is null || _selectedTestNode is null) return;
        var owner = FindTestFlowOwner(_selectedTestCase.Flow, _selectedTestNode.Id);
        var index = owner?.IndexOf(_selectedTestNode) ?? -1;
        var target = index + direction;
        if (owner is null || index < 0 || target < 0 || target >= owner.Count) return;
        var node = _selectedTestNode;
        var scrollOffset = FindVisualChild<ScrollViewer>(TestFlowTree)?.VerticalOffset;
        owner.Move(index, target);
        _selectedTestNode = node;
        _selectedTestNodeId = node.Id;
        _pendingTestNodeSelection = _selectedTestNode;
        RefreshTestFlowTreePreservingSelection(node.Id, scrollOffset);
    }

    private void RestoreTestFlowSelection(TestFlowNode node, double? scrollOffset = null, int? generation = null)
    {
        _selectedTestNodeId = node.Id;
        var restoreGeneration = generation ?? ++_selectionRestoreGeneration;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (restoreGeneration != _selectionRestoreGeneration) return;
            TestFlowTree.UpdateLayout();
            var wasRestoring = _restoringWorkspaceSelection;
            _restoringWorkspaceSelection = true;
            try
            {
                if (SelectTreeItemWithoutFocus(TestFlowTree, node))
                    _selectedTestNode = node;
            }
            finally { _restoringWorkspaceSelection = wasRestoring; }
            if (scrollOffset is double value && FindVisualChild<ScrollViewer>(TestFlowTree) is { } scroll)
                scroll.ScrollToVerticalOffset(value);
        }), DispatcherPriority.ApplicationIdle);
    }

    private void RefreshTestFlowTreePreservingSelection(string? nodeId = null, double? scrollOffset = null)
    {
        var keepId = nodeId ?? _selectedTestNodeId ?? _selectedTestNode?.Id;
        var snapshot = CaptureWorkspaceSelection();
        _restoringWorkspaceSelection = true;
        try { TestFlowTree.Items.Refresh(); }
        finally { _restoringWorkspaceSelection = false; }
        RebuildFlowEditor();
        if (keepId is null || _selectedTestCase is null) return;
        var keep = FindTestFlowNode(_selectedTestCase.Flow, keepId);
        if (keep is not null)
        {
            _selectedTestNode = keep;
            RestoreTestFlowSelection(keep, scrollOffset);
        }
        else
            RestoreWorkspaceSelection(snapshot);
    }

    private static TestFlowNode? FindTestFlowNode(IEnumerable<TestFlowNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) return node;
            var found = FindTestFlowNode(node.Children, id);
            if (found is not null) return found;
        }
        return null;
    }

    private void UpdateTestFlowMultiSelection(TestFlowNode node, ModifierKeys modifiers)
    {
        var owner = _selectedTestCase is null ? null : FindTestFlowOwner(_selectedTestCase.Flow, node.Id);
        if (owner is null) return;
        if (modifiers.HasFlag(ModifierKeys.Shift) && _selectedTestNodeIds.Count > 0)
        {
            var anchorId = _selectedTestNodeIds.First();
            var anchor = owner.FirstOrDefault(x => x.Id == anchorId);
            var start = anchor is null ? -1 : owner.IndexOf(anchor);
            var end = owner.IndexOf(node);
            if (start >= 0 && end >= 0)
            {
                _selectedTestNodeIds.Clear();
                for (var i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                    _selectedTestNodeIds.Add(owner[i].Id);
                return;
            }
        }
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selectedTestNodeIds.Add(node.Id)) _selectedTestNodeIds.Remove(node.Id);
        }
        else
        {
            _selectedTestNodeIds.Clear();
            _selectedTestNodeIds.Add(node.Id);
        }
    }

    private void GroupSelectedTestFlowNodes()
    {
        if (_selectedTestCase is null) return;
        var selected = _selectedTestNodeIds.ToHashSet();
        if (_selectedTestNode is not null && selected.Count == 0) selected.Add(_selectedTestNode.Id);
        if (selected.Count < 2) { AppendLog("请用 Ctrl/Shift 选择至少两个同级步骤再组合。"); return; }
        var owner = FindTestFlowOwner(_selectedTestCase.Flow, selected.First());
        if (owner is null || owner.Any(x => selected.Contains(x.Id)) == false)
        {
            AppendLog("只能组合当前测试用例中的同级步骤。"); return;
        }
        var chosen = owner.Where(x => selected.Contains(x.Id)).ToList();
        if (chosen.Count != selected.Count)
        {
            AppendLog("只能组合当前测试用例中的同级步骤。"); return;
        }
        var indexes = chosen.Select(owner.IndexOf).OrderBy(x => x).ToList();
        if (indexes.Zip(indexes.Skip(1), (a, b) => b == a + 1).Any(x => !x))
        {
            AppendLog("只能组合连续的同级步骤，避免改变原执行顺序。"); return;
        }
        var group = new TestFlowNode { Kind = TestFlowNodeKind.组合, Name = "测试组合" };
        foreach (var child in chosen)
        {
            owner.Remove(child);
            group.Children.Add(child);
        }
        owner.Insert(indexes[0], group);
        _selectedTestNode = group;
        _selectedTestNodeId = group.Id;
        _selectedTestNodeIds.Clear();
        _selectedTestNodeIds.Add(group.Id);
        RefreshTestFlowSummary(group);
        RefreshTestFlowTreePreservingSelection(group.Id);
        RestoreTestFlowSelection(group);
        AppendLog($"已将 {chosen.Count} 个当前用例步骤组合为“{group.Name}”，子树完整保留。");
    }

    private void UngroupSelectedTestFlowNode(TestFlowNode node)
    {
        if (_selectedTestCase is null || node.Kind != TestFlowNodeKind.组合 || node.FlowControl is not null) return;
        var owner = FindTestFlowOwner(_selectedTestCase.Flow, node.Id);
        var index = owner?.IndexOf(node) ?? -1;
        if (owner is null || index < 0) return;
        owner.RemoveAt(index);
        var children = node.Children.ToList();
        node.Children.Clear();
        for (var i = 0; i < children.Count; i++) owner.Insert(index + i, children[i]);
        _selectedTestNode = children.FirstOrDefault();
        _selectedTestNodeId = _selectedTestNode?.Id;
        _selectedTestNodeIds.Clear();
        if (_selectedTestNode is not null) _selectedTestNodeIds.Add(_selectedTestNode.Id);
        RefreshTestFlowTreePreservingSelection(_selectedTestNode?.Id);
        AppendLog("已解组当前测试用例组合，原子树和顺序已恢复。");
    }

    private async Task RunSelectedTestFlowNodeAsync(TestFlowNode node)
    {
        if (_selectedTestCase is null) return;
        if (!EnsureConnectedForRun(node.Kind == TestFlowNodeKind.组合 ? "当前组合" : "当前步骤")) return;
        if (!BeginRun()) return;
        BeginWorkspaceRunSelection();
        try
        {
            var isolatedCase = new TestCaseDefinition
            {
                Id = _selectedTestCase.Id,
                Name = _selectedTestCase.Name,
                Enabled = true,
                JudgementLogic = _selectedTestCase.JudgementLogic,
                Flow = { node }
            };
            var plan = new TestPlan { Id = _configuration.TestPlan.Id, Name = _configuration.TestPlan.Name, Cases = { isolatedCase } };
            var runner = CreateAutomaticTestRunner();
            var result = await runner.RunAsync(plan, _runCts?.Token ?? CancellationToken.None, new Progress<TestNodeResult>(x => AppendTestNodeResult(x)));
            if (!string.IsNullOrWhiteSpace(result.Error)) AppendLog($"运行预检失败：{result.Error}");
            var caseResult = result.Cases.FirstOrDefault();
            if (caseResult is not null) ApplyNodeResults(_selectedTestCase, caseResult.Nodes);
            RefreshRunVisuals();
            SetTestRunStatus($"{node.Name}：{caseResult?.Status ?? result.Status}", caseResult?.Status == TestResultStatus.通过 ? "GreenBrush" : "RedBrush");
        }
        catch (OperationCanceledException) { AppendLog("当前步骤/组合执行已取消。"); }
        catch (Exception ex) { AppendLog($"当前步骤/组合执行错误：{ex.GetType().Name}：{ex.Message}"); }
        finally { EndWorkspaceRunSelection(); EndRun(); }
    }

    private static bool SelectTreeItem(ItemsControl owner, object target)
    {
        foreach (var value in owner.Items)
        {
            if (ReferenceEquals(value, target) && owner.ItemContainerGenerator.ContainerFromItem(value) is TreeViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
                return true;
            }

            if (owner.ItemContainerGenerator.ContainerFromItem(value) is TreeViewItem child && SelectTreeItem(child, target))
                return true;
        }
        return false;
    }

    private AutomaticTestRunner CreateAutomaticTestRunner() =>
        new(
            _configuration,
            new UsbTestExecutionPort(
                _usb,
                (parse, time) =>
                {
                    var found = TryGetObservation(parse, time, out var sample);
                    return (found, sample);
                },
                AppendLog),
            waveformSource: new UsbWaveformAssertionSource(_waveformFrames));

    private static bool SelectTreeItemWithoutFocus(ItemsControl owner, object target)
    {
        foreach (var value in owner.Items)
        {
            if (ReferenceEquals(value, target) && owner.ItemContainerGenerator.ContainerFromItem(value) is TreeViewItem item)
            {
                item.IsSelected = true;
                return true;
            }

            if (owner.ItemContainerGenerator.ContainerFromItem(value) is TreeViewItem child && SelectTreeItemWithoutFocus(child, target))
                return true;
        }
        return false;
    }

    private void RenameTestCase(TestCaseDefinition testCase)
    {
        var dialog = new RenameDialog(testCase.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        testCase.Name = dialog.Value;
        RebuildTestCaseTree();
        if (ReferenceEquals(_selectedTestCase, testCase)) TestCaseTitle.Text = testCase.Name;
    }

    private void TestCaseDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TestCaseDefinition testCase }) return;
        RemoveTestCase(testCase);
        AppendLog($"已删除测试用例：{testCase.Name}");
        e.Handled = true;
    }

    private void RemoveTestCase(TestCaseDefinition testCase)
    {
        var ordered = _configuration.TestPlan.Cases.OrderBy(x => x.Order).ToList();
        var index = ordered.IndexOf(testCase);
        if (index < 0) return;
        _configuration.TestPlan.Cases.Remove(testCase);
        var remaining = _configuration.TestPlan.Cases.OrderBy(x => x.Order).ToList();
        var next = remaining.ElementAtOrDefault(Math.Min(index, Math.Max(0, remaining.Count - 1)));
        _selectedTestCase = next;
        _selectedTestNode = null;
        CloseStepEditor();
        RebuildTestCaseTree();
        TestFlowTree.ItemsSource = next?.Flow;
        TestCaseTitle.Text = next?.Name ?? string.Empty;
        if (next is not null) RestoreTestCaseSelection(next);
    }

    private void TestCaseWait_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TestCaseDefinition testCase })
            ConfigureTestCaseWait(testCase);
        e.Handled = true;
    }

    private void ConfigureTestCaseWait(TestCaseDefinition testCase)
    {
        var dialog = new WaitDialog(testCase.WaitAfterMs) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        testCase.WaitAfterMs = dialog.DurationMs;
        RebuildTestCaseTree();
        AppendLog(dialog.DurationMs == 0
            ? $"已清除用例间等待：{testCase.Name}。"
            : $"已设置用例间等待：{testCase.Name} → {dialog.DurationMs} ms。" );
    }

    private void RenameTestNode(TestFlowNode node)
    {
        var dialog = new RenameDialog(node.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        node.Name = dialog.Value;
        node.NameIsUserDefined = true;
        RefreshTestFlowTreePreservingSelection();
    }

    private async void RunTestPlan_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnectedForRun("测试计划")) return;
        if (!BeginRun()) return;
        BeginWorkspaceRunSelection();
        try
        {
        foreach (var testCase in _configuration.TestPlan.Cases)
        {
            testCase.LastStatus = testCase.Enabled ? TestResultStatus.未进入 : TestResultStatus.跳过;
            foreach (var node in testCase.Flow) SetTestNodeStatus(node, TestResultStatus.未进入);
        }
        RefreshRunVisuals();
        SetTestRunStatus("测试计划运行中", "StatusRunningBrush");
        _testCaseResults.Clear();
        var runner = CreateAutomaticTestRunner();
        var result = await runner.RunAsync(_runCts?.Token ?? CancellationToken.None, new Progress<TestNodeResult>(x => AppendTestNodeResult(x)));
        if (!string.IsNullOrWhiteSpace(result.Error)) AppendLog($"运行预检失败：{result.Error}");
        foreach (var testCase in result.Cases)
        {
            _testCaseResults.Add(testCase);
            var definition = _configuration.TestPlan.Cases.FirstOrDefault(x => x.Id == testCase.CaseId);
            if (definition is not null) definition.LastStatus = testCase.Status;
            if (definition is not null) ApplyNodeResults(definition, testCase.Nodes);
            AppendLog($"测试用例 [{testCase.Name}]：{testCase.Status}{(string.IsNullOrWhiteSpace(testCase.Error) ? string.Empty : $"；原因：{testCase.Error}")}");
        }
        RefreshRunVisuals();
        AppendLog($"自动测试完成：{result.Status}");
        SetTestRunStatus($"计划：{result.Status}", result.Status == TestResultStatus.通过 ? "GreenBrush" : "RedBrush");
        }
        catch (OperationCanceledException)
        {
            AppendLog("自动测试已取消。");
            SetTestRunStatus("计划：已取消", "AmberBrush");
        }
        catch (Exception ex)
        {
            AppendLog($"自动测试执行错误：{ex.GetType().Name}：{ex.Message}");
            SetTestRunStatus("计划：执行错误", "RedBrush");
        }
        finally { EndWorkspaceRunSelection(); EndRun(); }
    }

    private async void RunTestCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTestCase is not null) await RunTestCaseAsync(_selectedTestCase);
    }

    private async Task RunTestCaseAsync(TestCaseDefinition testCase)
    {
        if (!EnsureConnectedForRun($"测试用例 [{testCase.Name}]")) return;
        if (!BeginRun()) return;
        BeginWorkspaceRunSelection();
        try
        {
        testCase.LastStatus = TestResultStatus.运行中;
        foreach (var node in testCase.Flow) SetTestNodeStatus(node, TestResultStatus.未进入);
        RefreshRunVisuals();
        SetTestRunStatus($"{testCase.Name} 运行中", "StatusRunningBrush");
        var plan = new TestPlan { Id = _configuration.TestPlan.Id, Name = _configuration.TestPlan.Name, Cases = { testCase } };
        var runner = CreateAutomaticTestRunner();
        var result = await runner.RunAsync(plan, _runCts?.Token ?? CancellationToken.None, new Progress<TestNodeResult>(x => AppendTestNodeResult(x)));
        if (!string.IsNullOrWhiteSpace(result.Error)) AppendLog($"运行预检失败：{result.Error}");
        _testCaseResults.Clear();
        foreach (var item in result.Cases) _testCaseResults.Add(item);
        var caseResult = result.Cases.FirstOrDefault(x => x.CaseId == testCase.Id);
        if (caseResult is not null)
        {
            testCase.LastStatus = caseResult.Status;
            ApplyNodeResults(testCase, caseResult.Nodes);
        }
        RefreshRunVisuals();
        foreach (var item in result.Cases)
            AppendLog($"测试用例 [{item.Name}]：{item.Status}{(string.IsNullOrWhiteSpace(item.Error) ? string.Empty : $"；原因：{item.Error}")}");
        SetTestRunStatus($"{testCase.Name}：{result.Status}", result.Status == TestResultStatus.通过 ? "GreenBrush" : "RedBrush");
        }
        catch (OperationCanceledException)
        {
            AppendLog($"测试用例 [{testCase.Name}] 已取消。");
            SetTestRunStatus($"{testCase.Name}：已取消", "AmberBrush");
        }
        catch (Exception ex)
        {
            AppendLog($"测试用例 [{testCase.Name}] 执行错误：{ex.GetType().Name}：{ex.Message}");
            SetTestRunStatus($"{testCase.Name}：执行错误", "RedBrush");
        }
        finally { EndWorkspaceRunSelection(); EndRun(); }
    }

    private void AppendTestNodeResult(TestNodeResult node)
    {
        foreach (var testCase in _configuration.TestPlan.Cases)
        {
            if (TrySetTestNodeStatus(testCase.Flow, node.NodeId, node.Status))
            {
                break;
            }
        }
        var details = new List<string>();
        if (node.HasExpectation) details.Add($"期望={node.Expected ?? "未设置"}");
        if (node.ActualValue is { } actual) details.Add($"实际={actual:0.###}");
        if (!string.IsNullOrWhiteSpace(node.RawFrame)) details.Add($"报文={node.RawFrame}");
        if (node.CompletedAt is { } completed)
        {
            var elapsed = completed - node.StartedAt;
            details.Add($"耗时={Math.Max(0, elapsed.TotalMilliseconds):0} ms");
        }
        if (!string.IsNullOrWhiteSpace(node.Error)) details.Add($"原因={node.Error}");
        var ownerCase = _configuration.TestPlan.Cases.FirstOrDefault(testCase => FindTestFlowNode(testCase.Flow, node.NodeId) is not null);
        var location = ownerCase is null ? $"节点 [{node.Name}]" : $"测试用例 [{ownerCase.Name}] · 步骤/节点 [{node.Name}]";
        AppendLog($"{location}：{node.Status}{(details.Count == 0 ? string.Empty : $"；{string.Join("；", details)}")}");
    }

    private static void SetTestNodeStatus(TestFlowNode node, TestResultStatus status)
    {
        node.LastStatus = status;
        foreach (var child in node.Children) SetTestNodeStatus(child, status);
    }

    private static bool TrySetTestNodeStatus(IEnumerable<TestFlowNode> nodes, string id, TestResultStatus status)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) { node.LastStatus = status; return true; }
            if (TrySetTestNodeStatus(node.Children, id, status)) return true;
        }
        return false;
    }

    private static void ApplyNodeResults(TestCaseDefinition definition, IEnumerable<TestNodeResult> results)
    {
        foreach (var result in results)
        {
            TrySetTestNodeStatus(definition.Flow, result.NodeId, result.Status);
            ApplyNodeResults(definition, result.Children);
        }
    }

    private void SetTestRunStatus(string text, string brushKey)
    {
        TestRunStatusText.Text = text;
        TestRunStatusText.ToolTip = text;
        TestRunDot.Fill = TryFindResource(brushKey) as Brush ?? Brushes.SlateGray;
    }

    private void RefreshRunVisuals()
    {
        var snapshot = _runWorkspaceSelection;
        _restoringWorkspaceSelection = true;
        try
        {
            TestCaseTree.Items.Refresh();
            if (_selectedTestCase is not null) TestFlowTree.Items.Refresh();
        }
        finally { _restoringWorkspaceSelection = false; }
        if (snapshot is not null) RestoreWorkspaceSelection(snapshot);
    }

    private void BeginWorkspaceRunSelection()
        => _runWorkspaceSelection = CaptureWorkspaceSelection();

    private void EndWorkspaceRunSelection()
    {
        var snapshot = _runWorkspaceSelection;
        _runWorkspaceSelection = null;
        if (snapshot is null) return;
        RestoreWorkspaceSelection(snapshot);
    }

    private bool EnsureConnectedForRun(string target)
    {
        if (_usb.IsOpen) return true;
        var text = $"未连接：无法运行{target}";
        SetTestRunStatus(text, "RedBrush");
        AppendLog("请先连接主控板，再运行测试或资源。");
        return false;
    }

    private Grid BuildTreeHeader(ConfigurationTreeItem source)
    {
        if (_communicationTreeEntries.TryGetValue(source.Id, out var communication))
        {
            var syntheticHeader = new Grid { MinWidth = 210, Margin = new Thickness(0, 1, 0, 1) };
            syntheticHeader.Children.Add(new TextBlock { Text = source.Name, FontSize = 12, FontWeight = communication.Kind is "database" or "message" ? FontWeights.SemiBold : FontWeights.Normal, Foreground = communication.Kind == "signal" ? Brushes.DarkSlateGray : Brushes.DarkCyan, VerticalAlignment = VerticalAlignment.Center });
            return syntheticHeader;
        }
        var header = new Grid { MinWidth = 190 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // “流程/操作”是实现分类，不是用户要记忆的对象名称。树只负责导航，
        // 类型通过颜色、图标和中间步骤卡片表达，避免同一名称被前缀重复污染。
        namePanel.Children.Add(new TextBlock
        {
            Text = "",
            Width = 0,
            Foreground = Brushes.Transparent,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        namePanel.Children.Add(new TextBlock { Text = source.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(namePanel, 0);
        header.Children.Add(namePanel);

        var addButton = new Button
        {
            Content = "+",
            Style = (Style)FindResource("ConfigurationAddButtonStyle"),
            Width = 23,
            Height = 23,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DodgerBlue,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip = source.Kind == ConfigurationItemKind.文件夹 ? "在此分类中添加对象" : "在此对象同级添加对象",
            Tag = source
        };
        addButton.Click += TreeAddAtomicOperation_Click;
        Grid.SetColumn(addButton, 1);
        header.Children.Add(addButton);

        if (!ReferenceEquals(source, _configuration.Root))
        {
            var deleteButton = new Button
            {
                Content = "×",
                Style = (Style)FindResource("ConfigurationDeleteButtonStyle"),
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(179, 71, 71)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                ToolTip = "删除此对象",
                Tag = source
            };
            deleteButton.Click += ConfigurationTreeDeleteButton_Click;
            Grid.SetColumn(deleteButton, 2);
            header.Children.Add(deleteButton);
        }
        return header;
    }

    private void ConfigurationTreeDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConfigurationTreeItem item }) return;
        var treeItem = FindTreeItem(ConfigurationTree, item.Id);
        if (treeItem is not null) treeItem.IsSelected = true;
        _selectedItem = item;
        Delete_Click(sender, e);
        e.Handled = true;
    }

    private static bool SelectTreeItem(TreeViewItem item, string id)
    {
        if (item.DataContext is ConfigurationTreeItem { Id: var currentId } && currentId == id)
        {
            item.IsSelected = true;
            return true;
        }
        foreach (var child in item.Items.OfType<TreeViewItem>())
            if (SelectTreeItem(child, id)) { item.IsExpanded = true; return true; }
        return false;
    }

    private void TreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { DataContext: ConfigurationTreeItem item }) return;
        _selectedItem = item;
        if (_communicationTreeEntries.TryGetValue(item.Id, out var communication))
        {
            _selectedSequence = null; _selectedStep = null; StepsTree.ItemsSource = null;
            SequenceTitle.Text = item.Name;
            SequenceHint.Text = communication.Kind switch
            {
                "database" => "通信数据库定义：双击查看报文和信号；右键可创建广播或解析资源。",
                "message" => "报文定义：双击查看；右键可创建广播报文资源。",
                "signal" => "信号定义：双击查看；右键可创建报文解析资源。",
                _ => "通信数据库目录"
            };
            ShowEditor(null); return;
        }
        if ((item.Kind is ConfigurationItemKind.序列 or ConfigurationItemKind.原子操作) && item.Sequence is not null) OpenSequence(item.Sequence);
        else { _selectedSequence = null; _selectedStep = null; StepsTree.ItemsSource = null; SequenceTitle.Text = item.Name; SequenceHint.Text = string.Empty; ShowEditor(null); }
    }

    private void OpenSequence(ConfigurationSequence sequence)
    {
        _selectedSequence = sequence; _selectedStep = null; CloseStepEditor();
        // 资源和自动测试共用同一个中间编辑区；资源树选中后切换到资源步骤视图。
        TestFlowTree.Visibility = Visibility.Collapsed;
        StepsTree.Visibility = Visibility.Visible;
        AutomaticHeaderControls.Visibility = Visibility.Collapsed;
        ResourceHeaderControls.Visibility = Visibility.Visible;
        TestCaseTitle.Text = sequence.Name;
        TestCaseHint.Text = "";
        StepsTree.ItemsSource = sequence.Steps;
        RefreshReferenceSummaries(sequence.Steps);
        ShowEditor(null);
    }

    private void RefreshStepsView()
    {
        RefreshAllReferenceSummaries();
        StepsTree.Items.Refresh();
        UpdateGroupSelectionUi();
        if (_selectedStep is not null)
            RestoreStepSelection(_selectedStep);
    }

    private void RestoreStepSelection(ConfigurationStep step)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StepsTree.UpdateLayout();
            SelectTreeItemWithoutFocus(StepsTree, step);
        }), DispatcherPriority.ApplicationIdle);
    }

    private void RefreshAllReferenceSummaries()
    {
        foreach (var sequence in AllSequences())
            RefreshReferenceSummaries(sequence.Steps);
    }

    /// <summary>资源变更后立即刷新所有自动测试引用，避免必须切换页面才能看到新名称和摘要。</summary>
    private void RefreshLiveResourceReferences(TestFlowNode? keepStep = null)
    {
        RefreshAllReferenceSummaries();
        RefreshTestFlowSummaries();
        TestFlowTree?.Items.Refresh();
        if (keepStep is not null) RestoreTestFlowSelection(keepStep);
    }

    private void RefreshReferenceSummaries(IEnumerable<ConfigurationStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Kind == ConfigurationStepKind.引用序列)
            {
                var target = _configuration.FindSequence(step.Reference.SequenceId);
                step.SetResolvedSummary(target is null ? "未选择引用" : ReferenceSummaryResolver.Resolve(target));
            }
            else
            {
                step.SetResolvedSummary(null);
            }
            RefreshReferenceSummaries(step.Children);
        }
    }


    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedItem?.Kind == ConfigurationItemKind.文件夹 ? _selectedItem : _configuration.Root;
        var item = parent.AddFolder("未命名文件夹"); _selectedItem = item; RebuildTree(); BeginTreeRename();
    }

    private void AddSequence_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedItem?.Kind == ConfigurationItemKind.文件夹 ? _selectedItem : _configuration.Root;
        var item = parent.AddSequence("未命名序列"); _selectedItem = item; RebuildTree(); OpenSequence(item.Sequence!); BeginTreeRename();
    }

    private void AddAtomicOperationMenu_Click(object sender, RoutedEventArgs e)
    {
        var target = sender as FrameworkElement;
        var parent = _atomicOperationParent ?? (_selectedItem?.Kind == ConfigurationItemKind.文件夹 ? _selectedItem : _configuration.Root);
        ShowAtomicOperationMenu(target, parent);
    }

    private void TreeAddAtomicOperation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConfigurationTreeItem item }) return;
        _selectedItem = item;
        var parent = item.Kind == ConfigurationItemKind.文件夹 ? item : FindParent(_configuration.Root, item.Id) ?? _configuration.Root;
        _atomicOperationParent = parent;
        var treeItem = FindTreeItem(ConfigurationTree, item.Id);
        if (treeItem is not null) treeItem.IsSelected = true;
        ShowAtomicOperationMenu(sender as FrameworkElement, parent);
        e.Handled = true;
    }

    private void ShowAtomicOperationMenu(FrameworkElement? target, ConfigurationTreeItem parent)
    {
        _atomicOperationParent = parent;
        var menu = new ContextMenu { PlacementTarget = target, Placement = target is null ? PlacementMode.MousePoint : PlacementMode.Bottom };
        foreach (var kind in new[] { ConfigurationStepKind.广播报文, ConfigurationStepKind.动作控制, ConfigurationStepKind.电源控制, ConfigurationStepKind.报文解析, ConfigurationStepKind.等待 })
        {
            var copy = kind;
            var item = new MenuItem { Header = copy switch
            {
                ConfigurationStepKind.广播报文 => "发送报文",
                ConfigurationStepKind.动作控制 => "逻辑控制",
                ConfigurationStepKind.电源控制 => "电源控制",
                ConfigurationStepKind.报文解析 => "报文解析",
                _ => "等待"
            } };
            item.Click += (_, _) => AddAtomicOperation(copy);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => _atomicOperationParent = null;
        menu.IsOpen = true;
    }

    private void AddAtomicOperation(ConfigurationStepKind kind)
    {
        var parent = _atomicOperationParent ?? (_selectedItem?.Kind == ConfigurationItemKind.文件夹 ? _selectedItem : _configuration.Root);
        _atomicOperationParent = null;
        var item = parent.AddAtomicOperation(kind);
        _selectedItem = item;
        RebuildTree();
        OpenSequence(item.Sequence!);
        BeginTreeRename();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        BeginTreeRename();
    }

    private void BeginTreeRename()
    {
        if (_selectedItem is null) { AppendLog("请先选择树中的对象。"); return; }
        var treeItem = FindTreeItem(ConfigurationTree, _selectedItem.Id);
        var header = treeItem?.Header as Grid;
        var namePanel = header?.Children.OfType<StackPanel>().FirstOrDefault();
        var old = namePanel?.Children.OfType<TextBlock>().LastOrDefault();
        if (treeItem is null || old is null) return;
        var box = new TextBox { Text = _selectedItem.Name, MinWidth = 130, Padding = new Thickness(2) };
        box.LostKeyboardFocus += (_, _) => CommitTreeRename(box, old, treeItem);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitTreeRename(box, old, treeItem); e.Handled = true; }
            if (e.Key == Key.Escape) { treeItem.Header = BuildTreeHeader(_selectedItem!); e.Handled = true; }
        };
        treeItem.Header = box;
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    private void CommitTreeRename(TextBox box, TextBlock old, TreeViewItem treeItem)
    {
        // Enter 会先触发 LostKeyboardFocus；防止 Enter 与失焦重复提交，
        // 也不要把已经挂在旧 Header 内的 TextBlock 再次挂回 TreeViewItem。
        if (treeItem.Header is not TextBox) return;
        if (_selectedItem is not null && !string.IsNullOrWhiteSpace(box.Text))
        {
            _selectedItem.Name = box.Text.Trim();
            if (_selectedItem.Sequence is not null)
            {
                _selectedItem.Sequence.Name = _selectedItem.Name;
                if (_selectedItem.Kind == ConfigurationItemKind.原子操作 && _selectedItem.Sequence.Steps.Count == 1)
                    _selectedItem.Sequence.Steps[0].Name = _selectedItem.Name;
                SequenceTitle.Text = _selectedItem.Name;
            }
            treeItem.Header = BuildTreeHeader(_selectedItem);
            RefreshLiveResourceReferences();
            return;
        }
        treeItem.Header = BuildTreeHeader(_selectedItem!);
    }

    private static TreeViewItem? FindTreeItem(ItemsControl parent, string id)
    {
        foreach (var item in parent.Items.OfType<TreeViewItem>())
        {
            if (item.DataContext is ConfigurationTreeItem { Id: var currentId } && currentId == id) return item;
            var found = FindTreeItem(item, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem) source = VisualTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static ConfigurationTreeItem? FindParent(ConfigurationTreeItem parent, string id)
    {
        foreach (var child in parent.Children)
        {
            if (child.Id == id) return parent;
            var found = FindParent(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool ContainsTreeItem(ConfigurationTreeItem parent, string id)
    {
        if (parent.Id == id) return true;
        return parent.Children.Any(child => ContainsTreeItem(child, id));
    }

    private void ConfigurationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = e.GetPosition(ConfigurationTree);
        _configurationTreeScrollOffset = FindVisualChild<ScrollViewer>(ConfigurationTree)?.VerticalOffset;
        var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
        _treeDragCandidate = item?.DataContext as ConfigurationTreeItem;
        if (_treeDragCandidate == _configuration.Root || (_treeDragCandidate is not null && _communicationTreeEntries.ContainsKey(_treeDragCandidate.Id))) _treeDragCandidate = null;
    }

    private void ConfigurationTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _treeDragCandidate = null;
        var offset = _configurationTreeScrollOffset;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (offset is double value && FindVisualChild<ScrollViewer>(ConfigurationTree) is { } scroll)
                scroll.ScrollToVerticalOffset(value);
            _configurationTreeScrollOffset = null;
        }), DispatcherPriority.ContextIdle);
    }

    private void ConfigurationTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeDragCandidate is not ConfigurationTreeItem item) return;
        var current = e.GetPosition(ConfigurationTree);
        if (Math.Abs(current.X - _treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _treeDragCandidate = null;
        DragDrop.DoDragDrop(ConfigurationTree, item, DragDropEffects.Move);
    }

    private void ConfigurationTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ConfigurationTreeItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ConfigurationTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ConfigurationTreeItem)) is not ConfigurationTreeItem source || source == _configuration.Root) return;
        var targetControl = FindTreeViewItem(e.OriginalSource as DependencyObject);
        var target = targetControl?.DataContext as ConfigurationTreeItem;
        if (target is null || target == source || ContainsTreeItem(source, target.Id) || _communicationTreeEntries.ContainsKey(target.Id)) return;

        var oldParent = FindParent(_configuration.Root, source.Id);
        if (oldParent is null) return;

        // 拖到文件夹中间区域表示移入文件夹；拖到文件夹上/下沿或序列表示同级排序。
        var targetY = targetControl is null ? 0 : e.GetPosition(targetControl).Y;
        var targetHeight = targetControl?.ActualHeight ?? 0;
        var dropInsideFolder = target.Kind == ConfigurationItemKind.文件夹 &&
                               targetY > targetHeight * 0.25 && targetY < targetHeight * 0.75;
        if (dropInsideFolder)
        {
            oldParent.Children.Remove(source);
            target.Children.Add(source);
        }
        else
        {
            var newParent = FindParent(_configuration.Root, target.Id) ?? _configuration.Root;
            var targetIndex = newParent.Children.IndexOf(target);
            if (targetIndex < 0) return;
            if (e.GetPosition(targetControl!).Y > targetHeight / 2) targetIndex++;
            var oldIndex = oldParent.Children.IndexOf(source);
            if (ReferenceEquals(oldParent, newParent) && oldIndex < targetIndex) targetIndex--;
            oldParent.Children.Remove(source);
            if (ReferenceEquals(oldParent, newParent) && oldParent.Children.Count < targetIndex) targetIndex = oldParent.Children.Count;
            newParent.Children.Insert(Math.Clamp(targetIndex, 0, newParent.Children.Count), source);
        }
        _selectedItem = source;
        RebuildTree();
        e.Handled = true;

        _selectedItem = source;
        RebuildTree();
        e.Handled = true;
    }


    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || _selectedItem == _configuration.Root) { AppendLog("请先选择要删除的文件夹或序列。"); return; }
        if (MessageBox.Show(this, $"删除“{_selectedItem.Name}”及其内容？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (RemoveWithCapture(_configuration.Root, _selectedItem.Id, out var parent, out var removed, out var index))
        {
            _deletedStep = null; _deletedFromSequence = null; _deletedStepOwner = null; _deletedStepIndex = -1;
            _deletedFlowNode = null; _deletedFlowCase = null; _deletedFlowOwner = null; _deletedFlowIndex = -1;
            _deletedTreeParent = parent; _deletedTreeItem = removed; _deletedTreeIndex = index;
            _selectedItem = null; _selectedSequence = null; StepsTree.ItemsSource = null; ShowEditor(null); RebuildTree();
            AppendLog($"已删除“{removed!.Name}”，可按 Ctrl+Z 撤销。");
            UndoDeleteButton.Visibility = Visibility.Visible;
        }
    }

    private void ConfigurationTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_contextTreeItem is null)
        {
            _selectedItem = null;
            var blankMenu = new ContextMenu();
            blankMenu.Items.Add(MenuItem("新建流程", (_, _) => AddSequence_Click(this, new RoutedEventArgs())));
            blankMenu.Items.Add(MenuItem("添加操作", (_, _) =>
            {
                _atomicOperationParent = _configuration.Root;
                AddAtomicOperationMenu_Click(this, new RoutedEventArgs());
            }));
            blankMenu.Items.Add(MenuItem("新建分类", (_, _) => AddFolder_Click(this, new RoutedEventArgs())));
            ((TreeView)sender).ContextMenu = blankMenu;
            return;
        }
        // 右键菜单必须针对鼠标所在节点，而不是上一次选中的节点。
        if (_contextTreeItem?.DataContext is ConfigurationTreeItem contextItem)
        {
            _selectedItem = contextItem;
            _contextTreeItem.IsSelected = true;
        }
        _contextTreeItem = null;
        var menu = new ContextMenu();
        if (_selectedItem is not null && _communicationTreeEntries.TryGetValue(_selectedItem.Id, out var communication))
        {
            menu.Items.Add(MenuItem("查看定义", (_, _) => ShowDatabaseOverview()));
            if (communication.Value is CanMessageDefinition message)
                menu.Items.Add(MenuItem("创建广播报文资源", (_, _) => CreateBroadcastResource(message)));
            if (communication.Value is CommunicationSignalDefinition signal)
                menu.Items.Add(MenuItem("创建报文解析资源", (_, _) => CreateParseResource(signal)));
            ((TreeView)sender).ContextMenu = menu;
            return;
        }
        if (_selectedItem?.Kind == ConfigurationItemKind.文件夹)
        {
            menu.Items.Add(MenuItem("新建流程", (_, _) => AddSequence_Click(this, new RoutedEventArgs())));
            menu.Items.Add(MenuItem("添加操作", (_, _) =>
            {
                _atomicOperationParent = _selectedItem;
                AddAtomicOperationMenu_Click(this, new RoutedEventArgs());
            }));
            menu.Items.Add(MenuItem("新建文件夹", (_, _) => AddFolder_Click(this, new RoutedEventArgs())));
        }
        if (_selectedItem?.Kind is ConfigurationItemKind.序列 or ConfigurationItemKind.原子操作)
        {
            var treeItem = _selectedItem;
            menu.Items.Add(MenuItem("打开并编辑", (_, _) => { if (treeItem.Sequence is not null) OpenSequence(treeItem.Sequence); }));
            if (treeItem.Kind == ConfigurationItemKind.序列)
            {
                menu.Items.Add(MenuItem("添加引用到当前序列", (_, _) => AddSequenceReference(treeItem)));
                menu.Items.Add(MenuItem("复制步骤到当前序列", (_, _) => CopySequenceSteps(treeItem)));
            }
            menu.Items.Add(MenuItem("新建操作", (_, _) =>
            {
                _atomicOperationParent = FindParent(_configuration.Root, treeItem.Id) ?? _configuration.Root;
                AddAtomicOperationMenu_Click(this, new RoutedEventArgs());
            }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("重命名（F2）", (_, _) => Rename_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("删除（Delete）", (_, _) => Delete_Click(this, new RoutedEventArgs())));
        ((TreeView)sender).ContextMenu = menu;
    }

    private void CreateBroadcastResource(CanMessageDefinition message)
    {
        var parent = ChooseResourceFolder();
        if (parent is null) return;
        var item = parent.AddAtomicOperation(ConfigurationStepKind.广播报文);
        item.Name = message.Name; item.Sequence!.Name = message.Name; var step = item.Sequence.Steps[0]; step.Name = message.Name; _configuration.CommunicationDatabase?.Normalize(); _definitionBindingService.BindBroadcast(step, message); step.Broadcast.PeriodMs = message.CycleTimeMs ?? 100; step.Broadcast.Mode = BroadcastMode.开始周期发送;
        _selectedItem = item; RebuildTree(); OpenSequence(item.Sequence); AppendLog($"已从通信数据库创建广播资源：{message.Name}。");
    }

    private void CreateParseResource(CommunicationSignalDefinition definition)
    {
        var signal = definition.Signal;
        var parent = ChooseResourceFolder();
        if (parent is null) return;
        var item = parent.AddAtomicOperation(ConfigurationStepKind.报文解析);
        item.Name = signal.Name; item.Sequence!.Name = signal.Name; var step = item.Sequence.Steps[0]; step.Name = signal.Name; var databaseMessage = _configuration.CommunicationDatabase?.Messages.FirstOrDefault(x => x.Id == definition.Id && x.Extended == definition.Extended); if (databaseMessage is not null) { _configuration.CommunicationDatabase?.Normalize(); _definitionBindingService.BindParse(step, databaseMessage, signal); }
        _selectedItem = item; RebuildTree(); OpenSequence(item.Sequence); AppendLog($"已从通信数据库创建解析资源：{signal.Name} · 0x{definition.Id:X}。");
    }

    private void ConfigurationTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not TreeViewItem) source = VisualTreeHelper.GetParent(source);
        if (source is TreeViewItem item)
        {
            _contextTreeItem = item;
            if (item.DataContext is ConfigurationTreeItem contextItem) _selectedItem = contextItem;
            item.IsSelected = true;
        }
    }

    private void ConfigurationTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedItem is not null && _communicationTreeEntries.TryGetValue(_selectedItem.Id, out var communication))
        {
            ShowDatabaseOverview();
            e.Handled = true;
            return;
        }
        if (_selectedItem?.Sequence is null) return;

        // 配置树中的原子操作只有一个底层步骤。双击它时直接进入该步骤属性，
        // 不再让用户先打开序列、再点一次步骤。
        if (_selectedItem.Kind == ConfigurationItemKind.原子操作 && _selectedItem.Sequence.Steps.Count == 1)
        {
            var step = _selectedItem.Sequence.Steps[0];
            OpenSequence(_selectedItem.Sequence);
            // 等双击的第二次鼠标输入结束后再打开工作区检查器，避免 TreeView
            // 自身的双击选择过程覆盖当前步骤选择。
            _ = Dispatcher.BeginInvoke(new Action(() => ShowEditor(step)), DispatcherPriority.ContextIdle);
            e.Handled = true;
            return;
        }

        OpenSequence(_selectedItem.Sequence);
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            UndoDelete(); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C && StepsTree.IsKeyboardFocusWithin && _selectedStep is not null)
        {
            CopySelectedStep(); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && StepsTree.IsKeyboardFocusWithin)
        {
            PasteStep(); e.Handled = true; return;
        }
        if (e.Key == Key.Insert && ConfigurationTree.IsKeyboardFocusWithin && Keyboard.FocusedElement is not TextBox)
        {
            var treeItem = _selectedItem is null ? null : FindTreeItem(ConfigurationTree, _selectedItem.Id);
            var parent = _selectedItem?.Kind == ConfigurationItemKind.文件夹
                ? _selectedItem
                : (_selectedItem is null ? _configuration.Root : FindParent(_configuration.Root, _selectedItem.Id) ?? _configuration.Root);
            _atomicOperationParent = parent;
            ShowAtomicOperationMenu(treeItem, parent);
            e.Handled = true; return;
        }
        if (e.Key == Key.F2 && TestCaseTree.IsKeyboardFocusWithin && _selectedTestCase is not null)
        { RenameTestCase(_selectedTestCase); e.Handled = true; }
        else if (e.Key == Key.F2 && TestFlowTree.IsKeyboardFocusWithin && _selectedTestNode is not null)
        { RenameTestNode(_selectedTestNode); e.Handled = true; }
        else if (e.Key == Key.F2 && _selectedItem is not null) { Rename_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
        {
            if (ConfigurationTree.IsKeyboardFocusWithin) Delete_Click(this, new RoutedEventArgs());
            else if (StepsTree.IsKeyboardFocusWithin) DeleteStep_Click(this, new RoutedEventArgs());
            else if (TestCaseTree.IsKeyboardFocusWithin && _selectedTestCase is not null)
            { RemoveTestCase(_selectedTestCase); }
            else if (TestFlowTree.IsKeyboardFocusWithin && _selectedTestCase is not null && _selectedTestNode is not null)
            { RemoveTestFlowNode(_selectedTestNode); }
            e.Handled = true;
        }
    }

    private void AddStepMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSequence is null) { AppendLog("请先在配置树中选择一个序列。"); return; }
        if (_selectedItem?.Kind == ConfigurationItemKind.原子操作) { AppendLog("操作只能包含一个步骤，请直接编辑当前步骤。"); return; }
        var menu = new ContextMenu { PlacementTarget = sender as Button, Placement = PlacementMode.Top };
        foreach (var kind in Enum.GetValues<ConfigurationStepKind>())
        {
            var item = new MenuItem { Header = kind.ToString() };
            item.Click += (_, _) => AddStep(kind);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void GroupSelectedSteps()
    {
        if (_selectedSequence is null) return;
        var owner = FindGroupSelectionOwner(_selectedSequence.Steps);
        var selected = owner?.Where(x => x.SelectedForGroup).ToList() ?? [];
        if (owner is null || selected.Count < 2) { AppendLog("请勾选至少两个同级步骤再组合。"); return; }
        var indexes = selected.Select(owner.IndexOf).OrderBy(x => x).ToList();
        if (indexes.Zip(indexes.Skip(1), (a, b) => b == a + 1).Any(x => !x)) { AppendLog("只能组合连续的同级步骤，避免改变原执行顺序。"); return; }
        var group = new ConfigurationStep { Kind = ConfigurationStepKind.组合, Name = "组合步骤" };
        foreach (var step in selected) { step.SelectedForGroup = false; group.Children.Add(step); owner.Remove(step); }
        owner.Insert(Math.Min(indexes[0], owner.Count), group); _selectedStep = group; RefreshStepsView();
        AppendLog($"已将 {selected.Count} 个连续步骤组合为“{group.Name}”。可双击组合查看或解组。");
    }
    private static ObservableCollection<ConfigurationStep>? FindGroupSelectionOwner(ObservableCollection<ConfigurationStep> items)
    {
        if (items.Count(x => x.SelectedForGroup) >= 2) return items;
        foreach (var item in items) { var found = FindGroupSelectionOwner(item.Children); if (found is not null) return found; }
        return null;
    }
    private void UngroupSelectedStep()
    {
        if (_selectedSequence is null || _selectedStep is not { Kind: ConfigurationStepKind.组合 } group) return;
        var owner = FindStepOwner(_selectedSequence.Steps, group.Id); if (owner is null) return;
        var index = owner.IndexOf(group); owner.Remove(group);
        foreach (var child in group.Children.ToList()) owner.Insert(index++, child);
        group.Children.Clear(); _selectedStep = null; RefreshStepsView(); ShowEditor(null);
        AppendLog("已解组当前步骤，原执行顺序已恢复。");
    }
    private void StepGroupCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateGroupSelectionUi();

    private void UpdateGroupSelectionUi()
    {
        if (!IsInitialized) return;
        var count = _selectedSequence is null ? 0 : CountGroupSelections(_selectedSequence.Steps);
        if (GroupSelectionHint is not null)
            GroupSelectionHint.Text = count == 0 ? "勾选步骤后组合" : $"已勾选 {count} 个步骤";
        if (GroupStepsButton is not null)
            GroupStepsButton.IsEnabled = count >= 2;
    }

    private static int CountGroupSelections(IEnumerable<ConfigurationStep> steps)
        => steps.Count(step => step.SelectedForGroup) + steps.Sum(step => CountGroupSelections(step.Children));

    private void GroupSelectedSteps_Click(object sender, RoutedEventArgs e) => GroupSelectedSteps();
    private void UngroupSelectedStep_Click(object sender, RoutedEventArgs e) => UngroupSelectedStep();

    private async void RunCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceTabs.SelectedIndex == 1 && _selectedTestNode is not null)
        {
            await RunSelectedTestFlowNodeAsync(_selectedTestNode);
            return;
        }

        if (_selectedStep is null)
        {
            AppendLog("请先选择要执行的步骤。");
            return;
        }

        if (!BeginRun()) return;
        try { await RunAsync([_selectedStep]); }
        catch (OperationCanceledException) { AppendLog("资源步骤执行已取消。"); }
        catch (Exception ex) { AppendLog($"资源步骤执行错误：{ex.GetType().Name}：{ex.Message}"); }
        finally { EndRun(); }
    }

    private async void RunAllSequence_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSequence is null)
        {
            AppendLog("请先打开要执行的序列。");
            return;
        }

        if (!BeginRun()) return;
        try { await RunAsync(_selectedSequence.Steps); }
        catch (OperationCanceledException) { AppendLog("资源序列执行已取消。"); }
        catch (Exception ex) { AppendLog($"资源序列执行错误：{ex.GetType().Name}：{ex.Message}"); }
        finally { EndRun(); }
    }

    private void AddSequenceReference(ConfigurationTreeItem? source)
    {
        if (_selectedSequence is null || source?.Sequence is null) { AppendLog("请先打开目标序列。"); return; }
        if (source.Sequence.Id == _selectedSequence.Id) { AppendLog("不能引用当前正在编辑的序列。"); return; }
        var step = _selectedSequence.AddStep(ConfigurationStepKind.引用序列);
        step.Name = source.Sequence.Name;
        step.Reference.SequenceId = source.Sequence.Id;
        _selectedStep = step; RefreshStepsView();
    }
    private void CopySequenceSteps(ConfigurationTreeItem? source)
    {
        if (_selectedSequence is null || source?.Sequence is null) { AppendLog("请先打开目标序列。"); return; }
        if (source.Sequence.Id == _selectedSequence.Id) { AppendLog("源序列与目标序列相同。"); return; }
        foreach (var original in source.Sequence.Steps)
        {
            var clone = System.Text.Json.JsonSerializer.Deserialize<ConfigurationStep>(System.Text.Json.JsonSerializer.Serialize(original));
            if (clone is null) continue;
            clone.RegenerateIdsRecursively(); clone.Name += " 副本";
            _selectedSequence.Steps.Add(clone);
        }
        RefreshStepsView();
        AppendLog($"已将序列“{source.Sequence.Name}”的步骤复制到当前序列。");
    }

    private void AddStep(ConfigurationStepKind kind)
    {
        if (_selectedSequence is null) { AppendLog("请先选择一个序列。"); return; }
        var step = _selectedSequence.AddStep(kind);
        if (kind == ConfigurationStepKind.引用序列) step.Name = "选择资源";
        _selectedStep = step; RefreshStepsView();
    }

    private void StepsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedStep = e.NewValue as ConfigurationStep ?? (e.NewValue as TreeViewItem)?.DataContext as ConfigurationStep;
    }

    private void StepsTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext is ConfigurationStep step)
        {
            _selectedStep = step;
            _ = Dispatcher.BeginInvoke(new Action(() => ShowEditor(step)), DispatcherPriority.ContextIdle);
            e.Handled = true;
        }
    }

    private void StepsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _stepDragStart = e.GetPosition(StepsTree);
        _stepTreeDragCandidate = FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext as ConfigurationStep;
    }
    private void StepsTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _stepTreeDragCandidate = null;
    private void StepsTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _stepTreeDragCandidate is not ConfigurationStep step) return;
        var current = e.GetPosition(StepsTree);
        if (Math.Abs(current.X - _stepDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - _stepDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _stepTreeDragCandidate = null;
        DragDrop.DoDragDrop(StepsTree, step, DragDropEffects.Move);
    }
    private void StepsTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ConfigurationStep)) || e.Data.GetDataPresent(typeof(ConfigurationTreeItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private void StepsTree_Drop(object sender, DragEventArgs e)
    {
        if (_selectedSequence is null) return;
        if (e.Data.GetData(typeof(ConfigurationTreeItem)) is ConfigurationTreeItem sourceSequence)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) CopySequenceSteps(sourceSequence); else AddSequenceReference(sourceSequence);
            RefreshStepsView(); e.Handled = true; return;
        }
        if (e.Data.GetData(typeof(ConfigurationStep)) is not ConfigurationStep source) return;
        var targetControl = FindTreeViewItem(e.OriginalSource as DependencyObject);
        var target = targetControl?.DataContext as ConfigurationStep;
        if (target is null)
        {
            var sourceOwner = FindStepOwner(_selectedSequence.Steps, source.Id); if (sourceOwner is null || ReferenceEquals(sourceOwner, _selectedSequence.Steps)) return;
            sourceOwner.Remove(source); _selectedSequence.Steps.Add(source); RefreshStepsView(); e.Handled = true; return;
        }
        if (ReferenceEquals(source, target) || ContainsStep(source, target.Id)) return;
        var oldOwner = FindStepOwner(_selectedSequence.Steps, source.Id); var newOwner = FindStepOwner(_selectedSequence.Steps, target.Id);
        if (oldOwner is null || newOwner is null) return;
        var y = e.GetPosition(targetControl!).Y; var height = targetControl!.ActualHeight;
        if (target.Kind == ConfigurationStepKind.组合 && y > height * .25 && y < height * .75)
        {
            oldOwner.Remove(source); target.Children.Add(source); targetControl.IsExpanded = true;
        }
        else
        {
            var index = newOwner.IndexOf(target) + (y > height / 2 ? 1 : 0);
            var oldIndex = oldOwner.IndexOf(source); if (ReferenceEquals(oldOwner, newOwner) && oldIndex < index) index--;
            oldOwner.Remove(source); newOwner.Insert(Math.Clamp(index, 0, newOwner.Count), source);
        }
        RefreshStepsView(); e.Handled = true;
    }
    private void StepsTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var targetItem = FindTreeViewItem(Mouse.DirectlyOver as DependencyObject);
        if (targetItem?.DataContext is ConfigurationStep targetStep)
        {
            targetItem.IsSelected = true;
            _selectedStep = targetStep;
            StepsTree.ContextMenu = BuildStepContextMenu(targetStep);
            return;
        }
        var menu = new ContextMenu();
        var insert = new MenuItem { Header = "添加步骤" };
        foreach (var kind in Enum.GetValues<ConfigurationStepKind>()) { var copy = kind; insert.Items.Add(MenuItem(DisplayStepKind(copy), (_, _) => AddStep(copy))); }
        menu.Items.Add(insert); menu.Items.Add(MenuItem("组合已勾选步骤", (_, _) => GroupSelectedSteps()));
        menu.Items.Add(MenuItem("解组当前组合", (_, _) => UngroupSelectedStep())); menu.Items.Add(MenuItem("复制", (_, _) => CopySelectedStep()));
        menu.Items.Add(MenuItem("创建独立副本", (_, _) => DuplicateSelectedStep()));
        menu.Items.Add(MenuItem("粘贴", (_, _) => PasteStep())); menu.Items.Add(MenuItem("删除", (_, _) => DeleteStep_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator()); menu.Items.Add(MenuItem("执行当前步骤", async (_, _) => await RunSelectedStepAsync()));
        StepsTree.ContextMenu = menu;
    }

    private ContextMenu BuildStepContextMenu(ConfigurationStep step)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("属性", (_, _) => ShowEditor(step)));
        menu.Items.Add(MenuItem("重命名（F2）", (_, _) => RenameConfigurationStep(step)));
        menu.Items.Add(MenuItem("复制", (_, _) => { _selectedStep = step; CopySelectedStep(); }));
        menu.Items.Add(MenuItem("创建独立副本", (_, _) => { _selectedStep = step; DuplicateSelectedStep(); }));
        menu.Items.Add(MenuItem("删除（Delete）", (_, _) => { _selectedStep = step; DeleteStep_Click(this, new RoutedEventArgs()); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("执行当前步骤", async (_, _) => { _selectedStep = step; await RunSelectedStepAsync(); }));
        return menu;
    }

    private void RenameConfigurationStep(ConfigurationStep step)
    {
        var dialog = new RenameDialog(step.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        step.Name = dialog.Value;
        SyncAtomicTreeName(step);
        RefreshStepSummary();
    }
    private static string DisplayStepKind(ConfigurationStepKind kind) => kind switch
    {
        ConfigurationStepKind.广播报文 => "广播报文",
        ConfigurationStepKind.报文解析 => "报文解析",
        ConfigurationStepKind.电源控制 => "电源控制",
        ConfigurationStepKind.动作控制 => "动作控制",
        ConfigurationStepKind.等待 => "等待",
        ConfigurationStepKind.引用序列 => "引用",
        ConfigurationStepKind.组合 => "组合",
        _ => kind.ToString()
    };
    private static string DisplayTestFlowKind(TestFlowNodeKind kind) => kind switch
    {
        TestFlowNodeKind.引用序列 => "引用",
        TestFlowNodeKind.原子操作 => "步骤",
        TestFlowNodeKind.组合 => "组合",
        _ => kind.ToString()
    };
    private static bool ContainsStep(ConfigurationStep parent, string id) => parent.Children.Any(x => x.Id == id || ContainsStep(x, id));
    private static ObservableCollection<ConfigurationStep>? FindStepOwner(ObservableCollection<ConfigurationStep> items, string id)
    {
        if (items.Any(x => x.Id == id)) return items;
        foreach (var item in items) { var found = FindStepOwner(item.Children, id); if (found is not null) return found; }
        return null;
    }

    private static ObservableCollection<TestFlowNode>? FindTestFlowOwner(ObservableCollection<TestFlowNode> items, string id)
    {
        if (items.Any(x => x.Id == id)) return items;
        foreach (var item in items)
        {
            var found = FindTestFlowOwner(item.Children, id);
            if (found is not null) return found;
        }
        return null;
    }

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ConfigurationStep step } && _selectedSequence is not null)
        {
            var owner = FindStepOwner(_selectedSequence.Steps, step.Id); if (owner is null) return;
            _deletedTreeItem = null; _deletedTreeParent = null; _deletedTreeIndex = -1;
            _deletedFlowNode = null; _deletedFlowCase = null; _deletedFlowOwner = null; _deletedFlowIndex = -1;
            _deletedStep = step; _deletedFromSequence = _selectedSequence; _deletedStepOwner = owner; _deletedStepIndex = owner.IndexOf(step); owner.Remove(step);
            if (ReferenceEquals(_selectedStep, step)) { _selectedStep = null; ShowEditor(null); }
            RefreshStepsView(); AppendLog($"已删除步骤“{step.Name}”，可按 Ctrl+Z 撤销。");
            UndoDeleteButton.Visibility = Visibility.Visible;
        }
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = header };
        item.Click += action;
        return item;
    }

    private void DuplicateSelectedStep()
    {
        if (_selectedSequence is null || _selectedStep is not ConfigurationStep source) return;
        // 复制步骤使用 JSON 深拷贝，避免共享参数对象。
        var cloned = System.Text.Json.JsonSerializer.Deserialize<ConfigurationStep>(System.Text.Json.JsonSerializer.Serialize(source));
        if (cloned is null) return;
        cloned.RegenerateIdsRecursively();
        cloned.Name = source.Name + " 副本";
        var owner = FindStepOwner(_selectedSequence.Steps, source.Id); if (owner is null) return;
        owner.Insert(owner.IndexOf(source) + 1, cloned); RefreshStepsView();
    }

    private void CopySelectedStep()
    {
        if (_selectedStep is ConfigurationStep step)
        {
            _clipboardStepJson = System.Text.Json.JsonSerializer.Serialize(step);
            AppendLog($"已复制步骤：{step.Name}");
        }
    }

    private void PasteStep()
    {
        if (_selectedSequence is null || string.IsNullOrWhiteSpace(_clipboardStepJson)) return;
        var step = System.Text.Json.JsonSerializer.Deserialize<ConfigurationStep>(_clipboardStepJson);
        if (step is null) return;
        step.RegenerateIdsRecursively();
        step.Name += " 副本";
        var owner = _selectedStep is null ? _selectedSequence.Steps : FindStepOwner(_selectedSequence.Steps, _selectedStep.Id) ?? _selectedSequence.Steps;
        var index = _selectedStep is null ? owner.Count : owner.IndexOf(_selectedStep) + 1;
        owner.Insert(Math.Clamp(index, 0, owner.Count), step); RefreshStepsView();
    }

    private async Task RunSelectedStepAsync()
    {
        if (_selectedStep is null) return;
        if (!EnsureConnectedForRun("资源步骤")) return;
        if (!BeginRun()) return;
        try { await RunAsync([_selectedStep]); }
        catch (OperationCanceledException) { AppendLog("资源步骤执行已取消。"); }
        catch (Exception ex) { AppendLog($"资源步骤执行错误：{ex.GetType().Name}：{ex.Message}"); }
        finally { EndRun(); }
    }
    private async Task RunSelectedSequenceAsync()
    {
        if (_selectedSequence is null) return;
        if (!EnsureConnectedForRun("资源")) return;
        if (!BeginRun()) return;
        try { await RunAsync(_selectedSequence.Steps); }
        catch (OperationCanceledException) { AppendLog("资源序列执行已取消。"); }
        catch (Exception ex) { AppendLog($"资源序列执行错误：{ex.GetType().Name}：{ex.Message}"); }
        finally { EndRun(); }
    }
    private static bool Remove(ConfigurationTreeItem parent, string id)
    {
        var own = parent.Children.FirstOrDefault(x => x.Id == id);
        if (own is not null) { parent.Children.Remove(own); return true; }
        return parent.Children.Any(x => Remove(x, id));
    }
    private static bool RemoveWithCapture(ConfigurationTreeItem parent, string id, out ConfigurationTreeItem? owner, out ConfigurationTreeItem? removed, out int index)
    {
        owner = null; removed = null; index = -1;
        var directIndex = parent.Children.ToList().FindIndex(x => x.Id == id);
        if (directIndex >= 0) { owner = parent; index = directIndex; removed = parent.Children[directIndex]; parent.Children.RemoveAt(directIndex); return true; }
        foreach (var child in parent.Children.ToList()) if (RemoveWithCapture(child, id, out owner, out removed, out index)) return true;
        return false;
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSequence is null) { AppendLog("请先在树中选择或新建一个序列。"); return; }
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<ConfigurationStepKind>(tag, out var kind)) return;
        var step = _selectedSequence.AddStep(kind); _selectedStep = step; RefreshStepsView();
    }
    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSequence is not null && _selectedStep is ConfigurationStep step)
        {
            var owner = FindStepOwner(_selectedSequence.Steps, step.Id); if (owner is null) return;
            _deletedTreeItem = null; _deletedTreeParent = null; _deletedTreeIndex = -1;
            _deletedFlowNode = null; _deletedFlowCase = null; _deletedFlowOwner = null; _deletedFlowIndex = -1;
            _deletedStep = step; _deletedFromSequence = _selectedSequence; _deletedStepOwner = owner; _deletedStepIndex = owner.IndexOf(step);
            owner.Remove(step); _selectedStep = null; ShowEditor(null); RefreshStepsView();
            AppendLog($"已删除步骤“{step.Name}”，可按 Ctrl+Z 撤销。 ");
            UndoDeleteButton.Visibility = Visibility.Visible;
        }
    }
    private void UndoDelete()
    {
        if (_deletedTreeItem is not null && _deletedTreeParent is not null)
        {
            _deletedTreeParent.Children.Insert(Math.Min(_deletedTreeIndex, _deletedTreeParent.Children.Count), _deletedTreeItem);
            RebuildTree(); AppendLog($"已恢复“{_deletedTreeItem.Name}”。");
            _deletedTreeItem = null; _deletedTreeParent = null; _deletedTreeIndex = -1; UndoDeleteButton.Visibility = Visibility.Collapsed; return;
        }
        if (_deletedFlowNode is not null && _deletedFlowCase is not null && _deletedFlowOwner is not null && _deletedFlowIndex >= 0)
        {
            _deletedFlowOwner.Insert(Math.Min(_deletedFlowIndex, _deletedFlowOwner.Count), _deletedFlowNode);
            if (ReferenceEquals(_selectedTestCase, _deletedFlowCase))
            {
                _selectedTestNode = _deletedFlowNode;
                _selectedTestNodeId = _deletedFlowNode.Id;
                RefreshTestFlowTreePreservingSelection(_deletedFlowNode.Id);
            }
            AppendLog($"已恢复流程节点“{_deletedFlowNode.Name}”。");
            _deletedFlowNode = null; _deletedFlowCase = null; _deletedFlowOwner = null; _deletedFlowIndex = -1;
            UndoDeleteButton.Visibility = Visibility.Collapsed; return;
        }
        if (_deletedStep is null || _deletedFromSequence is null || _deletedStepOwner is null || _deletedStepIndex < 0) return;
        _deletedStepOwner.Insert(Math.Min(_deletedStepIndex, _deletedStepOwner.Count), _deletedStep);
        if (ReferenceEquals(_selectedSequence, _deletedFromSequence)) RefreshStepsView();
        AppendLog($"已恢复步骤“{_deletedStep.Name}”。");
        _deletedStep = null; _deletedFromSequence = null; _deletedStepOwner = null; _deletedStepIndex = -1;
        UndoDeleteButton.Visibility = Visibility.Collapsed;
    }
    private void UndoDelete_Click(object sender, RoutedEventArgs e) => UndoDelete();
    private void ShowEditor(ConfigurationStep? step, ConfigurationStep? anchor = null, Action<string>? onNameChanged = null)
    {
        if (step is null)
        {
            CloseStepEditor();
            return;
        }

        _propertyStep = step;
        _propertyAnchorStep = anchor ?? step;
        InspectorTitle.Text = step.Name;
        BackToReferenceButton.Visibility = ReferenceEquals(step, _propertyAnchorStep) ? Visibility.Collapsed : Visibility.Visible;

        StepEditor.Content = BuildEditorPanel(step, x =>
        {
            InspectorTitle.Text = x;
            onNameChanged?.Invoke(x);
        }, ReferenceEquals(step, _propertyAnchorStep));
        // 统一资源配置与自动测试的属性定位：只相对于当前步骤卡片打开。
        OpenInspector();
    }

    /// <summary>
    /// 仅当“所选报文/所选解析信号”改变了编辑器结构时重建当前检查器。
    /// 这是停靠检查器，不应再通过 ShowEditor 走一遍打开/定位流程；保留滚动位置
    /// 能避免用户编辑一项后回到顶部或看到属性面板闪退。
    /// </summary>
    private void RebuildInspectorContentKeepingViewport(ConfigurationStep step)
    {
        if (!ReferenceEquals(_propertyStep, step)) return;

        var offset = StepInspectorScrollViewer.VerticalOffset;
        var anchor = _propertyAnchorStep ?? step;
        InspectorTitle.Text = step.Name;
        StepEditor.Content = BuildEditorPanel(step, title => InspectorTitle.Text = title, ReferenceEquals(step, anchor));
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StepInspectorScrollViewer.UpdateLayout();
            StepInspectorScrollViewer.ScrollToVerticalOffset(Math.Min(offset, StepInspectorScrollViewer.ScrollableHeight));
        }), DispatcherPriority.Loaded);
    }

    private static StackPanel InspectorSection(string title, string? hint = null)
    {
        var section = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray,
            Margin = new Thickness(0, 0, 0, 3)
        });
        if (!string.IsNullOrWhiteSpace(hint))
            section.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        return section;
    }

    private StackPanel BuildEditorPanel(ConfigurationStep step, Action<string> titleChanged, bool includeReferencePreview, bool includeResourceActions = true, bool includeEditorHeading = true)
    {
        var panel = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var title = new TextBlock { Text = $"{DisplayStepKind(step.Kind)}配置", FontWeight = FontWeights.SemiBold, FontSize = 14 };
        heading.Children.Add(title);
        if (includeResourceActions)
        {
            var resourceActions = new Button { Content = "资源操作", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "选择另存为资源或保存并绑定" };
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("另存为资源（保持独立）", (_, _) => SaveStepAsResource(step, false)));
            menu.Items.Add(MenuItem("保存并绑定资源", (_, _) => SaveStepAsResource(step, true)));
            resourceActions.ContextMenu = menu;
            resourceActions.Click += (_, _) => resourceActions.ContextMenu.IsOpen = true;
            DockPanel.SetDock(resourceActions, Dock.Right); heading.Children.Add(resourceActions);
        }
        if (includeEditorHeading) panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = "步骤信息", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 3, 0, 3) });
        panel.Children.Add(Field("步骤名称", step.Name,
            x => { step.Name = x; titleChanged(x); },
            () => SyncAtomicTreeName(step)));
        if (step.Kind is ConfigurationStepKind.广播报文 or ConfigurationStepKind.报文解析)
            panel.Children.Add(BuildSourceStatus(step));
        switch (step.Kind)
        {
            case ConfigurationStepKind.广播报文: BuildBroadcast(panel, step); break;
            case ConfigurationStepKind.报文解析: BuildParse(panel, step); break;
            case ConfigurationStepKind.电源控制: BuildPower(panel, step); break;
            case ConfigurationStepKind.动作控制: BuildAction(panel, step); break;
            case ConfigurationStepKind.等待: BuildWait(panel, step); break;
            case ConfigurationStepKind.引用序列: BuildSequenceReference(panel, step); break;
        }
        if (includeReferencePreview && step.Kind == ConfigurationStepKind.引用序列) BuildReferencePreview(panel, step);
        return panel;
    }

    private void SaveStepAsResource(ConfigurationStep step, bool bindCurrentStep)
    {
        var propertyNode = ResolvePropertyTestNode();
        var keepCase = propertyNode is null
            ? _selectedTestCase
            : _configuration.TestPlan.Cases.FirstOrDefault(x => FindTestFlowNode(x.Flow, propertyNode.Id) is not null);
        var keepNodeId = propertyNode?.Id ?? _selectedTestNodeId ?? _selectedTestNode?.Id;
        var parent = ChooseResourceFolder();
        if (parent is null) return;
        var saved = new ResourceRelationService().SaveStepAsResource(parent, step, bindCurrentStep, propertyNode);
        if (!saved.Succeeded || saved.Item is null)
        {
            AppendLog($"保存资源失败：{saved.Diagnostic?.Message ?? "未知错误"}");
            return;
        }
        var item = saved.Item;
        var copy = item.Sequence!.Steps.Single();
        RebuildTree();
        if (keepCase is not null)
        {
            _selectedTestCase = keepCase;
            _pendingTestCaseSelection = keepCase;
            RestoreTestCaseSelection(keepCase);
            TestFlowTree.ItemsSource = keepCase.Flow;
            if (keepNodeId is not null)
            {
                _selectedTestNode = FindTestFlowNode(keepCase.Flow, keepNodeId);
                _selectedTestNodeId = _selectedTestNode?.Id;
                if (_selectedTestNode is not null) RestoreTestFlowSelection(_selectedTestNode);
            }
        }
        AppendLog(bindCurrentStep
            ? $"已将“{copy.Name}”保存为资源，并将测试步骤明确绑定到资源 [{item.Id}]。"
            : $"已将“{copy.Name}”另存为资源。测试用例中的原步骤保持本步骤独立配置。可在后续用例中选择该资源复用。");
        RefreshLiveResourceReferences(_selectedTestNode);
        _ = SaveImportedConfigurationAsync();
    }

    private void CloseStepEditor()
    {
        HideInspector();
        StepEditor.Content = null;
        _propertyStep = null;
        _propertyAnchorStep = null;
        _propertyTestNodeId = null;
        BackToReferenceButton.Visibility = Visibility.Collapsed;
    }

    private TestFlowNode? ResolvePropertyTestNode()
    {
        if (string.IsNullOrWhiteSpace(_propertyTestNodeId)) return null;
        foreach (var testCase in _configuration.TestPlan.Cases)
        {
            var node = FindTestFlowNode(testCase.Flow, _propertyTestNodeId);
            if (node is not null) return node;
        }
        return null;
    }

    private static TreeViewItem? FindStepContainer(ItemsControl parent, ConfigurationStep? target)
    {
        if (target is null) return null;
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(container.DataContext, target)) return container;
            var found = FindStepContainer(container, target);
            if (found is not null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindItemContainer(ItemsControl parent, object? target)
    {
        if (target is null) return null;
        parent.UpdateLayout();
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(container.DataContext, target)) return container;
            var found = FindItemContainer(container, target);
            if (found is not null) return found;
        }
        return null;
    }

    private UIElement? FindStepEditorAnchor(ConfigurationStep? step)
    {
        var container = FindStepContainer(StepsTree, step);
        if (container is null) return null;
        return FindVisualElement(container, "StepNameText") ?? container;
    }

    private static UIElement? FindVisualElement(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement element && element.Name == name) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var found = FindVisualElement(VisualTreeHelper.GetChild(parent, i), name);
            if (found is not null) return found;
        }
        return null;
    }

    private void BackToReference_Click(object sender, RoutedEventArgs e)
    {
        if (_propertyAnchorStep is not null) ShowEditor(_propertyAnchorStep);
    }

    private void ShowNestedEditor(ConfigurationStep step, UIElement anchor)
    {
        _propertyStep = step;
        _propertyAnchorStep = step;
        InspectorTitle.Text = step.Name;
        BackToReferenceButton.Visibility = Visibility.Collapsed;
        StepEditor.Content = BuildEditorPanel(step, x => InspectorTitle.Text = x, false);
        OpenInspector();
    }

    private void Window_StateChanged(object? sender, EventArgs e) { }

    // 隐藏/最小化只是窗口生命周期变化，不应丢弃当前 Inspector 编辑上下文。
    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { }

    private void CloseInspectorPopup_Click(object sender, RoutedEventArgs e)
    {
        CloseStepEditor();
        e.Handled = true;
    }

    private bool IsAnyPropertyComboOpen()
    {
        foreach (var combo in FindVisualChildren<ComboBox>(this))
            if (combo.IsDropDownOpen) return true;
        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void BuildReferencePreview(Panel panel, ConfigurationStep referenceStep)
    {
        var target = _configuration.FindSequence(referenceStep.Reference.SequenceId);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock { Text = target is null ? "资源未找到" : $"资源：{target.Name}", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray });
        if (target is null)
        {
            panel.Children.Add(new TextBlock { Text = "请重新选择资源。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 0) });
            return;
        }

        var list = new ListBox { ItemsSource = target.Steps, Height = 150, Margin = new Thickness(0, 6, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 240)) };
        var template = new DataTemplate();
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.MarginProperty, new Thickness(4, 3, 4, 3));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ConfigurationStep.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var summary = new FrameworkElementFactory(typeof(TextBlock));
        summary.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ConfigurationStep.Summary)));
        summary.SetValue(TextBlock.ForegroundProperty, Brushes.SlateGray);
        summary.SetValue(TextBlock.FontSizeProperty, 11.0);
        root.AppendChild(name); root.AppendChild(summary); template.VisualTree = root; list.ItemTemplate = template;
        list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: ConfigurationStep selected } item) return;
            list.SelectedItem = selected;
            ShowNestedEditor(selected, item);
            e.Handled = true;
        };
        panel.Children.Add(list);
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private UIElement Field(string label, string value, Action<string> changed, Action? committed = null)
    {
        var box = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        // 不要在每次按键时刷新步骤树：Items.Refresh 会重建卡片并抢走输入框焦点，
        // 造成文本只能输入一个字符。输入持续写回模型，失焦后再刷新摘要。
        box.TextChanged += (_, _) => changed(box.Text);
        box.LostFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            committed?.Invoke();
            RefreshStepSummary();
        }, DispatcherPriority.Background);
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = label
        };
        row.Children.Add(labelText);
        Grid.SetColumn(box, 1); row.Children.Add(box); return row;
    }

    private void SyncAtomicTreeName(ConfigurationStep step)
    {
        if (_selectedItem?.Kind != ConfigurationItemKind.原子操作 || _selectedItem.Sequence is null)
            return;
        if (_selectedItem.Sequence.Steps.Count != 1 || !ReferenceEquals(_selectedItem.Sequence.Steps[0], step))
            return;

        _selectedItem.Name = step.Name;
        _selectedItem.Sequence.Name = step.Name;
        if (ConfigurationTree.SelectedItem is TreeViewItem treeItem)
            treeItem.Header = BuildTreeHeader(_selectedItem);
        RefreshLiveResourceReferences(_selectedTestNode);
    }

    private void SyncReferencedResourceName(ConfigurationSequence sequence, ConfigurationStep step)
    {
        var item = FindConfigurationItemBySequence(_configuration.Root, sequence.Id);
        if (item?.Kind != ConfigurationItemKind.原子操作 || item.Sequence?.Steps.Count != 1)
            return;

        item.Name = step.Name;
        sequence.Name = step.Name;
        if (FindTreeItem(ConfigurationTree, item.Id) is { } treeItem)
            treeItem.Header = BuildTreeHeader(item);
        RefreshLiveResourceReferences(_selectedTestNode);
    }

    private static ConfigurationTreeItem? FindConfigurationItemBySequence(ConfigurationTreeItem item, string sequenceId)
    {
        if (item.Sequence?.Id == sequenceId) return item;
        foreach (var child in item.Children)
        {
            var found = FindConfigurationItemBySequence(child, sequenceId);
            if (found is not null) return found;
        }
        return null;
    }

    private static ConfigurationSequence? FindSequenceContainingStep(ConfigurationTreeItem item, string stepId)
    {
        if (item.Sequence is { } sequence && sequence.Steps.Any(step => step.Id == stepId || ContainsStep(step, stepId)))
            return sequence;
        foreach (var child in item.Children)
        {
            var found = FindSequenceContainingStep(child, stepId);
            if (found is not null) return found;
        }
        return null;
    }

    private static int Number(string text, int fallback = 0) => int.TryParse(text, out var x) ? x : fallback;
    private static double DoubleNumber(string text, double fallback = 0) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : fallback;
    private static uint Hex(string text, uint fallback = 0) => uint.TryParse(text.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var x) ? x : fallback;
    private bool IsDatabaseBinding(ConfigurationStep step, CanMessageDefinition message) =>
        step.Kind == ConfigurationStepKind.广播报文 &&
        step.Broadcast.Source == ConfigurationSource.通信数据库 &&
        string.Equals(step.Broadcast.MessageDefinitionId, message.DefinitionId, StringComparison.Ordinal);

    private ConfigurationStep? FindConfigurationStep(string id)
        => AllConfigurationSteps().FirstOrDefault(step => string.Equals(step.Id, id, StringComparison.Ordinal));

    private static TextBlock BuildSourceStatus(ConfigurationStep step)
    {
        var database = step.Kind == ConfigurationStepKind.广播报文
            ? step.Broadcast.Source == ConfigurationSource.通信数据库
            : step.Parse.Source == ConfigurationSource.通信数据库;
        var missing = step.Kind == ConfigurationStepKind.广播报文
            ? database && string.IsNullOrWhiteSpace(step.Broadcast.MessageDefinitionId)
            : database && (string.IsNullOrWhiteSpace(step.Parse.MessageDefinitionId) || string.IsNullOrWhiteSpace(step.Parse.SignalDefinitionId));
        var text = !database ? "独立配置" : missing ? "通信数据库 · 定义不可用" : "通信数据库";
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = missing ? Brushes.Firebrick : Brushes.SlateGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = text
        };
    }

    private void BuildBroadcast(Panel p, ConfigurationStep s)
    {
        var database = _configuration.CommunicationDatabase;
        var rawDataAdded = false;
        database?.Normalize();
        var hasDatabaseBinding = s.Broadcast.Source == ConfigurationSource.通信数据库;
        var boundMessage = database?.Messages.FirstOrDefault(message => IsDatabaseBinding(s, message));
        if (database is { Messages.Count: > 0 })
        {
            var messages = database.Messages.OrderBy(x => x.Id).ToList();
            var choices = new List<DefinitionChoice> { new("本步骤独立配置", null) };
            var messageList = new ComboBox { ItemTemplate = DatabaseChoiceTemplate(), MaxDropDownHeight = 260, IsTextSearchEnabled = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            TextSearch.SetTextPath(messageList, nameof(DefinitionChoice.DisplayName));
            choices.AddRange(messages.Select(x => new DefinitionChoice(CanDefinitionDisplay.FormatMessage(x), x, CanDefinitionDisplay.FullDescription(x))));
            messageList.ItemsSource = choices;
            messageList.SelectedItem = boundMessage is null
                ? (hasDatabaseBinding ? null : choices[0])
                : choices.FirstOrDefault(x => ReferenceEquals(x.Value, boundMessage));
            messageList.ToolTip = "选择通信数据库报文；公共定义请在通信数据库窗口中搜索和编辑";
            ConstrainComboDropDown(messageList);
            messageList.SelectionChanged += (_, _) =>
            {
                if (messageList.SelectedItem is not DefinitionChoice choice) return;
                if (choice.Value is null)
                {
                    s.Broadcast.Source = ConfigurationSource.独立配置;
                    s.Broadcast.MessageDefinitionId = "";
                    RebuildInspectorContentKeepingViewport(s);
                    return;
                }
                var selected = choice.Value;
                if (_rebuildingBroadcastEditor) return;
                _rebuildingBroadcastEditor = true;
                try
                {
                    _definitionBindingService.BindBroadcast(s, selected);
                    RememberDefinition(selected.DefinitionId);
                    foreach (var signal in selected.Signals)
                        if (!s.Broadcast.SignalValues.ContainsKey(signal.Name)) s.Broadcast.SignalValues[signal.Name] = "0";
                    s.Broadcast.DataHex = CanPayloadCodec.Encode(selected, s.Broadcast.SignalValues);
                    RefreshStepSummary();
                    RebuildInspectorContentKeepingViewport(s);
                }
                finally { _rebuildingBroadcastEditor = false; }
            };
            p.Children.Add(LabeledControl("报文定义", messageList));
            var selectedMessage = boundMessage;
            if (selectedMessage is not null)
            {
                // 新建广播步骤时先给每个定义信号一个可见的 0 初值，避免空输入框造成误解。
                foreach (var signal in selectedMessage.Signals)
                    if (!s.Broadcast.SignalValues.ContainsKey(signal.Name)) s.Broadcast.SignalValues[signal.Name] = "0";
                if (string.IsNullOrWhiteSpace(s.Broadcast.DataHex) || IsAllZeroData(s.Broadcast.DataHex))
                    s.Broadcast.DataHex = CanPayloadCodec.Encode(selectedMessage, s.Broadcast.SignalValues);
                TextBox? rawBox = null;
                p.Children.Add(BuildBroadcastMessageSummary(selectedMessage));
                p.Children.Add(BuildBroadcastSignalGrid(selectedMessage, s, data =>
                {
                    if (rawBox is not null && !rawBox.IsKeyboardFocusWithin) rawBox.Text = data;
                }));
                p.Children.Add(new TextBlock { Text = "生成数据（可直接编辑全部字节）", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 10, 0, 0) });
                rawBox = new TextBox { Text = s.Broadcast.DataHex, HorizontalAlignment = HorizontalAlignment.Stretch };
                rawBox.LostFocus += (_, _) =>
                {
                    s.Broadcast.DataHex = rawBox.Text;
                    var decoded = CanPayloadCodec.Decode(selectedMessage, rawBox.Text);
                    foreach (var item in decoded) s.Broadcast.SignalValues[item.Key] = item.Value;
                    // 原始数据和信号值必须双向同步；只在提交后重建当前检查器，
                    // 并保持滚动位置，不干扰正在输入的字符。
                    RefreshStepSummary();
                    RebuildInspectorContentKeepingViewport(s);
                };
                rawBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) { rawBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
                };
                p.Children.Add(LabeledControl("数据", rawBox));
                rawDataAdded = true;
            }
        }
        p.Children.Add(Field("报文 ID", $"0x{s.Broadcast.Id:X}", x => { s.Broadcast.Id = Hex(x); }));
        if (!rawDataAdded) p.Children.Add(Field("数据", s.Broadcast.DataHex, x => { s.Broadcast.DataHex = x; }, RefreshStepSummary));
        p.Children.Add(Field("周期 ms", s.Broadcast.PeriodMs.ToString(), x => { s.Broadcast.PeriodMs = Number(x, 100); }));
        var mode = new ComboBox { ItemsSource = Enum.GetValues<BroadcastMode>(), SelectedItem = s.Broadcast.Mode, HorizontalAlignment = HorizontalAlignment.Stretch }; ConstrainComboDropDown(mode); mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is BroadcastMode value) { s.Broadcast.Mode = value; RefreshStepSummary(); } };
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.Children.Add(new TextBlock { Text = "发送方式", VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(mode, 1); row.Children.Add(mode); p.Children.Add(row);
    }

    private static bool IsAllZeroData(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var bytes = text.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return bytes.Length > 0 && bytes.All(x => byte.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value == 0);
    }

    private static Border BuildBroadcastMessageSummary(CanMessageDefinition message)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var title = new TextBlock { Text = $"{message.Name} · 0x{message.Id:X}", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateBlue };
        var detail = new TextBlock { Text = $"{(message.Extended ? "扩展帧" : "标准帧")}  ·  DLC {message.Dlc}  ·  周期 {(message.CycleTimeMs is > 0 ? $"{message.CycleTimeMs} ms" : "未定义")}  ·  {message.Signals.Count} 个信号", Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumnSpan(title, 2); grid.Children.Add(title);
        Grid.SetRow(detail, 1); Grid.SetColumnSpan(detail, 2); grid.Children.Add(detail);
        return new Border { Child = grid, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 7, 0, 0), Background = new SolidColorBrush(Color.FromRgb(244, 248, 253)), BorderBrush = new SolidColorBrush(Color.FromRgb(205, 220, 238)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
    }

    private Border BuildBroadcastSignalGrid(CanMessageDefinition message, ConfigurationStep step, Action<string>? dataChanged = null)
    {
        var table = new Grid { Margin = new Thickness(0, 8, 0, 0), MinHeight = 100 };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.3, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        var header = new[] { "信号（中文）", "本次值", "单位", "位范围", "属性" };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < header.Length; c++)
        {
            var cell = new TextBlock { Text = header[c], FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Padding = new Thickness(6, 5, 4, 5), Background = new SolidColorBrush(Color.FromRgb(238, 244, 250)) };
            Grid.SetColumn(cell, c); table.Children.Add(cell);
        }
        foreach (var signal in message.Signals)
        {
            var row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var display = CanDefinitionDisplay.SignalPrimaryName(signal);
            var label = new TextBlock { Text = display, ToolTip = CanDefinitionDisplay.SignalTooltip(signal), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 5, 4, 5) };
            Grid.SetRow(label, row); Grid.SetColumn(label, 0); table.Children.Add(label);
            var initial = step.Broadcast.SignalValues.GetValueOrDefault(signal.Name, "0");
            Control editor;
            if (signal.ValueTable.Count > 0)
            {
                var combo = new ComboBox { ItemsSource = signal.ValueTable.OrderBy(x => x.Key).Select(x => $"{x.Key} · {x.Value}"), MaxDropDownHeight = 240, Margin = new Thickness(2) };
                combo.IsEditable = false;
                combo.SelectedItem = signal.ValueTable.TryGetValue(Number(initial), out var currentLabel)
                    ? $"{Number(initial)} · {currentLabel}"
                    : signal.ValueTable.OrderBy(x => x.Key).Select(x => $"{x.Key} · {x.Value}").FirstOrDefault();
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string text) { var raw = text.Split('·')[0].Trim(); step.Broadcast.SignalValues[signal.Name] = raw; step.Broadcast.DataHex = CanPayloadCodec.Encode(message, step.Broadcast.SignalValues); dataChanged?.Invoke(step.Broadcast.DataHex); RefreshStepSummary(); } };
                ConstrainComboDropDown(combo); editor = combo;
            }
            else
            {
                var box = new TextBox { Text = initial, Margin = new Thickness(2), Padding = new Thickness(4, 2, 4, 2) };
                box.LostFocus += (_, _) => { step.Broadcast.SignalValues[signal.Name] = box.Text; step.Broadcast.DataHex = CanPayloadCodec.Encode(message, step.Broadcast.SignalValues); dataChanged?.Invoke(step.Broadcast.DataHex); RefreshStepSummary(); };
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { step.Broadcast.SignalValues[signal.Name] = box.Text; step.Broadcast.DataHex = CanPayloadCodec.Encode(message, step.Broadcast.SignalValues); dataChanged?.Invoke(step.Broadcast.DataHex); RefreshStepSummary(); e.Handled = true; } };
                editor = box;
            }
            Grid.SetRow(editor, row); Grid.SetColumn(editor, 1); table.Children.Add(editor);
            var unit = new TextBlock { Text = string.IsNullOrWhiteSpace(signal.Unit) ? "—" : signal.Unit, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 5, 4, 5) };
            Grid.SetRow(unit, row); Grid.SetColumn(unit, 2); table.Children.Add(unit);
            var range = new TextBlock { Text = $"字节{signal.StartByte + 1}·位{signal.StartBitInByte + 1} / {signal.BitLength}b", Foreground = Brushes.SlateGray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 5, 4, 5) };
            Grid.SetRow(range, row); Grid.SetColumn(range, 3); table.Children.Add(range);
            var attr = new TextBlock { Text = signal.ValueTable.Count > 0 ? "枚举" : (signal.Factor == 1 && signal.Offset == 0 ? "数值" : $"×{signal.Factor:g}"), Foreground = Brushes.SlateGray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 5, 4, 5) };
            Grid.SetRow(attr, row); Grid.SetColumn(attr, 4); table.Children.Add(attr);
        }
        // 信号很多时只让信号区内部滚动；不再手工劫持滚轮事件。
        // WPF 会在内部滚动范围结束后正常路由给外层检查器。
        var scroll = new ScrollViewer
        {
            Content = table,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 310,
            MinHeight = 140,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false
        };
        return new Border { Child = scroll, BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 232)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 2, 0, 0) };
    }

    private void BuildParse(Panel p, ConfigurationStep s)
    {
        var database = _configuration.CommunicationDatabase;
        database?.Normalize();
        var selectionSection = InspectorSection("当前报文与信号");
        var databaseDefinitionMissing = false;
        p.Children.Add(selectionSection);
        if (s.Parse.Source == ConfigurationSource.通信数据库)
        {
            if (database is null || database.Messages.Count == 0)
            {
                databaseDefinitionMissing = true;
                selectionSection.Children.Add(ReadOnlyField("数据库定义", "不可用 · 未加载通信数据库", true));
            }
            else
            {
                var selection = ParseDefinitionSelectionState.Resolve(s.Parse, database);
                databaseDefinitionMissing = selection.DefinitionMissing;
                if (selection.Message is not null)
                {
                    ParseDefinitionSelectionState.SynchronizeMessageProjection(s.Parse, selection.Message);
                    selectionSection.Children.Add(ReadOnlyField("报文", CanDefinitionDisplay.FormatMessage(selection.Message)));
                    var signal = selection.Signal;
                    selectionSection.Children.Add(ReadOnlyField("信号", signal is null
                        ? "定义不可用或未选择"
                        : CanDefinitionDisplay.SignalPrimaryName(signal)));
                    if (signal is not null)
                    {
                        selectionSection.Children.Add(ReadOnlyField("信号信息",
                            $"{CanDefinitionDisplay.SignalNamesAndDescription(signal)}\n{CanDefinitionDisplay.SignalPosition(signal)}"));
                    }
                }
                else
                {
                    databaseDefinitionMissing = true;
                    selectionSection.Children.Add(ReadOnlyField("数据库定义", "不可用 · 报文定义丢失", true));
                }
            }
        }
        else
        {
            selectionSection.Children.Add(ReadOnlyField("来源", "本步骤独立配置"));
        }

        if (database is { Messages.Count: > 0 })
        {
            var messages = new List<DefinitionChoice> { new("本步骤独立配置", null) };
            messages.AddRange(database.Messages.OrderBy(x => x.Id)
                .Select(x => new DefinitionChoice(CanDefinitionDisplay.FormatMessage(x), x, CanDefinitionDisplay.FullDescription(x))));
            var messageList = new ComboBox
            {
                ItemsSource = messages,
                ItemTemplate = DatabaseChoiceTemplate(),
                IsTextSearchEnabled = true,
                MaxDropDownHeight = 260,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "选择当前解析步骤使用的通信数据库报文"
            };
            ConstrainComboDropDown(messageList);
            var currentMessage = s.Parse.Source == ConfigurationSource.通信数据库
                ? database.Messages.FirstOrDefault(x => x.DefinitionId == s.Parse.MessageDefinitionId)
                : null;
            messageList.SelectedItem = messages.FirstOrDefault(x => ReferenceEquals(x.Value, currentMessage)) ?? messages[0];

            var signalList = new ComboBox
            {
                ItemTemplate = DatabaseChoiceTemplate(),
                IsTextSearchEnabled = true,
                MaxDropDownHeight = 260,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "只显示当前报文的信号定义"
            };
            ConstrainComboDropDown(signalList);
            void RefreshSignals(CanMessageDefinition? message)
            {
                var choices = message?.Signals.Select(signal => new DefinitionChoice(
                    CanDefinitionDisplay.FormatSignalCandidate(signal), message, CanDefinitionDisplay.SignalTooltip(signal), signal)).ToList()
                    ?? [];
                signalList.ItemsSource = choices;
                signalList.SelectedItem = choices.FirstOrDefault(x => x.Signal?.DefinitionId == s.Parse.SignalDefinitionId);
            }
            RefreshSignals(currentMessage);
            messageList.SelectionChanged += (_, _) =>
            {
                if (messageList.SelectedItem is not DefinitionChoice choice) return;
                if (choice.Value is null)
                {
                    s.Parse.Source = ConfigurationSource.独立配置;
                    s.Parse.MessageDefinitionId = "";
                    s.Parse.SignalDefinitionId = "";
                    RefreshSignals(null);
                    s.NotifySummaryChanged();
                    RefreshStepSummary();
                    RebuildInspectorContentKeepingViewport(s);
                    return;
                }

                ParseDefinitionSelectionState.BindMessage(s.Parse, choice.Value);
                RefreshSignals(choice.Value);
                s.NotifySummaryChanged();
                RefreshStepSummary();
                RebuildInspectorContentKeepingViewport(s);
            };
            signalList.SelectionChanged += (_, _) =>
            {
                if (signalList.SelectedItem is not DefinitionChoice { Value: { } message, Signal: { } signal }) return;
                if (!string.Equals(message.DefinitionId, s.Parse.MessageDefinitionId, StringComparison.Ordinal)) return;
                ParseDefinitionSelectionState.BindSignal(s.Parse, message, signal);
                _definitionBindingService.BindParse(s, message, signal);
                RememberDefinition($"{message.DefinitionId}/{signal.DefinitionId}");
                s.NotifySummaryChanged();
                RefreshStepSummary();
                RebuildInspectorContentKeepingViewport(s);
            };
            selectionSection.Children.Add(LabeledControl("选择报文", messageList));
            selectionSection.Children.Add(LabeledControl("选择信号", signalList));
        }
        var parameterSection = InspectorSection("解析参数");
        p.Children.Add(parameterSection);
        var advancedGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
        advancedGrid.Children.Add(Field("报文 ID", $"0x{s.Parse.Id:X}", x => { s.Parse.Id = Hex(x); }));
        var frameType = new ComboBox
        {
            ItemsSource = new[] { "标准帧", "扩展帧" },
            SelectedIndex = s.Parse.Extended ? 1 : 0,
            IsTextSearchEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ConstrainComboDropDown(frameType);
        frameType.SelectionChanged += (_, _) =>
        {
            s.Parse.Extended = frameType.SelectedIndex == 1;
            RefreshStepSummary();
        };
        advancedGrid.Children.Add(LabeledControl("帧类型", frameType));
        advancedGrid.Children.Add(Field("起始字节", s.Parse.StartByte.ToString(), x => s.Parse.StartByte = Number(x)));
        advancedGrid.Children.Add(Field("起始位", s.Parse.StartBit.ToString(), x => s.Parse.StartBit = Number(x)));
        advancedGrid.Children.Add(Field("位长度", s.Parse.BitLength.ToString(), x => s.Parse.BitLength = Number(x, 8)));
        var byteOrder = new ComboBox
        {
            ItemsSource = new[] { "小端序（Intel）", "大端序（Motorola）" },
            SelectedItem = CanByteOrderDisplay.FromChinese(s.Parse.ByteOrder) == CanByteOrder.Motorola
                ? "大端序（Motorola）"
                : "小端序（Intel）",
            IsTextSearchEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ConstrainComboDropDown(byteOrder);
        byteOrder.SelectionChanged += (_, _) =>
        {
            s.Parse.ByteOrder = byteOrder.SelectedItem?.ToString()?.StartsWith("大端序", StringComparison.Ordinal) == true
                ? "Motorola"
                : "Intel";
            RefreshStepSummary();
        };
        advancedGrid.Children.Add(LabeledControl("字节序", byteOrder));
        advancedGrid.Children.Add(Field("换算系数", s.Parse.Factor.ToString(CultureInfo.InvariantCulture), x => s.Parse.Factor = DoubleNumber(x, 1)));
        advancedGrid.Children.Add(Field("偏移", s.Parse.Offset.ToString(CultureInfo.InvariantCulture), x => s.Parse.Offset = DoubleNumber(x, 0)));
        advancedGrid.Children.Add(Field("单位", s.Parse.Unit, x => s.Parse.Unit = x));
        advancedGrid.Children.Add(Field("显示方式", s.Parse.DisplayMode, x => s.Parse.DisplayMode = x));
        advancedGrid.Children.Add(Field("报警类型", s.Parse.AlarmType, x => s.Parse.AlarmType = x));
        advancedGrid.Children.Add(Field("信号描述", s.Parse.Description, x => s.Parse.Description = x));
        var advanced = new Expander
        {
            Header = s.Parse.Source == ConfigurationSource.通信数据库 ? "展开编辑解析参数" : "解析参数明细",
            IsExpanded = s.Parse.Source != ConfigurationSource.通信数据库,
            Content = advancedGrid,
            Margin = new Thickness(0, 3, 0, 0)
        };
        if (databaseDefinitionMissing)
        {
            advanced.IsEnabled = false;
            advanced.ToolTip = "通信数据库定义不可用，请先重新选择报文和信号";
        }
        parameterSection.Children.Add(advanced);
    }
    private List<ParseSignalDefinition> AvailableParseDefinitions()
    {
        return (_configuration.CommunicationDatabase?.Messages ?? [])
            .SelectMany(message => message.Signals.Select(signal => new ParseSignalDefinition
            {
                MessageName = message.Name,
                MessageDefinitionId = message.DefinitionId,
                SignalDefinitionId = signal.DefinitionId,
                Name = signal.Name,
                DisplayName = CanDefinitionDisplay.SignalPrimaryName(signal),
                Id = message.Id,
                Extended = message.Extended,
                StartByte = signal.StartBit / 8,
                StartBit = signal.StartBit % 8,
                BitLength = signal.BitLength,
                ByteOrder = signal.ByteOrder.ToChinese(),
                Factor = signal.Factor,
                Offset = signal.Offset,
                Unit = signal.Unit,
                DisplayMode = string.IsNullOrWhiteSpace(signal.DisplayMode) ? "数值" : signal.DisplayMode,
                AlarmType = signal.AlarmType,
                Description = string.IsNullOrWhiteSpace(signal.Description) ? signal.Comment : signal.Description
            }))
            .ToList();
    }

    private ParseDefinitionSet BuildParseDefinitionSetFromDatabase()
    {
        var set = new ParseDefinitionSet
        {
            Name = _configuration.CommunicationDatabase?.Name ?? "报文解析定义",
            SourcePath = _configuration.CommunicationDatabase?.SourcePath ?? ""
        };
        set.Signals.AddRange(AvailableParseDefinitions());
        return set;
    }

    private void MergeParseDefinitionsIntoDatabase(ParseDefinitionSet imported)
    {
        var database = _configuration.CommunicationDatabase ??= new CanDatabase { Name = imported.Name };
        foreach (var source in imported.Signals)
        {
            var message = database.GetOrAddMessage(source.Id, source.Extended);
            message.Dlc = Math.Max(message.Dlc, Math.Clamp(source.StartByte + 1, 1, 64));
            var signal = message.Signals.FirstOrDefault(x => string.Equals(x.Name, source.Name, StringComparison.OrdinalIgnoreCase))
                ?? message.Signals.FirstOrDefault(x => x.StartBit == source.StartByte * 8 + source.StartBit && x.BitLength == source.BitLength);
            signal ??= new CanSignalDefinition();
            signal.Name = source.Name;
            signal.StartBit = source.StartByte * 8 + source.StartBit;
            signal.BitLength = source.BitLength;
            signal.ByteOrder = CanByteOrderDisplay.FromChinese(source.ByteOrder);
            signal.Factor = source.Factor; signal.Offset = source.Offset; signal.Unit = source.Unit;
            signal.DisplayName = source.DisplayName ?? "";
            signal.DisplayMode = string.IsNullOrWhiteSpace(source.DisplayMode) ? "数值" : source.DisplayMode;
            signal.AlarmType = source.AlarmType;
            signal.Description = source.Description ?? "";
            signal.Comment = source.Description ?? "";
            if (!message.Signals.Contains(signal)) message.Signals.Add(signal);
        }
        foreach (var diagnostic in database.Normalize()) AppendLog($"通信数据库提示：{diagnostic}");
    }

    private void BuildPower(Panel p, ConfigurationStep s)
    {
        // 选择电源对象时只更新当前步骤模型。这里不能刷新整棵步骤树，
        // 否则编辑弹窗会被销毁并重建，用户刚选中的 IGN/按键板电源会看起来像没有生效。
        var target = new ComboBox
        {
            ItemsSource = Enum.GetValues<PowerTarget>(),
            SelectedIndex = (int)s.Power.Target,
            IsTextSearchEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ConstrainComboDropDown(target);
        void StorePowerTarget()
        {
            if (target.SelectedIndex >= 0 && target.SelectedIndex < Enum.GetValues<PowerTarget>().Length)
            {
                s.Power.Target = (PowerTarget)target.SelectedIndex;
                s.NotifySummaryChanged();
            }
        }
        target.SelectionChanged += (_, _) => StorePowerTarget();
        target.DropDownClosed += (_, _) => StorePowerTarget();
        var on = new CheckBox { Content = "打开", IsChecked = s.Power.On, Margin = new Thickness(12, 0, 0, 0) }; on.Checked += (_, _) => { s.Power.On = true; s.NotifySummaryChanged(); }; on.Unchecked += (_, _) => { s.Power.On = false; s.NotifySummaryChanged(); };
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(new TextBlock { Text = "控制对象", VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(target, 1); row.Children.Add(target); Grid.SetColumn(on, 2); row.Children.Add(on); p.Children.Add(row);
    }
    private void BuildAction(Panel p, ConfigurationStep s)
    {
        var grid = new UniformGrid { Columns = 5 };
        for (var i = 0; i < 20; i++) { var bit = i; var check = new CheckBox { Content = $"K{i + 1}", Margin = new Thickness(4), IsChecked = (s.Action.OutputMask & (1u << i)) != 0 }; check.Checked += (_, _) => { s.Action.OutputMask |= 1u << bit; RefreshStepSummary(); }; check.Unchecked += (_, _) => { s.Action.OutputMask &= ~(1u << bit); RefreshStepSummary(); }; grid.Children.Add(check); }
        p.Children.Add(grid);
    }
    private void BuildWait(Panel p, ConfigurationStep s) => p.Children.Add(Field("等待 ms", s.Wait.DurationMs.ToString(), x => { s.Wait.DurationMs = Number(x, 1000); }));
    private void BuildSequenceReference(Panel p, ConfigurationStep s)
    {
        var choices = AllSequences()
            .Where(x => x.Id != _selectedSequence?.Id)
            .Select(x => new SequenceChoice(x.Id, x.Name))
            .ToList();
        if (choices.Count == 0)
        {
            p.Children.Add(new TextBlock { Text = "没有可引用对象。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 8, 0, 0) });
            return;
        }
        var selectedIndex = choices.FindIndex(x => x.Id == s.Reference.SequenceId);
        var selector = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = selectedIndex,
            DisplayMemberPath = nameof(SequenceChoice.Name),
            IsTextSearchEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ConstrainComboDropDown(selector);
        void StoreReference()
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= choices.Count) return;
            var choice = choices[selector.SelectedIndex];
            s.Reference.SequenceId = choice.Id;
            if (s.Name is "引用序列" or "调用序列" or "引用")
                s.Name = choice.Name;
        }
        selector.SelectionChanged += (_, _) => StoreReference();
        selector.DropDownClosed += (_, _) => StoreReference();
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = "选择资源", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(selector, 1); row.Children.Add(selector);
        p.Children.Add(row);
    }

    private void RefreshStepSummary()
    {
        // 编辑属性时保持当前视觉树和停靠检查器不变；ConfigurationStep/TestFlowNode
        // 都会通过属性通知更新摘要。只有结构操作（添加、删除、排序）才调用
        // RefreshStepsView/Items.Refresh，它们会重建树容器。
        var propertyEditorOpen = InspectorHost?.Visibility == Visibility.Visible;
        NotifyVisibleStepSummaries();
        if (StepsTree is not null && !propertyEditorOpen) RefreshStepsView();
        RefreshTestFlowSummaries();
        if (TestFlowTree is not null && !propertyEditorOpen) RefreshTestFlowTreePreservingSelection();
    }

    private void NotifyVisibleStepSummaries()
    {
        foreach (var sequence in AllSequences())
            NotifyStepSummaries(sequence.Steps);
    }

    private static void NotifyStepSummaries(IEnumerable<ConfigurationStep> steps)
    {
        foreach (var step in steps)
        {
            step.NotifySummaryChanged();
            NotifyStepSummaries(step.Children);
        }
    }

    private IEnumerable<ConfigurationSequence> AllSequences() => AllSequences(_configuration.Root);
    private static IEnumerable<ConfigurationSequence> AllSequences(ConfigurationTreeItem item)
    {
        if (item.Kind is (ConfigurationItemKind.序列 or ConfigurationItemKind.原子操作) && item.Sequence is not null) yield return item.Sequence;
        foreach (var child in item.Children)
            foreach (var sequence in AllSequences(child)) yield return sequence;
    }

    private IEnumerable<ResourceChoice> AllResources() => AllResources(_configuration.Root);

    private static IEnumerable<ResourceChoice> AllResources(ConfigurationTreeItem item)
    {
        if (item.Kind is (ConfigurationItemKind.序列 or ConfigurationItemKind.原子操作) && item.Sequence is { } sequence)
        {
            // 资源选择不暴露后台分类；用户只需要选择一个已经配置好的资源。
            var kind = "资源";
            yield return new ResourceChoice(item.Id, sequence.Id, item.Name, kind, ReferenceSummaryResolver.Resolve(sequence), sequence);
        }
        foreach (var child in item.Children)
            foreach (var resource in AllResources(child)) yield return resource;
    }

    private ConfigurationSequence? ResolveResource(TestFlowNode node)
        => new ResourceRelationService().Resolve(_configuration, node).Sequence;

    private IEnumerable<ParseResourceChoice> AllParseResources() => ParseResources(_configuration.Root);

    private static IEnumerable<ParseResourceChoice> ParseResources(ConfigurationTreeItem item)
    {
        if (item.Sequence is { } sequence)
        {
            foreach (var step in sequence.Steps)
                foreach (var resource in ParseResources(step)) yield return resource;
        }
        foreach (var child in item.Children)
            foreach (var resource in ParseResources(child)) yield return resource;
    }

    private static IEnumerable<ParseResourceChoice> ParseResources(ConfigurationStep step)
    {
        if (step.Kind == ConfigurationStepKind.报文解析)
            yield return new ParseResourceChoice(step.Id, $"{step.Name} · 0x{step.Parse.Id:X}", step.Summary);
        foreach (var child in step.Children)
            foreach (var resource in ParseResources(child)) yield return resource;
    }

    private static Grid LabeledControl(string label, Control control)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = label
        };
        row.Children.Add(labelText);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Grid ReadOnlyField(string label, string value, bool warning = false)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = label
        });
        var valueText = new TextBlock
        {
            Text = value,
            Foreground = warning ? Brushes.Firebrick : Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static DataTemplate DatabaseChoiceTemplate()
    {
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
        text.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("ToolTip"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.MarginProperty, new Thickness(4, 3, 4, 3));
        text.SetValue(TextBlock.MinHeightProperty, 20d);
        template.VisualTree = text;
        return template;
    }

    private void RememberDefinition(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _recentDefinitionKeys.Remove(key);
        _recentDefinitionKeys.AddFirst(key);
        while (_recentDefinitionKeys.Count > 12) _recentDefinitionKeys.RemoveLast();
    }

    private static bool MatchesSearch(string query, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static void ConstrainComboDropDown(ComboBox combo)
    {
        combo.MaxDropDownHeight = 280;
        combo.HorizontalContentAlignment = HorizontalAlignment.Left;
        void ApplyWidth()
        {
            if (combo.Template.FindName("PART_Popup", combo) is Popup popup)
            {
                // 下拉菜单的宽度只服从其触发控件，不允许长资源/信号名称按内容
                // 把独立 Popup 撑满整个窗口。给出合理的可读上限即可。
                var width = Math.Clamp(combo.ActualWidth, 160, 520);
                popup.PlacementTarget = combo;
                popup.Placement = PlacementMode.Bottom;
                popup.Width = width;
                popup.MinWidth = width;
                popup.MaxWidth = width;
                popup.MaxHeight = combo.MaxDropDownHeight;
                popup.HorizontalOffset = 0;

                // 默认 WPF ComboBox 模板的 Popup 子元素可能在打开后再次按内容测量，
                // 仅设置 Popup.Width 不足以限制 ItemsPresenter，长文本会把下拉层拉满窗口。
                if (popup.Child is FrameworkElement child)
                {
                    child.Width = width;
                    child.MinWidth = width;
                    child.MaxWidth = width;
                    child.MaxHeight = combo.MaxDropDownHeight;
                    child.HorizontalAlignment = HorizontalAlignment.Left;
                    if (FindVisualChild<ScrollViewer>(child) is { } scroll)
                    {
                        scroll.Width = width;
                        scroll.MinWidth = width;
                        scroll.MaxWidth = width;
                        scroll.MaxHeight = combo.MaxDropDownHeight;
                        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                }
            }
        }

        combo.DropDownOpened += (_, _) =>
        {
            ApplyWidth();
            // 模板和 ItemsPresenter 在 DropDownOpened 后还会进行一次布局，
            // 延后一帧再次应用，确保最终可见的弹出层仍保持控件宽度。
            combo.Dispatcher.BeginInvoke(ApplyWidth, DispatcherPriority.Loaded);
        };
    }

    private static void ConstrainMenuDropDown(MenuItem menuItem)
    {
        void ApplyBounds()
        {
            Popup? popup = null;
            try { popup = menuItem.Template.FindName("PART_Popup", menuItem) as Popup; } catch (InvalidOperationException) { }
            popup ??= FindVisualChild<Popup>(menuItem);
            if (popup is null) return;

            const double width = 380;
            const double height = 420;
            popup.Width = width;
            popup.MaxHeight = height;
            if (popup.Child is FrameworkElement child)
            {
                child.Width = width;
                child.MaxWidth = width;
                child.MaxHeight = height;
                child.HorizontalAlignment = HorizontalAlignment.Left;
                if (FindVisualChild<ScrollViewer>(child) is { } scroll)
                {
                    scroll.Width = width;
                    scroll.MaxHeight = height;
                    scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                }
            }
        }

        menuItem.SubmenuOpened += (_, _) =>
        {
            ApplyBounds();
            menuItem.Dispatcher.BeginInvoke(ApplyBounds, DispatcherPriority.Loaded);
        };
    }

    private static ConfigurationStep? FindConfigurationStep(ConfigurationTreeItem item, string id)
    {
        if (item.Sequence is { } sequence)
        {
            foreach (var step in sequence.Steps)
            {
                var found = FindConfigurationStep(step, id);
                if (found is not null) return found;
            }
        }
        foreach (var child in item.Children)
        {
            var found = FindConfigurationStep(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ConfigurationStep? FindConfigurationStep(ConfigurationStep step, string id)
    {
        if (step.Id == id) return step;
        foreach (var child in step.Children)
        {
            var found = FindConfigurationStep(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private async void RunStep_Click(object sender, RoutedEventArgs e) { if (_selectedStep is not null) await RunSelectedStepAsync(); }
    private async void RunSequence_Click(object sender, RoutedEventArgs e) { if (_selectedSequence is not null) await RunSelectedSequenceAsync(); }
    private bool BeginRun()
    {
        lock (_runStateGate)
        {
            if (_runActive)
            {
                AppendLog("已有执行正在进行，请先停止或等待当前执行结束。");
                return false;
            }
            _runActive = true;
            _runCts = new CancellationTokenSource();
        }
        UpdateRunUi(true);
        _timelineEntries.Clear();
        if (_selectedSequence is not null) ResetExecutionStates(_selectedSequence.Steps);
        return true;
    }
    private void EndRun()
    {
        lock (_runStateGate)
        {
            _runActive = false;
            _runCts?.Dispose();
            _runCts = null;
        }
        UpdateRunUi(false);
    }
    private void UpdateRunUi(bool running)
    {
        // 运行期间不允许再次启动；停止按钮始终表达当前全局运行状态。
        if (StopTestPlanButton is not null) StopTestPlanButton.IsEnabled = running;
        if (StopTestCaseButton is not null) StopTestCaseButton.IsEnabled = running;
        if (StopResourceButton is not null) StopResourceButton.IsEnabled = running;
        if (RunTestPlanButton is not null) RunTestPlanButton.IsEnabled = !running;
        if (RunTestCaseButton is not null) RunTestCaseButton.IsEnabled = !running;
        if (RunCurrentResourceButton is not null) RunCurrentResourceButton.IsEnabled = !running;
        if (RunAllResourceButton is not null) RunAllResourceButton.IsEnabled = !running;
        if (RunCurrentResourceHeaderButton is not null) RunCurrentResourceHeaderButton.IsEnabled = !running;
        if (RunAllResourceHeaderButton is not null) RunAllResourceHeaderButton.IsEnabled = !running;
    }
    private void StopRun_Click(object sender, RoutedEventArgs e)
    {
        lock (_runStateGate)
        {
            if (!_runActive) return;
            _runCts?.Cancel();
        }
        AppendLog("已请求停止当前执行。");
    }
    private static void ResetExecutionStates(IEnumerable<ConfigurationStep> steps)
    {
        foreach (var step in steps) { step.ExecutionState = StepExecutionState.未运行; ResetExecutionStates(step.Children); }
    }
    private async Task<bool> RunAsync(IEnumerable<ConfigurationStep> steps)
    {
        var selectedSteps = steps.ToList();
        var debugCase = new TestCaseDefinition { Name = "调试步骤" };
        foreach (var step in selectedSteps)
            debugCase.Flow.Add(new TestFlowNode { Name = step.Name, Kind = TestFlowNodeKind.原子操作, Operation = step });
        var plan = new TestPlan { Id = _configuration.TestPlan.Id, Name = "调试预检" };
        plan.Cases.Add(debugCase);
        var preflight = new ExecutionPreflightService().Build(_configuration, plan);
        if (!preflight.CanRun)
        {
            foreach (var diagnostic in preflight.Diagnostics)
                AppendLog($"调试预检失败：步骤={diagnostic.StepName}，DefinitionId={diagnostic.DefinitionId}，{diagnostic.Message}");
            return false;
        }
        return await RunAsync(selectedSteps, []);
    }
    private async Task<bool> RunAsync(IEnumerable<ConfigurationStep> steps, HashSet<string> callStack)
    {
        _runCts?.Token.ThrowIfCancellationRequested();
        var stepList = steps.ToList();
        for (var index = 0; index < stepList.Count; index++)
        {
            _runCts?.Token.ThrowIfCancellationRequested();
            var step = stepList[index];
            step.ExecutionState = StepExecutionState.运行中;
            _timelineEntries.Add(new RunTimelineRow(DateTime.Now.ToString("HH:mm:ss"), step.Name, step.Kind.ToString(), "运行中"));
            AppendLog($"执行 {step.Kind}：{step.Name}");
            if (step.Kind == ConfigurationStepKind.广播报文 && step.Broadcast.Mode == BroadcastMode.开始周期发送)
            {
                var group = TakeContiguousBroadcastGroup(stepList, index, BroadcastMode.开始周期发送);
                var ok = await StartPeriodicBroadcastAsync(group); foreach (var item in group) item.ExecutionState = ok ? StepExecutionState.成功 : StepExecutionState.错误;
                if (!ok) return false;
                index += group.Count - 1;
                continue;
            }
            if (step.Kind == ConfigurationStepKind.广播报文 && step.Broadcast.Mode == BroadcastMode.停止周期发送)
            {
                var group = TakeContiguousBroadcastGroup(stepList, index, BroadcastMode.停止周期发送);
                var ok = await StopPeriodicBroadcastAsync(); foreach (var item in group) item.ExecutionState = ok ? StepExecutionState.成功 : StepExecutionState.错误;
                if (!ok) return false;
                index += group.Count - 1;
                continue;
            }
            var success = await ExecuteStep(step, callStack); step.ExecutionState = success ? StepExecutionState.成功 : StepExecutionState.错误;
            _timelineEntries.Add(new RunTimelineRow(DateTime.Now.ToString("HH:mm:ss"), step.Name, step.Kind.ToString(), success ? "完成" : "执行错误"));
            if (!success) return false;
        }
        return true;
    }

    private static List<ConfigurationStep> TakeContiguousBroadcastGroup(
        IReadOnlyList<ConfigurationStep> steps, int startIndex, BroadcastMode mode)
    {
        var group = new List<ConfigurationStep>();
        for (var index = startIndex; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.Kind != ConfigurationStepKind.广播报文 || step.Broadcast.Mode != mode) break;
            group.Add(step);
        }
        return group;
    }

    private async Task<bool> StartPeriodicBroadcastAsync(IReadOnlyList<ConfigurationStep> steps)
    {
        if (!_usb.IsOpen) { AppendLog("主控板未连接。"); return false; }
        if (steps.Count is < 1 or > Frame.MAX_BC_FRAMES)
        {
            AppendLog($"周期广播帧数必须为 1~{Frame.MAX_BC_FRAMES}，当前为 {steps.Count}。");
            return false;
        }

        var frames = new List<(byte ide, uint id, ushort periodMs, byte dlc, byte[] data)>();
        foreach (var step in steps)
        {
            if (step.Broadcast.PeriodMs is < 1 or > ushort.MaxValue)
            {
                AppendLog($"周期报文“{step.Name}”的周期必须在 1~{ushort.MaxValue} ms 内。");
                return false;
            }
            if (!TryData(step.Broadcast.DataHex, step.Broadcast.Dlc, out var data))
            {
                AppendLog($"周期报文“{step.Name}”的数据格式错误。");
                return false;
            }
            frames.Add((step.Broadcast.Extended ? (byte)1 : (byte)0, step.Broadcast.Id,
                (ushort)step.Broadcast.PeriodMs, step.Broadcast.Dlc, data));
        }

        var (setOk, setError, setStatus, _) = await _usb.SendAndWaitAsync(
            Frame.CMD_SET_BROADCAST, Frame.SetBroadcastPayload(frames));
        if (!setOk || setStatus != Frame.STS_OK)
        {
            AppendLog($"下发周期广播表失败：{(setError.Length > 0 ? setError : Frame.StatusName(setStatus))}");
            return false;
        }

        var (startOk, startError, startStatus, _) = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_START, []);
        if (!startOk || startStatus != Frame.STS_OK)
        {
            AppendLog($"启动周期广播失败：{(startError.Length > 0 ? startError : Frame.StatusName(startStatus))}");
            return false;
        }

        var (statusOk, statusError, statusCode, payload) = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, []);
        if (!statusOk || statusCode != Frame.STS_OK || payload.Length < 2)
        {
            AppendLog($"周期广播已启动，但状态查询失败：{(statusError.Length > 0 ? statusError : Frame.StatusName(statusCode))}");
            return false;
        }
        if (payload[0] != 1 || payload[1] != frames.Count)
        {
            AppendLog($"周期广播状态异常：运行={payload[0]}，主控板帧数={payload[1]}，期望={frames.Count}。");
            return false;
        }
        AppendLog($"周期广播已由主控板启动：{frames.Count} 帧。 ");
        return true;
    }

    private async Task<bool> StopPeriodicBroadcastAsync()
    {
        if (!_usb.IsOpen) { AppendLog("主控板未连接。"); return false; }
        var (ok, error, status, _) = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STOP, []);
        if (!ok || status != Frame.STS_OK)
        {
            AppendLog($"停止周期广播失败：{(error.Length > 0 ? error : Frame.StatusName(status))}");
            return false;
        }
        var (statusOk, statusError, statusCode, payload) = await _usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STATUS, []);
        if (!statusOk || statusCode != Frame.STS_OK || payload.Length < 2)
        {
            AppendLog($"周期广播已停止，但状态查询失败：{(statusError.Length > 0 ? statusError : Frame.StatusName(statusCode))}");
            return false;
        }
        if (payload[0] != 0)
        {
            AppendLog($"周期广播状态异常：主控板仍显示运行中。");
            return false;
        }
        AppendLog("周期广播已停止。");
        return true;
    }
    private async Task<bool> ExecuteStep(ConfigurationStep s, HashSet<string> callStack)
    {
        if (s.Kind == ConfigurationStepKind.等待)
        {
            await Task.Delay(Math.Max(0, s.Wait.DurationMs), _runCts?.Token ?? CancellationToken.None);
            return true;
        }
        if (s.Kind == ConfigurationStepKind.组合)
        {
            _timelineEntries.Add(new RunTimelineRow(DateTime.Now.ToString("HH:mm:ss"), s.Name, "组合", "进入"));
            AppendLog($"进入组合：{s.Name}");
            return await RunAsync(s.Children, callStack);
        }
        if (s.Kind == ConfigurationStepKind.引用序列)
        {
            var target = _configuration.FindSequence(s.Reference.SequenceId);
            if (target is null) { AppendLog("资源不存在，请重新选择。"); return false; }
            if (!callStack.Add(target.Id)) { AppendLog($"检测到循环引用：{target.Name}"); return false; }
            try { return await RunAsync(target.Steps, callStack); }
            finally { callStack.Remove(target.Id); }
        }
        if (!_usb.IsOpen) { AppendLog("主控板未连接。"); return false; }
        byte cmd; byte[] payload;
        switch (s.Kind)
        {
            case ConfigurationStepKind.广播报文:
                if (s.Broadcast.Mode != BroadcastMode.发送一次)
                {
                    AppendLog("周期广播必须通过序列执行，不能按单帧发送。");
                    return false;
                }
                if (!TryData(s.Broadcast.DataHex, s.Broadcast.Dlc, out var data)) { AppendLog("广播报文数据格式错误。"); return false; }
                cmd = Frame.CMD_CAN_SEND; payload = Frame.CanSendPayload(1, s.Broadcast.Extended, s.Broadcast.Id, s.Broadcast.Dlc, data); break;
            case ConfigurationStepKind.电源控制:
                cmd = s.Power.Target switch { PowerTarget.ECU主电源 => Frame.CMD_SET_ECU_PWR, PowerTarget.IGN => Frame.CMD_SET_IGN, _ => Frame.CMD_SET_KEYPWR }; payload = Frame.PowerPayload(s.Power.On); break;
            case ConfigurationStepKind.动作控制: cmd = Frame.CMD_CAN_SEND; payload = Frame.KeypadOutputPayload(s.Action.OutputMask); break;
            case ConfigurationStepKind.报文解析:
                return await ExecuteParseStepAsync(s.Parse, _runCts?.Token ?? CancellationToken.None);
            default: return false;
        }
        var (ok, error, status, _) = await _usb.SendAndWaitAsync(cmd, payload);
        AppendLog(ok && status == Frame.STS_OK ? "命令完成。" : $"命令失败：{(error.Length > 0 ? error : Frame.StatusName(status))}"); return ok && status == Frame.STS_OK;
    }
    private static bool TryData(string text, byte dlc, out byte[] data)
    { var parts = text.Split([' ', ',', ':', '-'], StringSplitOptions.RemoveEmptyEntries); data = []; if (parts.Length != dlc) return false; try { data = parts.Select(x => byte.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray(); return true; } catch { return false; } }

    private async Task<bool> ExecuteParseStepAsync(ParseParameters parse, CancellationToken cancellationToken)
    {
        var hasExpectation = !string.IsNullOrWhiteSpace(parse.Expectation);
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt + TimeSpan.FromMilliseconds(Math.Max(0, parse.TimeoutMs));
        var notBefore = startedAt;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetObservation(parse, notBefore, out var sample))
            {
                var displayName = !string.IsNullOrWhiteSpace(parse.DisplayName)
                    ? parse.DisplayName
                    : !string.IsNullOrWhiteSpace(parse.SignalName) ? parse.SignalName : $"0x{parse.Id:X}";
                var unit = string.IsNullOrWhiteSpace(sample.Unit) ? string.Empty : $" {sample.Unit}";
                AppendLog($"报文解析：{displayName} = {sample.Value:0.###}{unit}; 原始={sample.RawFrame}");
                if (!hasExpectation)
                    return true;

                if (!ExpectationEvaluator.TryEvaluate(sample.Value, parse.Expectation, out var matched, out var error))
                {
                    AppendLog($"报文解析执行错误：{error ?? "期望表达式无效。"}");
                    return false;
                }
                if (matched)
                {
                    AppendLog($"报文解析判定通过：期望 {parse.Expectation}，实际 {sample.Value:0.###}。");
                    return true;
                }

                notBefore = sample.Timestamp;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    AppendLog($"报文解析判定失败：超时 {parse.TimeoutMs} ms 未满足 {parse.Expectation}，最后实际值 {sample.Value:0.###}。");
                    return false;
                }
            }
            else if (DateTimeOffset.UtcNow >= deadline)
            {
                AppendLog($"报文解析执行错误：超时 {parse.TimeoutMs} ms 未收到 ID 0x{parse.Id:X} 的新报文。");
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private void ReceiveCan(CanFrame frame)
    {
        if (frame.Channel != 1) return;
        _waveformFrames.Add(frame);
        lock (_rawGate) _latestCanById[(frame.Ide, frame.Id)] = frame;
        var key = (frame.Ide, frame.Id);
        lock (_rawGate)
        {
            if (!_pendingRawById.TryGetValue(key, out var pending))
            {
                pending = new PendingRaw(frame.Ide, frame.Id);
                _pendingRawById[key] = pending;
            }
            pending.Apply(frame);
        }
    }

    private void FlushRawCan()
    {
        if (_rawPaused) return;
        PendingRawSnapshot[] changes;
        lock (_rawGate)
        {
            if (_pendingRawById.Count == 0) return;
            changes = _pendingRawById.Values.Select(x => x.Snapshot()).ToArray();
            _pendingRawById.Clear();
        }

        foreach (var change in changes)
        {
            var key = (change.Ide, change.Id);
            if (!_rawById.TryGetValue(key, out var row))
            {
                row = new RawRow(change.Ide, change.Id);
                _rawById[key] = row;
                _rawRows.Add(row);
            }
            row.Apply(change);
        }
        UpdateCan1EmptyState();
        RawGrid.Items.Refresh();
    }

    private void RawPause_Click(object sender, RoutedEventArgs e)
    {
        _rawPaused = !_rawPaused;
        RawPauseButton.Content = _rawPaused ? "继续刷新" : "暂停刷新";
        RawPauseButton.ToolTip = _rawPaused
            ? "继续显示最新 ECU 报文"
            : "冻结右侧显示，不影响 ECU 报文接收和自动测试";
        AppendLog(_rawPaused ? "ECU CAN1 实时报文显示已暂停。" : "ECU CAN1 实时报文显示已继续。 ");
    }

    private void RawClear_Click(object sender, RoutedEventArgs e)
    {
        lock (_rawGate)
        {
            _rawById.Clear();
            _rawRows.Clear();
            _pendingRawById.Clear();
        }
        UpdateCan1EmptyState();
        RawGrid.Items.Refresh();
    }

    private void RefreshLive() => UpdateCan1EmptyState();

    private void UpdateCan1EmptyState()
    {
        Can1Empty = _rawRows.Count == 0;
        if (RawGrid is not null)
            RawGrid.ToolTip = Can1Empty ? "CAN1 暂无接收报文" : "CAN1 实时报文已按 ID 汇总";
    }

    private bool TryGetObservation(ParseParameters parse, DateTimeOffset notBefore, out ObservationSample sample)
    {
        lock (_rawGate)
        {
            if (!_latestCanById.TryGetValue((parse.Extended, parse.Id), out var frame))
            {
                sample = default!;
                return false;
            }
            var receivedAt = EstimateReceivedAt(frame);
            if (receivedAt < notBefore || !TryDecodeSignal(frame, parse, out var value))
            {
                sample = default!;
                return false;
            }
            sample = new ObservationSample(value, frame.DataText, receivedAt, parse.Unit);
            return true;
        }
    }

    private static DateTimeOffset EstimateReceivedAt(CanFrame frame) =>
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - frame.TimeMs));

    private static bool TryDecodeSignal(CanFrame frame, WaveformAssertionParameters assertion, out double value)
    {
        var parse = new ParseParameters
        {
            Extended = assertion.Extended,
            Id = assertion.Id,
            StartByte = assertion.StartByte,
            StartBit = assertion.StartBit,
            BitLength = assertion.BitLength,
            ByteOrder = assertion.ByteOrder,
            Factor = assertion.Factor,
            Offset = assertion.Offset,
            Unit = assertion.Unit
        };
        return TryDecodeSignal(frame, parse, out value);
    }

    private static string DescribeWaveformTarget(WaveformAssertionParameters assertion)
    {
        var signal = !string.IsNullOrWhiteSpace(assertion.DisplayName)
            ? assertion.DisplayName
            : !string.IsNullOrWhiteSpace(assertion.SignalName) ? assertion.SignalName : assertion.DatabaseSignalName;
        return $"报文 0x{assertion.Id:X}{(assertion.Extended ? "（扩展帧）" : "")} / 信号 {signal ?? "未命名"}";
    }

    private static bool TryDecodeSignal(CanFrame frame, ParseParameters parse, out double value)
    {
        value = 0;
        if (parse.StartByte < 0 || parse.StartBit < 0 || parse.StartBit > 7 || parse.BitLength is < 1 or > 32) return false;
        var start = parse.StartByte * 8 + parse.StartBit;
        uint raw = 0;
        var motorola = parse.ByteOrder.Contains("Motorola", StringComparison.OrdinalIgnoreCase)
            || parse.ByteOrder.Contains("大", StringComparison.Ordinal);
        if (motorola)
        {
            var position = start;
            for (var bit = 0; bit < parse.BitLength; bit++)
            {
                var byteIndex = position / 8;
                if (byteIndex < 0 || byteIndex >= frame.Dlc || byteIndex >= frame.Data.Length) return false;
                raw = (raw << 1) | (uint)((frame.Data[byteIndex] >> (position % 8)) & 1);
                position = position % 8 == 0 ? position + 15 : position - 1;
            }
        }
        else
        {
            if (start + parse.BitLength > frame.Dlc * 8) return false;
            for (var bit = 0; bit < parse.BitLength; bit++)
            {
                var position = start + bit;
                if ((frame.Data[position / 8] & (1 << (position % 8))) != 0) raw |= 1u << bit;
            }
        }
        value = raw * parse.Factor + parse.Offset;
        return true;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var error = _usb.Open();
        if (error.Length > 0)
        {
            SetConnection(false, "连接失败");
            AppendLog($"主控板连接失败：{error}");
        }
    }
    private void Disconnect_Click(object sender, RoutedEventArgs e) => _usb.Close();
    private void ConnectionChanged(bool connected)
    {
        SetConnection(connected);
        if (connected) _ = VerifyMainControllerProtocolAsync();
        else AppendLog("主控 USB 已断开；不自动发送任何 KEY/IGN/夹具恢复命令，实体输出状态须现场核对。");
    }

    /// <summary>
    /// QUICK 首轮只读联机检查；Open()/重连绝不施加 ECU/KEY 电源或 IGN。
    /// PING/GET_VERSION 只验证协议层，不代表固件镜像身份或真实夹具到位。
    /// </summary>
    private async Task VerifyMainControllerProtocolAsync()
    {
        if (Interlocked.Exchange(ref _checkingUsbProtocol, 1) != 0) return;
        try
        {
            AppendLog("USB 已枚举打开，开始 PING / GET_VERSION；没有执行任何输出控制。");
            var ping = await _usb.SendAndWaitAsync(Frame.CMD_PING, Array.Empty<byte>(), 2000);
            if (!ping.ok || ping.status != Frame.STS_OK ||
                System.Text.Encoding.ASCII.GetString(ping.payload) != "PONG")
            {
                AppendLog("USB PING 检查失败：" + (ping.err.Length > 0 ? ping.err : Frame.StatusName(ping.status)));
                if (_usb.IsOpen) SetConnection(false, "主控协议 PING 未通过");
                return;
            }
            AppendLog("USB PING=PONG（只证明应用层通讯正常）。");
            var version = await _usb.SendAndWaitAsync(Frame.CMD_GET_VERSION, Array.Empty<byte>(), 2000);
            if (!version.ok || version.status != Frame.STS_OK || version.payload.Length is < 1 or > 80)
            {
                AppendLog("GET_VERSION 失败：" + (version.err.Length > 0 ? version.err : Frame.StatusName(version.status)));
                if (_usb.IsOpen) SetConnection(false, "主控版本读取失败");
                return;
            }
            var firmware = System.Text.Encoding.ASCII.GetString(version.payload);
            AppendLog("GET_VERSION=" + firmware);
            if (firmware != Frame.MAIN_CONTROL_V04_VERSION)
            {
                AppendLog("警告：不是预期 v0.4-dev 协议；禁止按新版双夹具协议发送控制命令。版本文字也不能替代固件 SHA 核查。");
                ConnectionText.ToolTip = "当前固件 " + firmware + "；未确认为新夹具协议";
                return;
            }
            // Only query MCU levels; never toggle actual outputs during connection.
            var ign = await _usb.SendAndWaitAsync(Frame.CMD_GET_IGN, Array.Empty<byte>(), 2000);
            if (ign.ok && ign.status == Frame.STS_OK && ign.payload.Length == 1 && ign.payload[0] <= 1)
                AppendLog("GET_IGN：MCU GPIO=" + ign.payload[0] + "（非 ECU 实测电压）。");
            else
                AppendLog("GET_IGN 未确认：" + (ign.err.Length > 0 ? ign.err : Frame.StatusName(ign.status)));
            var clamps = await _usb.SendAndWaitAsync(Frame.CMD_GET_CLAMPS, Array.Empty<byte>(), 2000);
            if (clamps.ok && clamps.status == Frame.STS_OK && clamps.payload.Length == 1 && clamps.payload[0] <= 3)
                AppendLog("GET_CLAMPS：夹具1 GPIO=" + (clamps.payload[0] & 1) +
                    "，夹具2 GPIO=" + ((clamps.payload[0] >> 1) & 1) + "（非机械到位反馈）。");
            else
                AppendLog("GET_CLAMPS 未确认：" + (clamps.err.Length > 0 ? clamps.err : Frame.StatusName(clamps.status)));
            if (_usb.IsOpen)
                ConnectionText.ToolTip = "PING 通过，版本=" + firmware + "；GPIO 已读回（不等于物理测量）";
        }
        catch (Exception ex)
        {
            AppendLog("USB 只读联机检查异常：" + ex.Message);
        }
        finally { Interlocked.Exchange(ref _checkingUsbProtocol, 0); }
    }

    private void SetConnection(bool connected, string? disconnectedText = null)
    {
        ConnectionDot.Fill = connected
            ? Brushes.LimeGreen
            : disconnectedText is null ? Brushes.Gray : Brushes.IndianRed;
        ConnectionText.Text = connected ? "主控板已连接" : disconnectedText ?? "主控板未连接";
        ConnectionText.ToolTip = ConnectionText.Text;
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        _configuration = EcuConfiguration.CreateNew();
        _selectedItem = null; _selectedSequence = null; StepsTree.ItemsSource = null;
        var directory = GetRememberedDirectory();
        _path = Path.Combine(directory, $"新 ECU 配置-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        UpdateDatabaseStatus(); RebuildTree(); ProjectText.Text = $"{Path.GetFileName(_path)} · 自动保存"; LoadRecentConfigSelector();
    }

    private void ConfigFileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildConfigFileMenu();
        menu.PlacementTarget = ConfigFileMenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // 顶部命令栏只改变工作区可见性，不改变资源引用关系。
    private void ResourceToolbar_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceTabs.SelectedIndex = 1;
        ResourceNavigationExpander.IsExpanded = true;
        ResourceNavigationExpander.BringIntoView();
    }

    private void CommunicationDatabaseToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (_configuration.CommunicationDatabase is null)
        {
            MessageBox.Show(this, "当前配置尚未加载通信数据库。请从“更多”导入 DBC 或解析定义。", "通信数据库");
            return;
        }
        ShowDatabaseOverview();
    }

    private void ConfigFileMenuButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ConfigFileMenuButton.ContextMenu = BuildConfigFileMenu();
    }

    private ContextMenu BuildConfigFileMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("新建配置", (_, _) => New_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("打开配置…", (_, _) => Open_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("保存", (_, _) => Save_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("另存为…", (_, _) => SaveAs_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("导入 DBC 通信数据库…", (_, _) => ImportDbc_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("导出 DBC 通信数据库…", (_, _) => ExportDbc_Click(this, new RoutedEventArgs())));
         menu.Items.Add(MenuItem("打开报文定义", (_, _) => ShowDatabaseOverview()));
         menu.Items.Add(MenuItem("导入报文解析 CSV…", (_, _) => ImportParseCsv_Click(this, new RoutedEventArgs())));
         menu.Items.Add(MenuItem("导出报文解析 CSV…", (_, _) => ExportParseCsv_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MenuItem("导出项目 JSON…", (_, _) => SaveAs_Click(this, new RoutedEventArgs())));
        return menu;
    }

    private void ImportDbc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DBC 通信数据库 (*.dbc)|*.dbc|所有文件 (*.*)|*.*", InitialDirectory = GetRememberedDirectory() };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = DbcParser.Parse(dialog.FileName);
            if (result.Database.Messages.Count == 0) { MessageBox.Show(this, string.Join(Environment.NewLine, result.Errors), "DBC 导入失败"); return; }
            _configuration.CommunicationDatabase = result.Database;
            foreach (var diagnostic in _configuration.CommunicationDatabase.Normalize()) AppendLog($"通信数据库提示：{diagnostic}");
            RefreshDefinitionBindings();
            UpdateDatabaseStatus();
            foreach (var error in result.Errors) AppendLog($"DBC 导入警告：{error}");
            AppendLog($"已导入 DBC：{result.Database.Name}，报文 {result.Database.Messages.Count} 个。");
            UpdateDatabaseStatus(); RebuildTree();
            ShowDatabaseOverview(result.Errors);
            _ = SaveImportedConfigurationAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "DBC 导入失败"); }
    }

    private void ImportParseCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "报文解析 CSV (*.csv)|*.csv|所有文件 (*.*)|*.*", InitialDirectory = GetRememberedDirectory() };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            MergeParseDefinitionsIntoDatabase(ParseDefinitionCsv.Import(dialog.FileName));
            UpdateDatabaseStatus();
            AppendLog($"已导入报文解析定义并合并到通信数据库。");
            RefreshDefinitionBindings(); RebuildTree(); _ = SaveImportedConfigurationAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "解析 CSV 导入失败"); }
    }

    private void ExportDbc_Click(object sender, RoutedEventArgs e)
    {
        if (_configuration.CommunicationDatabase is not { Messages.Count: > 0 } database)
        {
            MessageBox.Show(this, "当前项目没有可导出的通信数据库。", "导出提示");
            return;
        }
        var dialog = new SaveFileDialog { Filter = "DBC 通信数据库 (*.dbc)|*.dbc", InitialDirectory = GetRememberedDirectory(), FileName = $"{database.Name}.dbc" };
        if (dialog.ShowDialog(this) != true) return;
        try { DbcExporter.Export(dialog.FileName, database); AppendLog($"已导出 DBC：{Path.GetFileName(dialog.FileName)}。"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "DBC 导出失败"); }
    }

    private void ExportParseCsv_Click(object sender, RoutedEventArgs e)
    {
        var parseDefinitions = BuildParseDefinitionSetFromDatabase();
        if (parseDefinitions.Signals.Count == 0) { MessageBox.Show(this, "当前项目没有可导出的报文解析定义。", "导出提示"); return; }
        var dialog = new SaveFileDialog { Filter = "报文解析 CSV (*.csv)|*.csv", InitialDirectory = GetRememberedDirectory(), FileName = "报文解析定义.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { ParseDefinitionCsv.Export(dialog.FileName, parseDefinitions); AppendLog($"已导出报文解析定义：{Path.GetFileName(dialog.FileName)}。"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "解析 CSV 导出失败"); }
    }

    private async Task SaveImportedConfigurationAsync()
    {
        if (string.IsNullOrWhiteSpace(_path)) return;
        try
        {
            _configuration.CommunicationDatabase?.Normalize();
            await File.WriteAllTextAsync(_path, ConfigurationJson.Serialize(_configuration));
            ProjectText.Text = $"{Path.GetFileName(_path)} · 已自动保存"; LoadRecentConfigSelector();
        }
        catch (Exception ex) { AppendLog($"导入内容自动保存失败：{ex.Message}"); }
    }

    private void UpdateDatabaseStatus()
    {
        if (DatabaseStatusText is null) return;
        var db = _configuration.CommunicationDatabase;
        if (db is null)
        {
            DatabaseStatusText.Text = "通信数据库：未加载";
            DatabaseStatusText.Foreground = ThemeBrush("Text3Brush");
            return;
        }

        if (db.Messages.Count == 0)
        {
            DatabaseStatusText.Text = $"通信数据库：{db.Name} · 空数据库";
            DatabaseStatusText.Foreground = ThemeBrush("AmberBrush");
            return;
        }

        var signalCount = db.Messages.Sum(x => x.Signals.Count);
        DatabaseStatusText.Text = $"通信数据库：{db.Name} · {db.Messages.Count} 报文 · {signalCount} 信号";
        DatabaseStatusText.Foreground = ThemeBrush("GreenBrush");
    }

    private void DatabaseStatusText_Click(object sender, MouseButtonEventArgs e)
        => CommunicationDatabaseToolbar_Click(sender, new RoutedEventArgs());

    private async void ReloadCommunicationDefinitions_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        {
            MessageBox.Show(this, "当前配置还没有保存到文件，无法从文件更新通信定义。", "更新通信定义");
            return;
        }

        try
        {
            var diskJson = await File.ReadAllTextAsync(_path);
            var disk = ConfigurationJson.Deserialize(diskJson);
            var oldMessageCount = _configuration.CommunicationDatabase?.Messages.Count ?? 0;
            var oldSignalCount = _configuration.CommunicationDatabase?.Messages.Sum(x => x.Signals.Count) ?? 0;
            var oldParseCount = _configuration.CommunicationDatabase?.Messages.Sum(x => x.Signals.Count) ?? 0;

            // 只同步通信定义，不替换当前测试用例和资源树，避免丢失当前未保存的流程编辑。
            _configuration.CommunicationDatabase = disk.CommunicationDatabase;
            RefreshDefinitionBindings();
            UpdateDatabaseStatus();
            RebuildTree();

            var newMessageCount = _configuration.CommunicationDatabase?.Messages.Count ?? 0;
            var newSignalCount = _configuration.CommunicationDatabase?.Messages.Sum(x => x.Signals.Count) ?? 0;
            var newParseCount = _configuration.CommunicationDatabase?.Messages.Sum(x => x.Signals.Count) ?? 0;
            AppendLog($"已更新通信定义：报文 {oldMessageCount}→{newMessageCount}，信号 {oldSignalCount}→{newSignalCount}，解析 {oldParseCount}→{newParseCount}。测试流程未被替换。");
            ProjectText.Text = $"{Path.GetFileName(_path)} · 通信定义已更新";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"更新通信定义失败：{ex.Message}", "更新通信定义");
        }
    }

    private void ShowDatabaseOverview(IReadOnlyList<string>? importWarnings = null, bool parseOnly = false)
    {
        var database = _configuration.CommunicationDatabase ??= new CanDatabase { Name = "新通信数据库" };
        UpdateDatabaseStatus();
        var editor = new CommunicationDatabaseWindow(
            this,
            database,
            CommitCommunicationDatabase,
            () => ImportDbc_Click(this, new RoutedEventArgs()),
            () => ExportDbc_Click(this, new RoutedEventArgs()),
            () => ImportParseCsv_Click(this, new RoutedEventArgs()),
            () => ExportParseCsv_Click(this, new RoutedEventArgs()),
            () => Save_Click(this, new RoutedEventArgs()),
            () => ReloadCommunicationDefinitions_Click(this, new RoutedEventArgs()));
        editor.ShowDialog();
    }

    private void CommitCommunicationDatabase()
    {
        _configuration.CommunicationDatabase?.Normalize();
        RefreshDefinitionBindings();
        UpdateDatabaseStatus();
        RebuildTree();
        _ = SaveImportedConfigurationAsync();
    }

    // 旧版动态窗口保留在当前版本中仅用于对照；新入口统一由 CommunicationDatabaseWindow 承担。
    private void ShowDatabaseOverviewLegacy(IReadOnlyList<string>? importWarnings = null, bool parseOnly = false)
    {
        var db = _configuration.CommunicationDatabase ??= new CanDatabase { Name = "新通信数据库" };
        UpdateDatabaseStatus();
        var window = new Window { Owner = this, Title = "通信数据库（报文与信号统一定义）", Width = 1420, Height = 860, MinWidth = 1100, MinHeight = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White };
        var root = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        Button Tool(string title, RoutedEventHandler action) { var b = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) }; b.Click += action; return b; }
        toolbar.Children.Add(Tool("导入 DBC 报文定义…", (_, _) => { window.Close(); ImportDbc_Click(this, new RoutedEventArgs()); }));
        toolbar.Children.Add(Tool("导出 DBC…", (_, _) => ExportDbc_Click(this, new RoutedEventArgs())));
        toolbar.Children.Add(Tool("导入解析 CSV…", (_, _) => { window.Close(); ImportParseCsv_Click(this, new RoutedEventArgs()); }));
        toolbar.Children.Add(Tool("导出解析 CSV…", (_, _) => ExportParseCsv_Click(this, new RoutedEventArgs())));
        toolbar.Children.Add(Tool("保存项目", (_, _) => Save_Click(this, new RoutedEventArgs())));
        toolbar.Children.Add(Tool("刷新当前配置", (_, _) => { window.Close(); ReloadCommunicationDefinitions_Click(this, new RoutedEventArgs()); }));
        toolbar.Children.Add(new TextBlock { Text = $"{db.Name} · {db.Messages.Count} 个报文 · {db.Messages.Sum(x => x.Signals.Count)} 个信号 · 广播/解析共用", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray, Margin = new Thickness(12, 0, 0, 0) });
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var search = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 6), ToolTip = "按报文 ID、报文名称、名称/别名或信号说明筛选" }; DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        var messageList = new ListBox { BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 232)), BorderThickness = new Thickness(1) }; left.Children.Add(messageList);
        var leftBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) }; DockPanel.SetDock(leftBar, Dock.Bottom);
        leftBar.Children.Add(Tool("新增报文", (_, _) => { var m = new CanMessageDefinition { Name = "新报文", Dlc = 8, CycleTimeMs = 100 }; db.Messages.Add(m); messageList.ItemsSource = FilterMessages(); messageList.SelectedItem = m; Commit(); }));
        leftBar.Children.Add(Tool("删除", (_, _) => { if (messageList.SelectedItem is CanMessageDefinition m) { db.Messages.Remove(m); messageList.ItemsSource = FilterMessages(); messageList.SelectedIndex = 0; Commit(); } })); left.Children.Add(leftBar);
        Grid.SetColumn(left, 0); body.Children.Add(left);
        var grip = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent }; Grid.SetColumn(grip, 1); body.Children.Add(grip);
        var editor = new Grid { Margin = new Thickness(0, 0, 0, 0) }; editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) }); editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var messageFields = new Grid { Margin = new Thickness(0, 0, 0, 4) }; for (var i = 0; i < 8; i++) messageFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? 100 : 1, i % 2 == 0 ? GridUnitType.Pixel : GridUnitType.Star) });
        var nameBox = new TextBox(); var idBox = new TextBox(); var extBox = new CheckBox { Content = "扩展帧", VerticalAlignment = VerticalAlignment.Center }; var dlcBox = new TextBox(); var cycleBox = new TextBox(); var sendBox = new ComboBox { ItemsSource = new[] { "仅发送一次", "开始周期发送", "停止周期发送" } };
        void Field(string label, Control c, int row, int col) { messageFields.RowDefinitions.Count(); var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 12, 3) }; panel.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(c); Grid.SetRow(panel, row); Grid.SetColumn(panel, col); Grid.SetColumnSpan(panel, 3); messageFields.Children.Add(panel); }
        messageFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); messageFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Field("名称", nameBox, 0, 0); Field("报文 ID", idBox, 0, 4); Field("帧类型", extBox, 1, 0); Field("DLC", dlcBox, 1, 2); Field("周期(ms)", cycleBox, 1, 4); Field("发送用途", sendBox, 1, 6);
        Grid.SetRow(messageFields, 0); editor.Children.Add(messageFields);
        var signalGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false, RowHeight = 28, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 0, 0, 0) };
        var signalToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) }; signalToolbar.Children.Add(new TextBlock { Text = "报文信号定义", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        var addSignal = Tool("新增信号", (_, _) => { if (messageList.SelectedItem is CanMessageDefinition m) { var s = new CanSignalDefinition { Name = "新信号", DisplayName = "", BitLength = 1, DisplayMode = "数值" }; m.Signals.Add(s); signalGrid.ItemsSource = null; signalGrid.ItemsSource = m.Signals; signalGrid.SelectedItem = s; Commit(); } }); signalToolbar.Children.Add(addSignal); signalToolbar.Children.Add(Tool("删除信号", (_, _) => { if (messageList.SelectedItem is CanMessageDefinition m && signalGrid.SelectedItem is CanSignalDefinition s) { m.Signals.Remove(s); signalGrid.ItemsSource = null; signalGrid.ItemsSource = m.Signals; Commit(); } })); signalToolbar.Children.Add(Tool("创建广播资源", (_, _) => { if (messageList.SelectedItem is CanMessageDefinition m) CreateBroadcastResource(m); })); signalToolbar.Children.Add(Tool("创建解析资源", (_, _) => { if (messageList.SelectedItem is CanMessageDefinition m && signalGrid.SelectedItem is CanSignalDefinition s) CreateParseResource(new CommunicationSignalDefinition(m.Id, m.Extended, s)); })); Grid.SetRow(signalToolbar, 2); editor.Children.Add(signalToolbar);
        signalGrid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.Name)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "别名", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.DisplayName)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "起始字节", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.StartByte)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "起始位", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.StartBitInByte)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "位宽", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.BitLength)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "字节序", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.ByteOrderDisplay)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "系数", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.Factor)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "偏移", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.Offset)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.Unit)) }); signalGrid.Columns.Add(new DataGridTextColumn { Header = "描述", Binding = new System.Windows.Data.Binding(nameof(CanSignalDefinition.Description)) }); Grid.SetRow(signalGrid, 3); editor.Children.Add(signalGrid);
        Grid.SetColumn(editor, 2); body.Children.Add(editor); root.Children.Add(body); window.Content = root;
        IEnumerable<CanMessageDefinition> FilterMessages() { var q = search.Text.Trim(); return db.Messages.OrderBy(x => x.Id).Where(x => CanDefinitionDisplay.Matches(x, q)).ToList(); }
        void Commit() { var invalid = db.ValidateSignalNames(); if (invalid.Count > 0) { MessageBox.Show(this, string.Join("\n", invalid.Take(8)), "无法保存通信数据库", MessageBoxButton.OK, MessageBoxImage.Warning); return; } db.Normalize(); RefreshDefinitionBindings(); RebuildTree(); _ = SaveImportedConfigurationAsync(); UpdateDatabaseStatus(); }
        signalGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(Commit, System.Windows.Threading.DispatcherPriority.Background);
        void LoadMessage(CanMessageDefinition? m) { if (m is null) return; nameBox.Text = m.Name; idBox.Text = $"0x{m.Id:X}"; extBox.IsChecked = m.Extended; dlcBox.Text = m.Dlc.ToString(CultureInfo.InvariantCulture); cycleBox.Text = m.CycleTimeMs?.ToString(CultureInfo.InvariantCulture) ?? ""; sendBox.SelectedItem = m.SendType; signalGrid.ItemsSource = m.Signals; }
        void SaveMessage() { if (messageList.SelectedItem is not CanMessageDefinition m) return; m.Name = nameBox.Text.Trim(); if (uint.TryParse(idBox.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) m.Id = id; m.Extended = extBox.IsChecked == true; if (int.TryParse(dlcBox.Text, out var dlc)) m.Dlc = Math.Clamp(dlc, 0, 64); if (int.TryParse(cycleBox.Text, out var cycle)) m.CycleTimeMs = cycle; m.SendType = sendBox.SelectedItem?.ToString() ?? "仅发送一次"; messageList.ItemsSource = FilterMessages(); messageList.SelectedItem = m; Commit(); }
        search.TextChanged += (_, _) => { var selected = messageList.SelectedItem; messageList.ItemsSource = FilterMessages(); if (selected is not null) messageList.SelectedItem = selected; };
        messageList.DisplayMemberPath = nameof(CanMessageDefinition.Name); messageList.ItemsSource = FilterMessages(); messageList.SelectionChanged += (_, _) => LoadMessage(messageList.SelectedItem as CanMessageDefinition); messageList.MouseDoubleClick += (_, _) => LoadMessage(messageList.SelectedItem as CanMessageDefinition);
        foreach (var c in new Control[] { nameBox, idBox, dlcBox, cycleBox }) c.LostFocus += (_, _) => SaveMessage(); extBox.Checked += (_, _) => SaveMessage(); extBox.Unchecked += (_, _) => SaveMessage(); sendBox.SelectionChanged += (_, _) => SaveMessage();
        messageList.SelectedIndex = 0; window.ShowDialog();
    }

    private void RefreshDefinitionBindings()
    {
        var database = _configuration.CommunicationDatabase;
        if (database is null) return;
        var result = _definitionBindingService.Refresh(_configuration, database);
        foreach (var diagnostic in result.Diagnostics)
            AppendLog($"数据库定义诊断 [{diagnostic.Code}] 步骤 {diagnostic.StepId}：{diagnostic.Message}");
        RefreshStepSummary();
    }

    private IEnumerable<ConfigurationStep> AllConfigurationSteps()
    {
        IEnumerable<ConfigurationStep> WalkItem(ConfigurationTreeItem item) { if (item.Sequence is { } seq) foreach (var step in seq.Steps) { yield return step; foreach (var child in WalkStep(step)) yield return child; } foreach (var child in item.Children) foreach (var nested in WalkItem(child)) yield return nested; }
        IEnumerable<ConfigurationStep> WalkStep(ConfigurationStep step) { foreach (var child in step.Children) { yield return child; foreach (var nested in WalkStep(child)) yield return nested; } }
        return WalkItem(_configuration.Root);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ECU 配置 (*.json)|*.json", InitialDirectory = GetRememberedDirectory() };
        if (dialog.ShowDialog(this) != true) return;
        try { var json = await File.ReadAllTextAsync(dialog.FileName); _configuration = ConfigurationJson.Deserialize(json); _path = dialog.FileName; RememberPath(_path); RefreshDefinitionBindings(); UpdateDatabaseStatus(); RebuildTree(); ProjectText.Text = Path.GetFileName(_path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开失败"); }
        LoadRecentConfigSelector();
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_path is null) { var dialog = new SaveFileDialog { Filter = "ECU 配置 (*.json)|*.json", InitialDirectory = GetRememberedDirectory(), FileName = "ecu-configuration.json" }; if (dialog.ShowDialog(this) != true) return; _path = dialog.FileName; }
        RememberPath(_path); await File.WriteAllTextAsync(_path, ConfigurationJson.Serialize(_configuration)); ProjectText.Text = $"{Path.GetFileName(_path)} · 已保存"; LoadRecentConfigSelector(); AppendLog("配置已保存。");
    }
    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ECU 配置 (*.json)|*.json", InitialDirectory = GetRememberedDirectory(), FileName = _path is null ? "ecu-configuration.json" : Path.GetFileName(_path) };
        if (dialog.ShowDialog(this) != true) return;
        _path = dialog.FileName; RememberPath(_path); await File.WriteAllTextAsync(_path, ConfigurationJson.Serialize(_configuration));
        ProjectText.Text = $"{Path.GetFileName(_path)} · 已保存"; LoadRecentConfigSelector();
    }
    private string GetRememberedDirectory()
    {
        try
        {
            var remembered = GetRememberedConfigPath();
            var directory = Directory.Exists(remembered) ? remembered : Path.GetDirectoryName(remembered);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) return directory;
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
    private string? GetRememberedConfigPath()
    {
        if (!File.Exists(_configPathMemory)) return null;
        var remembered = File.ReadAllText(_configPathMemory).Trim();
        return string.IsNullOrWhiteSpace(remembered) ? null : remembered;
    }
    private void RememberPath(string path)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_configPathMemory)!); File.WriteAllText(_configPathMemory, Path.GetFullPath(path)); } catch { }
    }
    private async Task RestoreLastConfigAsync()
    {
        if (_restoredLastConfig) return;
        _restoredLastConfig = true;
        var rememberedPath = GetRememberedConfigPath();
        if (string.IsNullOrWhiteSpace(rememberedPath) || !File.Exists(rememberedPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(rememberedPath);
            _configuration = ConfigurationJson.Deserialize(json);
            _path = rememberedPath;
            RefreshDefinitionBindings();
            UpdateDatabaseStatus();
            RebuildTree();
            ProjectText.Text = Path.GetFileName(_path);
            LoadRecentConfigSelector();
        }
        catch (Exception ex)
        {
            AppendLog($"上次配置无法自动恢复：{ex.Message}");
        }
    }
    private void LoadRecentConfigSelector()
    {
        _updatingRecentSelector = true;
        RecentConfigSelector.Items.Clear();
        RecentConfigSelector.Items.Add(new RecentConfig(_path is null ? "选择配置" : Path.GetFileName(_path), "__current__"));
        RecentConfigSelector.Items.Add(new RecentConfig("打开其他配置…", ""));
        var directory = GetRememberedDirectory();
        if (Directory.Exists(directory))
            foreach (var file in Directory.GetFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTime).Take(12)) RecentConfigSelector.Items.Add(new RecentConfig(Path.GetFileName(file), file));
        RecentConfigSelector.SelectedIndex = 0;
        _updatingRecentSelector = false;
    }
    private async void RecentConfigSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRecentSelector) return;
        if (RecentConfigSelector.SelectedItem is not RecentConfig recent) return;
        if (recent.Path == "__current__") return;
        if (string.IsNullOrWhiteSpace(recent.Path)) { RecentConfigSelector.SelectedIndex = -1; Open_Click(this, new RoutedEventArgs()); return; }
        try { var json = await File.ReadAllTextAsync(recent.Path); _configuration = ConfigurationJson.Deserialize(json); _path = recent.Path; RememberPath(_path); RefreshDefinitionBindings(); UpdateDatabaseStatus(); RebuildTree(); ProjectText.Text = Path.GetFileName(_path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开失败"); }
        finally { RecentConfigSelector.SelectedIndex = 0; }
    }
    private void AppendLog(string text)
    {
        // 自动测试执行器会在线程池线程继续执行；日志入口必须统一切回 WPF UI 线程，
        // 否则一次日志写入异常会中断整个测试，并遗留仍在运行的周期广播。
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(text)));
            return;
        }
        var message = text.Trim();
        // “命令完成”没有诊断价值，步骤开始、错误和关键状态已经足够表达执行过程。
        if (message is "命令完成。" or "命令完成") return;
        var now = DateTime.Now;
        if (string.Equals(message, _lastDisplayedLog, StringComparison.Ordinal) && (now - _lastDisplayedLogAt).TotalMilliseconds < 300)
            return;
        _lastDisplayedLog = message;
        _lastDisplayedLogAt = now;
        _logEntries.Add($"[{now:HH:mm:ss}] {message}");
        if (_logEntries.Count > 1000) _logEntries.RemoveRange(0, _logEntries.Count - 1000);
        RefreshLogView();
    }

    private void RefreshLogView()
    {
        if (LogRows is null) return;
        var previousOffset = FindVisualChild<ScrollViewer>(LogRows)?.VerticalOffset;
        var query = LogSearch?.Text?.Trim() ?? "";
        var level = (LogLevelFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
        var lines = _logEntries.Where(x => string.IsNullOrWhiteSpace(query) || x.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (level == "错误") lines = lines.Where(x => x.Contains("失败", StringComparison.Ordinal) || x.Contains("错误", StringComparison.Ordinal) || x.Contains("异常", StringComparison.Ordinal));
        else if (level == "信息") lines = lines.Where(x => !x.Contains("失败", StringComparison.Ordinal) && !x.Contains("错误", StringComparison.Ordinal) && !x.Contains("异常", StringComparison.Ordinal));
        var visible = lines.ToList();
        _visibleLogEntries.Clear();
        foreach (var line in visible) _visibleLogEntries.Add(ParseLogEntry(line));
        if (previousOffset is double offset)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (FindVisualChild<ScrollViewer>(LogRows) is { } scroll)
                    scroll.ScrollToVerticalOffset(Math.Min(offset, scroll.ScrollableHeight));
            }), DispatcherPriority.Loaded);
        }
    }

    private static UiLogEntry ParseLogEntry(string value)
    {
        var time = "";
        var message = value;
        if (value.StartsWith("[", StringComparison.Ordinal) && value.IndexOf(']') is var end && end > 1)
        {
            time = value[1..end];
            message = value[(end + 1)..].TrimStart();
        }
        var level = message.Contains("失败", StringComparison.Ordinal) || message.Contains("错误", StringComparison.Ordinal) || message.Contains("异常", StringComparison.Ordinal)
            ? "错误"
            : message.Contains("警告", StringComparison.Ordinal) || message.Contains("提示", StringComparison.Ordinal)
                ? "警告"
                : message.Contains("通过", StringComparison.Ordinal) || message.Contains("成功", StringComparison.Ordinal)
                    ? "通过"
                    : "信息";
        var source = message.Contains("CAN", StringComparison.OrdinalIgnoreCase) ? "CAN 通信"
            : message.Contains("步骤", StringComparison.Ordinal) || message.Contains("执行", StringComparison.Ordinal) ? "执行引擎"
            : message.Contains("资源", StringComparison.Ordinal) ? "资源管理" : "系统";
        return new UiLogEntry(time, level, source, message);
    }

    private Brush LogBrush(string value)
    {
        if (value.Contains("判定失败", StringComparison.Ordinal)) return ThemeBrush("AmberBrush");
        if (IsErrorLogEntry(value) || value.Contains("执行错误", StringComparison.Ordinal)) return ThemeBrush("RedBrush");
        if (value.Contains("通过", StringComparison.Ordinal) || value.Contains("成功", StringComparison.Ordinal) || value.Contains("已连接", StringComparison.Ordinal)) return ThemeBrush("GreenBrush");
        if (value.Contains("跳过", StringComparison.Ordinal) || value.Contains("仅观察", StringComparison.Ordinal)) return ThemeBrush("Text3Brush");
        if (value.Contains("运行中", StringComparison.Ordinal)) return ThemeBrush("StatusRunningBrush");
        return ThemeBrush("TextBrush");
    }

    private Brush ThemeBrush(string key) => TryFindResource(key) as Brush ?? Brushes.SlateGray;

    private static bool IsErrorLogEntry(string value) =>
        value.Contains("失败", StringComparison.Ordinal) || value.Contains("错误", StringComparison.Ordinal) || value.Contains("异常", StringComparison.Ordinal);

    private void LogSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshLogView();
    private void LogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshLogView();
    private void ClearLog_Click(object sender, RoutedEventArgs e) { _logEntries.Clear(); _visibleLogEntries.Clear(); _lastDisplayedLog = null; }
    private void Ui(Action action) { if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action); }
    private async Task AutoSaveAsync()
    {
        if (_path is null) return;
        try
        {
            await File.WriteAllTextAsync(_path, ConfigurationJson.Serialize(_configuration));
            ProjectText.Text = $"{Path.GetFileName(_path)} · 已自动保存";
        }
        catch
        {
            ProjectText.Text = $"{Path.GetFileName(_path)} · 自动保存失败";
        }
    }
    private void Window_Closed(object? sender, EventArgs e) { SaveLayout(); _autoSave.Stop(); _refresh.Stop(); _usb.Dispose(); }
    private sealed class UsbWaveformAssertionSource(WaveformFrameBuffer frames) : IWaveformAssertionSource
    {
        public Task<WaveformSampleReadResult> ReadSamplesAsync(
            WaveformAssertionParameters assertion,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffered = frames.Read(assertion.Extended, assertion.Id, windowStart, windowEnd);
            if (buffered.Count == 0)
            {
                return Task.FromResult(WaveformSampleReadResult.NoSamples(
                    $"波形判定未采集到窗口内样本：{DescribeWaveformTarget(assertion)}。"));
            }

            var samples = new List<WaveformSample>(buffered.Count);
            foreach (var item in buffered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryDecodeSignal(item.Frame, assertion, out var value))
                {
                    samples.Add(new WaveformSample(
                        Math.Max(0, (item.ReceivedAt - windowStart).TotalMilliseconds), value));
                }
            }

            if (samples.Count == 0)
            {
                return Task.FromResult(WaveformSampleReadResult.NoSamples(
                    $"波形判定窗口内有报文但无法解码信号：{DescribeWaveformTarget(assertion)}。"));
            }

            var minimum = Math.Max(1, assertion.Rule.MinimumSampleCount);
            if (samples.Count < minimum)
            {
                return Task.FromResult(WaveformSampleReadResult.InsufficientSamples(
                    samples,
                    $"波形判定样本不足：需要至少 {minimum} 点，实际 {samples.Count} 点。"));
            }

            return Task.FromResult(WaveformSampleReadResult.Success(samples));
        }
    }

    private sealed class WaveformFrameBuffer
    {
        private static readonly TimeSpan Retention = TimeSpan.FromSeconds(120);
        private readonly object _gate = new();
        private readonly Dictionary<(bool Ide, uint Id), Queue<BufferedCanFrame>> _frames = [];

        public void Add(CanFrame frame)
        {
            var key = (frame.Ide, frame.Id);
            var buffered = new BufferedCanFrame(
                frame with { Data = frame.Data.ToArray() },
                EstimateReceivedAt(frame));
            lock (_gate)
            {
                if (!_frames.TryGetValue(key, out var queue))
                {
                    queue = new Queue<BufferedCanFrame>();
                    _frames[key] = queue;
                }

                queue.Enqueue(buffered);
                Trim(queue, buffered.ReceivedAt - Retention);
            }
        }

        public IReadOnlyList<BufferedCanFrame> Read(
            bool extended,
            uint id,
            DateTimeOffset start,
            DateTimeOffset end)
        {
            lock (_gate)
            {
                if (!_frames.TryGetValue((extended, id), out var queue)) return [];
                Trim(queue, DateTimeOffset.UtcNow - Retention);
                return queue
                    .Where(x => x.ReceivedAt >= start && x.ReceivedAt <= end)
                    .ToArray();
            }
        }

        private static void Trim(Queue<BufferedCanFrame> queue, DateTimeOffset oldest)
        {
            while (queue.Count > 0 && queue.Peek().ReceivedAt < oldest) queue.Dequeue();
        }
    }

    private sealed record BufferedCanFrame(CanFrame Frame, DateTimeOffset ReceivedAt);

    private sealed class UsbTestExecutionPort(UsbLink usb, Func<ParseParameters, DateTimeOffset, (bool Found, ObservationSample Sample)> observation, Action<string> log) : ITestExecutionPort
    {
        public async Task<HardwareCommandResult> SendBroadcastAsync(BroadcastParameters broadcast, CancellationToken cancellationToken)
        {
            if (!TryData(broadcast.DataHex, broadcast.Dlc, out var data)) return HardwareCommandResult.Fail("报文数据格式错误。");
            var result = await usb.SendAndWaitAsync(Frame.CMD_CAN_SEND, Frame.CanSendPayload(1, broadcast.Extended, broadcast.Id, broadcast.Dlc, data));
            return result.ok && result.status == Frame.STS_OK ? HardwareCommandResult.Ok() : HardwareCommandResult.Fail(result.err.Length > 0 ? result.err : Frame.StatusName(result.status));
        }

        public async Task<HardwareCommandResult> StartPeriodicBroadcastAsync(IReadOnlyList<BroadcastParameters> broadcasts, CancellationToken cancellationToken)
        {
            if (broadcasts.Count is < 1 or > Frame.MAX_BC_FRAMES) return HardwareCommandResult.Fail($"周期广播帧数必须为 1~{Frame.MAX_BC_FRAMES}。");
            var frames = new List<(byte ide, uint id, ushort periodMs, byte dlc, byte[] data)>();
            foreach (var broadcast in broadcasts)
            {
                if (broadcast.PeriodMs is < 1 or > ushort.MaxValue || !TryData(broadcast.DataHex, broadcast.Dlc, out var data)) return HardwareCommandResult.Fail("周期广播参数无效。");
                frames.Add((broadcast.Extended ? (byte)1 : (byte)0, broadcast.Id, (ushort)broadcast.PeriodMs, broadcast.Dlc, data));
            }
            var set = await usb.SendAndWaitAsync(Frame.CMD_SET_BROADCAST, Frame.SetBroadcastPayload(frames));
            if (!set.ok || set.status != Frame.STS_OK) return HardwareCommandResult.Fail(set.err.Length > 0 ? set.err : Frame.StatusName(set.status));
            var start = await usb.SendAndWaitAsync(Frame.CMD_BROADCAST_START, []);
            if (!start.ok || start.status != Frame.STS_OK) return HardwareCommandResult.Fail(start.err.Length > 0 ? start.err : Frame.StatusName(start.status));
            log($"自动测试启动周期广播：{frames.Count} 帧。");
            return HardwareCommandResult.Ok();
        }

        public async Task<HardwareCommandResult> StopPeriodicBroadcastAsync(CancellationToken cancellationToken)
        {
            var stop = await usb.SendAndWaitAsync(Frame.CMD_BROADCAST_STOP, []);
            if (!stop.ok || stop.status != Frame.STS_OK) return HardwareCommandResult.Fail(stop.err.Length > 0 ? stop.err : Frame.StatusName(stop.status));
            log("自动测试停止周期广播。");
            return HardwareCommandResult.Ok();
        }

        public async Task<HardwareCommandResult> SetPowerAsync(PowerParameters power, CancellationToken cancellationToken)
        {
            var command = power.Target switch { PowerTarget.ECU主电源 => Frame.CMD_SET_ECU_PWR, PowerTarget.IGN => Frame.CMD_SET_IGN, _ => Frame.CMD_SET_KEYPWR };
            var result = await usb.SendAndWaitAsync(command, Frame.PowerPayload(power.On));
            return result.ok && result.status == Frame.STS_OK ? HardwareCommandResult.Ok() : HardwareCommandResult.Fail(result.err.Length > 0 ? result.err : Frame.StatusName(result.status));
        }

        public async Task<HardwareCommandResult> SetActionAsync(ActionParameters action, CancellationToken cancellationToken)
        {
            var result = await usb.SendAndWaitAsync(Frame.CMD_CAN_SEND, Frame.KeypadOutputPayload(action.OutputMask));
            return result.ok && result.status == Frame.STS_OK ? HardwareCommandResult.Ok() : HardwareCommandResult.Fail(result.err.Length > 0 ? result.err : Frame.StatusName(result.status));
        }

        public bool TryGetLatest(ParseParameters parse, DateTimeOffset notBefore, out ObservationSample sample)
        {
            var found = observation(parse, notBefore);
            sample = found.Sample;
            return found.Found;
        }
    }

    private sealed record UiLayout(double LeftWidth, double RightWidth, double LogHeight, double LogWidth = 0);
    private sealed record LiveRow(string Name, string Value);
    private sealed record SequenceChoice(string Id, string Name);
    private sealed record FolderChoice(ConfigurationTreeItem Item, string Path);
    private sealed record DefinitionChoice(string DisplayName, CanMessageDefinition? Value, string? ToolTip = null, CanSignalDefinition? Signal = null);
    private sealed record ResourceChoice(string Id, string SequenceId, string Name, string KindLabel, string Summary, ConfigurationSequence Sequence)
    {
        public string DisplayName => $"{Name} · {KindLabel}";
    }
    private sealed record ParseResourceChoice(string Id, string Name, string Summary);
    private sealed record RecentConfig(string Name, string Path);
    private sealed record RunTimelineRow(string Time, string Name, string Kind, string Status);
    private sealed record UiLogEntry(string Time, string Level, string Source, string Message);
    private sealed class PendingRaw(bool ide, uint id)
    {
        public bool Ide { get; } = ide;
        public uint Id { get; } = id;
        private CanFrame? _latest;
        private long _lastTime = -1;
        private readonly Queue<long> _intervals = new();

        public void Apply(CanFrame frame)
        {
            if (_lastTime >= 0)
            {
                var interval = frame.TimeMs - _lastTime;
                // USB 接收可能在同一毫秒内连续交付多帧。零值/倒退值不是周期，不能覆盖有效测量。
                if (interval > 0 && interval <= 60_000)
                {
                    _intervals.Enqueue(interval);
                    while (_intervals.Count > 8) _intervals.Dequeue();
                }
            }
            _lastTime = frame.TimeMs;
            _latest = frame with { Data = frame.Data.Take(frame.Dlc).ToArray() };
        }

        public PendingRawSnapshot Snapshot()
        {
            long? period = null;
            if (_intervals.Count > 0)
            {
                var values = _intervals.OrderBy(x => x).ToArray();
                period = values[values.Length / 2];
            }
            return new(Ide, Id, _latest!.Value, period);
        }
    }

    private sealed record PendingRawSnapshot(bool Ide, uint Id, CanFrame Latest, long? Period);

    private sealed class RawRow(bool ide, uint id)
    {
        public string Id { get; } = ide ? $"0x{id:X8}" : $"0x{id:X3}";
        public string Data { get; private set; } = "";
        public string Period { get; private set; } = "—";

        public void Apply(PendingRawSnapshot update)
        {
            Data = string.Join(" ", update.Latest.Data.Take(update.Latest.Dlc).Select(x => x.ToString("X2")));
            if (update.Period is long period) Period = $"{period} ms";
        }
    }
}


