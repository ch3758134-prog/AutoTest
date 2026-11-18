using EPB.Core.Communication;
using EPB.Core.Configuration;

namespace EPB.Core.Testing;

public sealed record ExecutionPreflightDiagnostic(
    string Code,
    string CaseName,
    string NodeName,
    string StepName,
    string DefinitionId,
    string Message);

public sealed class ExecutionPlan
{
    public IReadOnlyDictionary<string, ConfigurationStep> StepsById { get; }
    public IReadOnlyList<ExecutionPreflightDiagnostic> Diagnostics { get; }
    public bool CanRun => Diagnostics.Count == 0;

    public ExecutionPlan(
        IReadOnlyDictionary<string, ConfigurationStep> stepsById,
        IReadOnlyList<ExecutionPreflightDiagnostic> diagnostics)
    {
        StepsById = stepsById;
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// Builds an immutable Core-only execution snapshot before any port operation.
/// WPF state and the mutable definition cache are intentionally not consulted later.
/// </summary>
public sealed class ExecutionPreflightService
{
    private readonly CommunicationDefinitionResolver _resolver = new();

    public ExecutionPlan Build(EcuConfiguration configuration, TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(plan);

        var snapshots = new Dictionary<string, ConfigurationStep>(StringComparer.Ordinal);
        var diagnostics = new List<ExecutionPreflightDiagnostic>();
        var visitedSequences = new HashSet<string>(StringComparer.Ordinal);

        // The plan supplied by the caller is the execution scope. Root is only
        // used as an index for resources explicitly referenced by those cases;
        // unrelated resources must not block a single-case run.
        foreach (var testCase in plan.Cases.Where(x => x.Enabled).OrderBy(x => x.Order))
            foreach (var node in testCase.Flow)
                InspectNode(node, testCase.Name, configuration, snapshots, diagnostics, visitedSequences);

        foreach (var testCase in plan.Cases.Where(x => x.Enabled).OrderBy(x => x.Order))
            ValidateFlowControls(testCase.Flow, testCase.Name, configuration, new Dictionary<string, TestFlowNode>(StringComparer.Ordinal), diagnostics);

        return new ExecutionPlan(snapshots, diagnostics);
    }

    private static void ValidateFlowControls(
        IReadOnlyList<TestFlowNode> nodes,
        string caseName,
        EcuConfiguration configuration,
        IDictionary<string, TestFlowNode> availableResults,
        ICollection<ExecutionPreflightDiagnostic> diagnostics)
    {
        foreach (var node in nodes)
        {
            if (node.FlowControl is { } control)
            {
                ValidateControl(node, control, caseName, configuration, availableResults, diagnostics);
                foreach (var path in node.Children.Where(x => x.PathKind != TestFlowPathKind.None))
                {
                    var pathResults = new Dictionary<string, TestFlowNode>(availableResults, StringComparer.Ordinal);
                    ValidateFlowControls(path.Children, caseName, configuration, pathResults, diagnostics);
                }
                continue;
            }

            if (node.Kind == TestFlowNodeKind.组合)
                ValidateFlowControls(node.Children, caseName, configuration, availableResults, diagnostics);

            if (IsVerdictProducer(node))
            {
                availableResults[node.Id] = node;
                if (node.Operation is not null) availableResults[node.Operation.Id] = node;
            }
        }
    }

    private static void ValidateControl(
        TestFlowNode node,
        TestFlowControlDefinition control,
        string caseName,
        EcuConfiguration configuration,
        IDictionary<string, TestFlowNode> availableResults,
        ICollection<ExecutionPreflightDiagnostic> diagnostics)
    {
        var paths = node.Children.GroupBy(x => x.PathKind).ToDictionary(x => x.Key, x => x.ToList());
        void Add(string code, string message, string definitionId = "")
            => diagnostics.Add(new(code, caseName, node.Name, node.Name, definitionId, message));

        switch (control.Kind)
        {
            case TestFlowControlKind.Condition:
                ValidateVerdictReference(control, availableResults, node, Add);
                if (paths.GetValueOrDefault(TestFlowPathKind.Then)?.Count != 1)
                    Add("ControlPathMissing", "条件控制必须存在且只能存在一个 Then 路径。");
                if (paths.GetValueOrDefault(TestFlowPathKind.Else)?.Count > 1)
                    Add("ControlPathDuplicate", "条件控制最多只能存在一个 Else 路径。");
                if (node.Children.Any(x => x.PathKind is not TestFlowPathKind.Then and not TestFlowPathKind.Else))
                    Add("ControlPathInvalid", "条件控制只能包含 Then 或 Else 路径。");
                break;
            case TestFlowControlKind.WaitForVerdict:
                ValidateVerdictReference(control, availableResults, node, Add);
                if (control.PeriodMs <= 0) Add("ControlTimingInvalid", "等待控制的周期必须大于 0 毫秒。");
                if (control.TimeoutMs <= 0) Add("ControlTimingInvalid", "等待控制的超时必须大于 0 毫秒。");
                if (paths.GetValueOrDefault(TestFlowPathKind.Hit)?.Count != 1 ||
                    paths.GetValueOrDefault(TestFlowPathKind.Timeout)?.Count != 1)
                    Add("ControlPathMissing", "等待控制必须存在且只能存在 Hit、Timeout 两条固定路径。");
                if (node.Children.Any(x => x.PathKind is not TestFlowPathKind.Hit and not TestFlowPathKind.Timeout))
                    Add("ControlPathInvalid", "等待控制只能包含 Hit 或 Timeout 路径。");
                break;
            case TestFlowControlKind.ValueBranch:
                if (control.SignalDefinition is null ||
                    string.IsNullOrWhiteSpace(control.SignalDefinition.MessageDefinitionId) ||
                    string.IsNullOrWhiteSpace(control.SignalDefinition.SignalDefinitionId))
                    Add("SignalDefinitionMissing", "值分支必须绑定通信数据库中的报文和信号定义。");
                else if (FindSignal(configuration.CommunicationDatabase, control.SignalDefinition) is null)
                    Add("SignalDefinitionMissing", "值分支引用的通信数据库信号不存在。", control.SignalDefinition.SignalDefinitionId);
                if (paths.GetValueOrDefault(TestFlowPathKind.Default)?.Count != 1)
                    Add("ControlPathMissing", "值分支必须存在且只能存在一个 Default 路径。");
                if (node.Children.Any(x => x.PathKind is not TestFlowPathKind.Case and not TestFlowPathKind.Default))
                    Add("ControlPathInvalid", "值分支只能包含 Case 或 Default 路径。");
                var duplicateCases = node.Children
                    .Where(x => x.PathKind == TestFlowPathKind.Case)
                    .GroupBy(x => x.PathValue.Trim(), StringComparer.Ordinal)
                    .Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
                foreach (var duplicate in duplicateCases)
                    Add("ControlCaseInvalid", string.IsNullOrWhiteSpace(duplicate.Key)
                        ? "Case 路径必须配置匹配值。"
                        : $"Case 匹配值重复：{duplicate.Key}。");
                break;
            default:
                Add("ControlKindInvalid", $"不支持的流程控制类型：{control.Kind}。");
                break;
        }
    }

    private static void ValidateVerdictReference(
        TestFlowControlDefinition control,
        IDictionary<string, TestFlowNode> availableResults,
        TestFlowNode node,
        Action<string, string, string> add)
    {
        if (control.ResultReference is null ||
            string.IsNullOrWhiteSpace(control.ResultReference.ProducerNodeId))
        {
            add("ResultReferenceMissing", "流程控制必须引用当前作用域中此前的结果步骤。", "");
            return;
        }
        if (control.ResultReference.Channel != ResultChannel.Verdict)
        {
            add("ResultChannelInvalid", "IF/WHEN 只能消费已有步骤的 Verdict。", control.ResultReference.ProducerNodeId);
            return;
        }
        if (!availableResults.TryGetValue(control.ResultReference.ProducerNodeId, out var producer))
        {
            add("ResultReferenceMissing", "流程控制只能引用当前作用域中此前已经执行的结果步骤。", control.ResultReference.ProducerNodeId);
            return;
        }
        if (!IsVerdictProducer(producer))
            add("ResultVerdictMissing", "被引用步骤没有可供 IF/WHEN 消费的 Verdict。", producer.Id);
    }

    private static bool IsVerdictProducer(TestFlowNode node)
    {
        if (node.Operation?.Kind != ConfigurationStepKind.报文解析) return false;
        var expectation = node.ParseAssertion?.Expectation ?? node.Operation.Parse.Expectation;
        return !string.IsNullOrWhiteSpace(expectation);
    }

    private static (CanMessageDefinition Message, CanSignalDefinition Signal)? FindSignal(
        CanDatabase? database,
        SignalDefinitionReference reference)
    {
        var message = database?.Messages.FirstOrDefault(x => x.DefinitionId == reference.MessageDefinitionId);
        var signal = message?.Signals.FirstOrDefault(x => x.DefinitionId == reference.SignalDefinitionId);
        return message is null || signal is null ? null : (message, signal);
    }

    private void InspectNode(
        TestFlowNode node,
        string caseName,
        EcuConfiguration configuration,
        IDictionary<string, ConfigurationStep> snapshots,
        ICollection<ExecutionPreflightDiagnostic> diagnostics,
        ISet<string> visitedSequences)
    {
        if (node.Operation is not null)
        {
            var step = CloneStep(node.Operation);
            if (node.ParseAssertion is { } assertion && step.Kind == ConfigurationStepKind.报文解析)
            {
                step.Parse.Expectation = assertion.Expectation;
                step.Parse.TimeoutMs = Math.Max(0, assertion.TimeoutMs);
            }
            InspectStep(step, caseName, node.Name, caseName, configuration, snapshots, diagnostics, visitedSequences);
            // An inline operation is authoritative. Stale ResourceId/SequenceId
            // fields are editing residue and must not turn it into a reference.
            foreach (var child in node.Children)
                InspectNode(child, caseName, configuration, snapshots, diagnostics, visitedSequences);
            return;
        }

        foreach (var child in node.Children)
            InspectNode(child, caseName, configuration, snapshots, diagnostics, visitedSequences);

        var referenceId = !string.IsNullOrWhiteSpace(node.ResourceId) ? node.ResourceId : node.SequenceId;
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            if (node.Operation is null && node.Kind != TestFlowNodeKind.组合)
                diagnostics.Add(new("ReferenceMissing", caseName, node.Name, node.Name, "", "引用节点未选择资源或序列。"));
            return;
        }

        var sequence = FindSequence(configuration.Root, referenceId);
        if (sequence is null)
        {
            diagnostics.Add(new("ReferenceMissing", caseName, node.Name, node.Name, referenceId, $"引用对象不存在：节点“{node.Name}”，引用 ID={referenceId}"));
            return;
        }
        InspectSequence(sequence, caseName, node.Name, configuration, snapshots, diagnostics, visitedSequences);
    }

    private void InspectSequence(
        ConfigurationSequence sequence,
        string caseName,
        string nodeName,
        EcuConfiguration configuration,
        IDictionary<string, ConfigurationStep> snapshots,
        ICollection<ExecutionPreflightDiagnostic> diagnostics,
        ISet<string> visitedSequences)
    {
        if (!visitedSequences.Add(sequence.Id)) return;
        foreach (var step in sequence.Steps)
            InspectStep(step, caseName, nodeName, caseName, configuration, snapshots, diagnostics, visitedSequences);
    }

    private void InspectStep(
        ConfigurationStep original,
        string? caseName,
        string nodeName,
        string context,
        EcuConfiguration configuration,
        IDictionary<string, ConfigurationStep> snapshots,
        ICollection<ExecutionPreflightDiagnostic> diagnostics,
        ISet<string> visitedSequences)
    {
        var step = CloneStep(original);
        step.Parse.Expectation = original.Parse.Expectation;
        step.Parse.TimeoutMs = original.Parse.TimeoutMs;
        var resolution = Resolve(step, configuration.CommunicationDatabase);
        if (!resolution.Succeeded)
        {
            foreach (var item in resolution.Diagnostics)
                diagnostics.Add(new(
                    item.Status.ToString(),
                    caseName ?? context,
                    nodeName,
                    original.Name,
                    DefinitionIdFor(step),
                    item.Message));
        }
        else
        {
            CommunicationDefinitionResolver.ApplyCanonicalProjection(step, resolution);
            var databaseBound = IsDatabaseBound(step);
            if (databaseBound)
                snapshots.TryAdd(original.Id, step);
        }

        foreach (var child in original.Children)
            InspectStep(child, caseName, nodeName, context, configuration, snapshots, diagnostics, visitedSequences);

        if (original.Kind == ConfigurationStepKind.引用序列)
        {
            var referenceId = original.Reference.SequenceId;
            var sequence = string.IsNullOrWhiteSpace(referenceId) ? null : FindSequence(configuration.Root, referenceId);
            if (sequence is null)
                diagnostics.Add(new("ReferenceMissing", caseName ?? context, nodeName, original.Name, referenceId ?? "", "引用步骤未找到目标序列。"));
            else
                InspectSequence(sequence, caseName ?? context, nodeName, configuration, snapshots, diagnostics, visitedSequences);
        }
    }

    private CommunicationDefinitionResolution Resolve(ConfigurationStep step, CanDatabase? database)
    {
        if (step.Kind is ConfigurationStepKind.广播报文 && step.Broadcast.Source == ConfigurationSource.通信数据库 ||
            step.Kind is ConfigurationStepKind.报文解析 && step.Parse.Source == ConfigurationSource.通信数据库 ||
            step.Kind is ConfigurationStepKind.波形判定 && step.WaveformAssertion.Source == ConfigurationSource.通信数据库)
        {
            return database is null
                ? CommunicationDefinitionResolution.Missing(CommunicationDefinitionResolutionStatus.MessageMissing, "步骤声明通信数据库来源，但当前配置未加载通信数据库。")
                : _resolver.Resolve(step, database);
        }

        return _resolver.Resolve(step, database ?? new CanDatabase());
    }

    private static bool IsDatabaseBound(ConfigurationStep step)
        => step.Kind switch
        {
            ConfigurationStepKind.广播报文 => step.Broadcast.Source == ConfigurationSource.通信数据库,
            ConfigurationStepKind.报文解析 => step.Parse.Source == ConfigurationSource.通信数据库,
            ConfigurationStepKind.波形判定 => step.WaveformAssertion.Source == ConfigurationSource.通信数据库,
            _ => false
        };

    private static string DefinitionIdFor(ConfigurationStep step)
        => step.Kind switch
        {
            ConfigurationStepKind.广播报文 => step.Broadcast.MessageDefinitionId,
            ConfigurationStepKind.报文解析 => step.Parse.MessageDefinitionId,
            ConfigurationStepKind.波形判定 => step.WaveformAssertion.MessageDefinitionId,
            _ => ""
        };

    private static ConfigurationSequence? FindSequence(ConfigurationTreeItem item, string id)
    {
        if (item.Sequence is { } sequence && (item.Id == id || sequence.Id == id)) return sequence;
        foreach (var child in item.Children)
        {
            var found = FindSequence(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ConfigurationStep CloneStep(ConfigurationStep step)
        => ConfigurationJson.Deserialize(ConfigurationJson.Serialize(new EcuConfiguration
        {
            Root = new ConfigurationTreeItem
            {
                Sequence = new ConfigurationSequence { Steps = [step] }
            }
        })).Root.Sequence!.Steps[0];
}
