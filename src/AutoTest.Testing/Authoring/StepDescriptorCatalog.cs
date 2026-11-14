using System.Collections.Immutable;

namespace AutoTest.Testing.Authoring;

public sealed record StepDescriptor(string Type, ImmutableArray<string> RequiredSlots,
    ImmutableArray<string> RequiredProperties, bool ExecutableV1);

public sealed class StepDescriptorCatalog
{
    private readonly Dictionary<string, StepDescriptor> _types = new(StringComparer.Ordinal);
    public void Register(StepDescriptor descriptor)
    {
        if (!_types.TryAdd(descriptor.Type, descriptor))
            throw new InvalidOperationException($"Duplicate step type: {descriptor.Type}");
    }
    public StepDescriptor? Lookup(string type) => _types.GetValueOrDefault(type);
    public IReadOnlyCollection<StepDescriptor> Enumerate() => _types.Values.ToArray();
    public static StepDescriptorCatalog Builtins()
    {
        var c = new StepDescriptorCatalog();
        void Add(string name, bool executable, string[]? slots = null, string[]? props = null) =>
            c.Register(new(name, (slots ?? []).ToImmutableArray(),
                (props ?? []).ToImmutableArray(), executable));
        Add("Sequence", true, ["body"]);
        Add("If", true, ["then", "else"], ["variable", "equals"]);
        Add("Switch", false, ["cases"], ["variable"]);
        Add("Case", false, ["body"], ["equals"]);
        Add("When", false, ["body"], ["variable", "equals", "timeoutMs"]);
        Add("Loop", true, ["body"], ["count"]);
        Add("TryCatch", false, ["try", "catch", "finally"]);
        Add("Subroutine", false, ["body"]);
        Add("CallSubroutine", false, [], ["target"]);
        Add("Parallel", false, ["branches"]);
        Add("SetVariable", true, [], ["variable", "value"]);
        Add("Wait", true, [], ["milliseconds"]);
        // Explicitly modeled future capabilities; never claim V1 runtime support.
        foreach (var name in new[] { "PowerSet", "KeySet", "CanSend", "CanWait", "UdsRequest",
            "Flash", "WaveCapture", "WaveCompare", "Log", "Fail", "Skip" }) Add(name, false);
        return c;
    }
}
