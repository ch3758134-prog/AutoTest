using EPB.Core.Testing;

namespace EPB.Core.Configuration;

public enum ResourceRelationState
{
    独立步骤,
    共享引用,
    空引用,
    引用失效
}

public sealed record ResourceRelationDiagnostic(
    string Code,
    string NodeId,
    string NodeName,
    string ResourceId,
    string SequenceId,
    string Message);

public sealed record ResourceRelationResolution(
    ResourceRelationState State,
    ConfigurationSequence? Sequence,
    ResourceRelationDiagnostic? Diagnostic)
{
    public bool IsUsableReference => State == ResourceRelationState.共享引用 && Sequence is not null;
}

public sealed record ResourceSaveResult(
    bool Succeeded,
    ConfigurationTreeItem? Item,
    ResourceRelationDiagnostic? Diagnostic);

public sealed record ResourceDeleteResult(
    bool Succeeded,
    IReadOnlyList<ResourceRelationDiagnostic> AffectedReferences,
    ResourceRelationDiagnostic? Diagnostic);

/// <summary>
/// Core contract for shared resource relations. It deliberately contains no UI
/// concerns: callers choose buttons/menus, while this service owns relation state.
/// </summary>
public sealed class ResourceRelationService
{
    public ResourceRelationResolution Resolve(EcuConfiguration configuration, TestFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(node);

        if (node.Operation is not null)
            return new(ResourceRelationState.独立步骤, null, null);
        if (string.IsNullOrWhiteSpace(node.ResourceId) && string.IsNullOrWhiteSpace(node.SequenceId))
            return new(ResourceRelationState.空引用, null, Diagnostic(node, "EmptyReference", "引用节点未选择资源或序列。"));

        var sequence = !string.IsNullOrWhiteSpace(node.ResourceId)
            ? FindSequenceByResourceId(configuration.Root, node.ResourceId)
            : configuration.FindSequence(node.SequenceId);
        if (sequence is not null)
            return new(ResourceRelationState.共享引用, sequence, null);

        var reference = !string.IsNullOrWhiteSpace(node.ResourceId) ? node.ResourceId : node.SequenceId;
        return new(ResourceRelationState.引用失效, null,
            Diagnostic(node, "MissingReference", $"引用对象不存在：节点“{node.Name}”，引用 ID={reference}。"));
    }

    /// <summary>Explicitly detaches a shared reference by copying its current content.</summary>
    public ResourceRelationResolution CopyAsLocal(EcuConfiguration configuration, TestFlowNode node)
    {
        var relation = Resolve(configuration, node);
        if (!relation.IsUsableReference) return relation;

        var target = relation.Sequence!;
        if (target.Steps.Count == 1)
        {
            var source = target.Steps[0];
            var operation = CloneStep(source);
            operation.RegenerateIdsRecursively();
            node.Kind = TestFlowNodeKind.原子操作;
            node.Operation = operation;
            node.ParseAssertion = CloneAssertion(node.ParseAssertion, source.Id);
            node.Children.Clear();
        }
        else
        {
            node.Kind = TestFlowNodeKind.组合;
            node.Operation = null;
            node.Children.Clear();
            foreach (var source in target.Steps)
            {
                var operation = CloneStep(source);
                operation.RegenerateIdsRecursively();
                node.Children.Add(new TestFlowNode
                {
                    Name = operation.Name,
                    Kind = TestFlowNodeKind.原子操作,
                    Operation = operation,
                    ParseAssertion = CloneAssertion(node.ParseAssertion, source.Id)
                });
            }
        }
        node.ResourceId = string.Empty;
        node.SequenceId = string.Empty;
        node.Name = target.Name;
        return new(ResourceRelationState.独立步骤, null, null);
    }

