using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

public enum TestPhase { Prepare, Test, Cleanup }

/// <summary>Immutable editor node; ID survives edits and persistence. Slots own child sequences.</summary>
public sealed record StepNode(StepId Id, string Type, string Name,
    ImmutableDictionary<string, string> Properties,
    ImmutableDictionary<string, ImmutableArray<StepNode>> Slots)
{
    public static StepNode Create(string type, string? name = null) =>
        new(StepId.New(), type, name ?? type,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, ImmutableArray<StepNode>>.Empty);
    public StepNode WithProperty(string key, string value) =>
        this with { Properties = Properties.SetItem(key, value) };
    public StepNode WithSlot(string slot, params StepNode[] children) =>
        this with { Slots = Slots.SetItem(slot, children.ToImmutableArray()) };
    public StepNode CloneNewIds() => this with
    {
        Id = StepId.New(),
        Slots = Slots.ToImmutableDictionary(x => x.Key,
            x => x.Value.Select(n => n.CloneNewIds()).ToImmutableArray())
    };
}

public sealed record TestDocument(TestDocumentId Id, string Name, int FormatVersion,
    ImmutableDictionary<TestPhase, ImmutableArray<StepNode>> Phases)
{
    public ImmutableArray<TestTargetMapping> Mappings { get; init; } =
        ImmutableArray<TestTargetMapping>.Empty;
    public const int CurrentFormat = 1;
    public static TestDocument Create(string name) => new(TestDocumentId.New(), name, CurrentFormat,
        Enum.GetValues<TestPhase>().ToImmutableDictionary(p => p,
            _ => ImmutableArray<StepNode>.Empty));
    public ImmutableArray<StepNode> Get(TestPhase phase) =>
        Phases.TryGetValue(phase, out var nodes) ? nodes : ImmutableArray<StepNode>.Empty;
    public IEnumerable<StepNode> Walk() => Phases.Values.SelectMany(nodes => WalkNodes(nodes));
    public static IEnumerable<StepNode> WalkNodes(IEnumerable<StepNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in node.Slots.Values.SelectMany(nodes => WalkNodes(nodes))) yield return child;
        }
    }
}
