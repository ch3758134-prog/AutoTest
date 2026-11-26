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
        Add("Switch", true, ["cases"], ["variable"]);
        Add("Case", true, ["body"], ["equals"]);
        Add("When", false, ["body"], ["variable", "equals", "timeoutMs"]);
        Add("Loop", true, ["body"], ["count"]);
        Add("TryCatch", false, ["try", "catch", "finally"]);
        Add("Subroutine", false, ["body"]);
        Add("CallSubroutine", false, [], ["target"]);
        // G3-approved engineering resource call; runtime remains blocked until
        // its exact transitive dependency closure is prepared and authorized.
        Add("CallPublicFlow", false, [], ["flowAssetId", "flowRevision", "flowSha"]);
        Add("Parallel", false, ["branches"]);
        Add("SetVariable", true, [], ["variable", "value"]);
        Add("Wait", true, [], ["milliseconds"]);
        // Real runtime actions use typed, version-pinned Mapping references.
        // Only RealRunCoordinator executes these physical operations.
        Add("Write", false, [], ["target", "value"]);
        Add("Read", false, [], ["target", "variable", "mode", "timeoutMs", "maxAgeMs"]);
        Add("WaitUntil", false, [], ["target", "variable", "operator", "expected", "timeoutMs"]);
        // Formal CAN signal assertion: waits for matching data, then contributes to TEST verdict.
        Add("AssertSignal", false, [], ["target", "operator", "expected", "timeoutMs"]);
        Add("CanStopPeriodic", false);
        Add("Calculate", false, [], ["variable", "left", "operator", "right"]);
        Add("Expect", false, [], ["left", "operator", "right"]);
        Add("WaveStart", false, [], ["target", "captureKey", "durationMs"]);
        Add("WaveStop", false, [], ["captureKey", "variable"]);
        Add("WaveAnalyze", false, [], ["captureKey", "variable", "tolerance", "coverage"]);
        // Formal wave judgement uses a completed real capture and pinned reference/profile.
        Add("WaveCompare", false, [], ["captureKey", "variable", "tolerance", "coverage"]);
        // Explicitly modeled future capabilities; never claim V1 runtime support.
        // FLASH uses the same TEST DSL. Physical execution remains disabled until wired.
        Add("Flash", false, [], ["programmingProfile", "firmwarePath",
            "securityLibraryPath", "targetLabel"]);
        foreach (var name in new[] { "PowerSet", "KeySet", "CanSend", "CanWait", "UdsRequest",
            "WaveCapture", "Log", "Fail", "Skip" }) Add(name, false);
        return c;
    }
}