    /// <summary>Creates a new resource; binding is explicit and never implicit.</summary>
    public ResourceSaveResult SaveStepAsResource(
        ConfigurationTreeItem parent,
        ConfigurationStep step,
        bool bindCurrentStep,
        TestFlowNode? currentNode = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(step);
        if (parent.Kind != ConfigurationItemKind.文件夹)
            return new(false, null, new("InvalidParent", currentNode?.Id ?? "", currentNode?.Name ?? step.Name, "", "", "资源必须保存到文件夹中。"));
        if (bindCurrentStep && currentNode is null)
            return new(false, null, new("MissingNode", "", step.Name, "", "", "保存并绑定资源必须指定当前测试步骤。"));

        var item = parent.AddAtomicOperation(step.Kind);
        var copy = CloneStep(step);
        copy.RegenerateIdsRecursively();
        item.Sequence!.Steps.Clear();
        item.Sequence.Steps.Add(copy);
        item.Name = copy.Name;
        item.Sequence.Name = copy.Name;

        if (bindCurrentStep)
        {
            var node = currentNode!;
            node.Operation = null;
            node.ResourceId = item.Id;
            node.SequenceId = item.Sequence.Id;
            node.Name = copy.Name;
            if (copy.Kind == ConfigurationStepKind.报文解析)
            {
                node.ParseAssertion ??= new ParseAssertion();
                node.ParseAssertion.ParseResourceId = copy.Id;
            }
        }
        return new(true, item, null);
    }

    /// <summary>Reports references before deletion; references remain persisted and become diagnosable.</summary>
    public ResourceDeleteResult DeleteResource(EcuConfiguration configuration, string resourceId)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(resourceId))
            return new(false, [], new("MissingResourceId", "", "", resourceId ?? "", "", "资源 ID 不能为空。"));

        var item = FindItem(configuration.Root, resourceId);
        if (item is null)
            return new(false, [], new("MissingResource", "", "", resourceId, "", $"资源不存在：{resourceId}。"));

        var sequenceId = item.Sequence?.Id ?? "";
        var affected = EnumerateNodes(configuration.TestPlan.Cases.SelectMany(x => x.Flow))
            .Where(node => node.Operation is null &&
                (node.ResourceId == resourceId || (!string.IsNullOrWhiteSpace(sequenceId) && node.SequenceId == sequenceId)))
            .Select(node => Diagnostic(node, "DanglingReference", $"资源已删除，引用仍保留：ResourceId={resourceId}，SequenceId={sequenceId}。"))
            .ToList();

        if (!RemoveItem(configuration.Root, item))
            return new(false, affected, new("DeleteFailed", "", item.Name, resourceId, sequenceId, "无法从配置树删除资源。"));
        return new(true, affected, null);
    }

    public IReadOnlyList<ResourceRelationDiagnostic> FindDanglingReferences(EcuConfiguration configuration)
        => EnumerateNodes(configuration.TestPlan.Cases.SelectMany(x => x.Flow))
            .Select(node => Resolve(configuration, node).Diagnostic)
            .Where(x => x is not null)
            .Cast<ResourceRelationDiagnostic>()
            .ToList();

    private static ResourceRelationDiagnostic Diagnostic(TestFlowNode node, string code, string message)
        => new(code, node.Id, node.Name, node.ResourceId, node.SequenceId, message);

    private static ConfigurationSequence? FindSequenceByResourceId(ConfigurationTreeItem item, string id)
    {
        if (item.Id == id && item.Sequence is not null) return item.Sequence;
        if (item.Sequence?.Id == id) return item.Sequence;
        foreach (var child in item.Children)
        {
            var found = FindSequenceByResourceId(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ConfigurationTreeItem? FindItem(ConfigurationTreeItem item, string id)
    {
        if (item.Id == id) return item;
        foreach (var child in item.Children)
        {
            var found = FindItem(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool RemoveItem(ConfigurationTreeItem root, ConfigurationTreeItem target)
    {
        if (root.Children.Remove(target)) return true;
        foreach (var child in root.Children)
            if (RemoveItem(child, target)) return true;
        return false;
    }

    private static IEnumerable<TestFlowNode> EnumerateNodes(IEnumerable<TestFlowNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Children)) yield return child;
        }
    }

    private static ConfigurationStep CloneStep(ConfigurationStep step)
        => ConfigurationJson.Deserialize(ConfigurationJson.Serialize(new EcuConfiguration
        {
            Root = new ConfigurationTreeItem { Sequence = new ConfigurationSequence { Steps = [step] } }
        })).Root.Sequence!.Steps[0];

    private static ParseAssertion? CloneAssertion(ParseAssertion? source, string resourceStepId)
    {
        if (source is null) return null;
        if (!string.IsNullOrWhiteSpace(source.ParseResourceId) && source.ParseResourceId != resourceStepId) return null;
        return new ParseAssertion { Expectation = source.Expectation, TimeoutMs = source.TimeoutMs };
    }
}
