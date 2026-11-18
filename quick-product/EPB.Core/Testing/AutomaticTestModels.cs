using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using EPB.Core.Configuration;

namespace EPB.Core.Testing;

/// <summary>一次自动测试计划。资源配置与测试计划分开保存，测试计划只组织要执行的用例。</summary>
public sealed class TestPlan : INotifyPropertyChanged
{
    private string _id = NewId();
    private string _name = "自动测试";
    private ObservableCollection<TestCaseDefinition> _cases = [];
    public string Id { get => _id; set => SetId(ref _id, value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string NodeId => Id;
    public string Name { get => _name; set => SetValue(ref _name, value); }
    public ObservableCollection<TestCaseDefinition> Cases { get => _cases; set => ReplaceCollection(ref _cases, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private static string NewId() => Guid.NewGuid().ToString("N");
    private void SetId(ref string field, string? value) { var next = string.IsNullOrWhiteSpace(value) ? NewId() : value; if (field == next) return; field = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); }
    private void SetValue(ref string field, string? value) { value ??= string.Empty; if (field == value) return; field = value; OnPropertyChanged(); }
    private void ReplaceCollection<T>(ref ObservableCollection<T> field, ObservableCollection<T>? value) { var next = value ?? []; if (ReferenceEquals(field, next)) return; field = next; OnPropertyChanged(); }
}

public sealed class TestCaseDefinition : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "新测试用例";
    private bool _enabled = true;
    private int _order;
    private int _waitAfterMs;
    private TestJudgementLogic _judgementLogic = TestJudgementLogic.全部满足;
    private TestResultStatus _lastStatus = TestResultStatus.未运行;
    private ObservableCollection<TestFlowNode> _flow = [];
    public string Id { get => _id; set { var next = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value; if (_id == next) return; _id = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string NodeId => Id;
    public string Name { get => _name; set { value ??= string.Empty; if (_name == value) return; _name = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); } }
    public int Order { get => _order; set { if (_order == value) return; _order = value; OnPropertyChanged(); } }
    /// <summary>本用例完成后、下一个启用用例开始前的等待时间（毫秒）。</summary>
    public int WaitAfterMs { get => _waitAfterMs; set { if (_waitAfterMs == value) return; _waitAfterMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(WaitSummary)); } }
    /// <summary>本用例中多个带期望的报文解析节点如何合并判定。</summary>
    public TestJudgementLogic JudgementLogic { get => _judgementLogic; set { if (_judgementLogic == value) return; _judgementLogic = value; OnPropertyChanged(); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string WaitSummary => WaitAfterMs > 0 ? $"后等待 {WaitAfterMs} ms" : string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public TestResultStatus LastStatus { get => _lastStatus; set { if (_lastStatus == value) return; _lastStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusSummary)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusSummary => LastStatus switch
    {
        TestResultStatus.通过 => "通过",
        TestResultStatus.判定失败 => "判定失败",
        TestResultStatus.执行错误 => "执行错误",
        TestResultStatus.运行中 => "运行中",
        TestResultStatus.跳过 or TestResultStatus.未进入 => "跳过",
        TestResultStatus.仅观察 => "仅观察",
        _ => "未运行"
    };
    public ObservableCollection<TestFlowNode> Flow { get => _flow; set { if (ReferenceEquals(_flow, value)) return; _flow = value ?? []; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public enum TestJudgementLogic
{
    全部满足,
    任一满足
}

public enum TestFlowNodeKind
{
    引用序列,
    原子操作,
    组合
}

/// <summary>测试用例中的流程节点。步骤通过 ResourceId 绑定资源；等待和组合可以直接存在于测试用例中。</summary>
public sealed class TestFlowNode : INotifyPropertyChanged
{
    private string _name = "新流程节点";
    private string _displaySummary = "";
    private string? _displayKindOverride;
    private TestResultStatus _lastStatus = TestResultStatus.未运行;
    private bool _enabled = true;
    private TestFlowNodeKind _kind = TestFlowNodeKind.引用序列;
    private string _resourceId = "";
    private string _sequenceId = "";
    private ConfigurationStep? _operation;
    private bool _nameIsUserDefined;
    private ObservableCollection<TestFlowNode> _children = [];
    private string _id = Guid.NewGuid().ToString("N");
    public TestFlowNode() => _children.CollectionChanged += ChildrenChanged;
    public string Id { get => _id; set { var next = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value; if (_id == next) return; _id = next; OnPropertyChanged(); OnPropertyChanged(nameof(NodeId)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string NodeId => Id;
    public string Name { get => _name; set { value ??= string.Empty; if (_name == value) return; _name = value; OnPropertyChanged(); } }
    /// <summary>用户明确编辑过节点名称后，摘要刷新不得再自动生成覆盖。</summary>
    public bool NameIsUserDefined { get => _nameIsUserDefined; set { if (_nameIsUserDefined == value) return; _nameIsUserDefined = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); } }
    public TestFlowNodeKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayKind)); OnPropertyChanged(nameof(DisplaySummary)); } }
    /// <summary>统一资源引用 Id；可以指向原子操作或流程。</summary>
    public string ResourceId { get => _resourceId; set { value ??= string.Empty; if (_resourceId == value) return; _resourceId = value; OnPropertyChanged(); } }
    /// <summary>旧配置字段。新建配置使用 ResourceId，运行时仍接受旧流程 Id。</summary>
    public string SequenceId { get => _sequenceId; set { value ??= string.Empty; if (_sequenceId == value) return; _sequenceId = value; OnPropertyChanged(); } }
    public ConfigurationStep? Operation { get => _operation; set { if (ReferenceEquals(_operation, value)) return; if (_operation is not null) _operation.PropertyChanged -= OperationChanged; _operation = value; if (_operation is not null) _operation.PropertyChanged += OperationChanged; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayKind)); OnPropertyChanged(nameof(DisplaySummary)); } }
    /// <summary>报文解析步骤的测试判定。解析字段来自资源，期望和超时只属于当前测试步骤。</summary>
    public ParseAssertion? ParseAssertion { get; set; }
    /// <summary>流程控制引用；控制节点不复制操作定义。</summary>
    public TestFlowControlDefinition? FlowControl { get; set; }
    /// <summary>控制节点下的固定路径容器标记。</summary>
    public TestFlowPathKind PathKind { get; set; }
    /// <summary>CASE 路径的匹配值；其它路径为空。</summary>
    public string PathValue { get; set; } = "";
    public ObservableCollection<TestFlowNode> Children { get => _children; set { if (ReferenceEquals(_children, value)) return; if (_children is not null) _children.CollectionChanged -= ChildrenChanged; _children = value ?? []; _children.CollectionChanged += ChildrenChanged; OnPropertyChanged(); OnPropertyChanged(nameof(DisplaySummary)); } }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySummary { get => _displaySummary; set { if (_displaySummary == value) return; _displaySummary = value; OnPropertyChanged(); } }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? DisplayKindOverride { get => _displayKindOverride; set { if (_displayKindOverride == value) return; _displayKindOverride = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayKind)); } }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayKind => DisplayKindOverride ?? Operation?.DisplayKind ?? (Kind == TestFlowNodeKind.引用序列 ? "步骤" : Kind.ToString());

    [System.Text.Json.Serialization.JsonIgnore]
    public TestResultStatus LastStatus { get => _lastStatus; set { if (_lastStatus == value) return; _lastStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusSummary)); } }

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusSummary => LastStatus switch
    {
        TestResultStatus.通过 => "通过",
        TestResultStatus.判定失败 => "判定失败",
        TestResultStatus.执行错误 => "执行错误",
        TestResultStatus.运行中 => "运行中",
        TestResultStatus.跳过 or TestResultStatus.未进入 => "跳过",
        TestResultStatus.仅观察 => "仅观察",
        _ => "未运行"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OperationChanged(object? sender, PropertyChangedEventArgs e) { OnPropertyChanged(nameof(DisplayKind)); OnPropertyChanged(nameof(DisplaySummary)); }
    private void ChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(DisplaySummary));
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ParseAssertion
{
    /// <summary>引用配置树中的解析资源步骤 Id。</summary>
    public string ParseResourceId { get; set; } = "";
    /// <summary>图形化比较条件序列化后的表达式，如 =5、>=3、10..20；为空表示仅观察。</summary>
    public string Expectation { get; set; } = "";
    /// <summary>从本步骤开始等待条件成立的最长时间，单位毫秒。</summary>
    public int TimeoutMs { get; set; } = 5000;
}

public enum TestResultStatus
{
    未运行,
    运行中,
    通过,
    判定失败,
    执行错误,
    跳过,
    未进入,
    仅观察,
    已取消
}

public sealed record ObservationSample(
    double Value,
    string RawFrame,
    DateTimeOffset Timestamp,
    string? Unit = null);

public enum ParseExecutionOutcome
{
    NoMatchingFrame,
    StaleFrame,
    DecodeInvalid,
    ValueMismatch,
    ExpectationMet,
    ReceiveError
}

public sealed record HardwareCommandResult(bool Succeeded, string? Error = null)
{
    public static HardwareCommandResult Ok() => new(true);
    public static HardwareCommandResult Fail(string error) => new(false, error);
}

public sealed class TestNodeResult
{
    public string NodeId { get; init; } = "";
    public string Name { get; init; } = "";
    public TestResultStatus Status { get; set; } = TestResultStatus.未运行;
    public bool HasExpectation { get; set; }
    public double? ActualValue { get; set; }
    public string? Expected { get; set; }
    public string? RawFrame { get; set; }
    public string? Error { get; set; }
    public ParseExecutionOutcome? ParseOutcome { get; set; }
    /// <summary>解析步骤产生的后端中间结果；IF/WHEN 只应消费其中的 Verdict。</summary>
    public ProducedSignalResult? ProducedSignal { get; set; }
    /// <summary>控制节点实际选择的路径；控制本身不产生通过/失败判定。</summary>
    public TestFlowPathKind? SelectedPath { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<TestNodeResult> Children { get; } = [];
}

public sealed class TestCaseResult
{
    public string CaseId { get; init; } = "";
    public string Name { get; init; } = "";
    public TestResultStatus Status { get; set; } = TestResultStatus.未运行;
    public List<TestNodeResult> Nodes { get; } = [];
    public string? Error { get; set; }
}

public sealed class TestPlanResult
{
    public string PlanId { get; init; } = "";
    public string Name { get; init; } = "";
    public TestResultStatus Status { get; set; } = TestResultStatus.未运行;
    public string? Error { get; set; }
    public List<TestCaseResult> Cases { get; } = [];
}

/// <summary>后端唯一依赖的测试工具能力。USB、主控板和按键板协议由宿主适配器实现。</summary>
public interface ITestExecutionPort
{
    Task<HardwareCommandResult> SendBroadcastAsync(BroadcastParameters broadcast, CancellationToken cancellationToken);
    Task<HardwareCommandResult> StartPeriodicBroadcastAsync(IReadOnlyList<BroadcastParameters> broadcasts, CancellationToken cancellationToken);
    Task<HardwareCommandResult> StopPeriodicBroadcastAsync(CancellationToken cancellationToken);
    Task<HardwareCommandResult> SetPowerAsync(PowerParameters power, CancellationToken cancellationToken);
    Task<HardwareCommandResult> SetActionAsync(ActionParameters action, CancellationToken cancellationToken);
    bool TryGetLatest(ParseParameters parse, DateTimeOffset notBefore, out ObservationSample sample);

    /// <summary>
    /// 读取后端信号结果。默认实现兼容现有硬件适配器；新的适配器可直接返回中性结果。
    /// </summary>
    SignalReadResult ReadSignal(SignalReadDefinition definition, DateTimeOffset notBefore)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var parse = definition.ToConfigurationParameters();
        return TryGetLatest(parse, notBefore, out var sample)
            ? SignalReadResult.Received(new SignalObservation(sample.Value, sample.RawFrame, sample.Timestamp, sample.Unit))
            : SignalReadResult.Unavailable();
    }
}
