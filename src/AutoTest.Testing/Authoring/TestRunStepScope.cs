namespace AutoTest.Testing.Authoring;

/// <summary>
/// Runtime-only reachability of TEST step references. A disabled parent disables
/// its entire subtree. Stored but unused resources remain editable; they are not
/// implicitly authorized to send physical CAN in a real run.
/// </summary>
public static class TestRunStepScope
{
    public static IEnumerable<StepNode> EnabledSteps(TestDocument document)
    {
        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var node in EnabledNodes(document.Get(phase)))
                yield return node;
    }

    private static IEnumerable<StepNode> EnabledNodes(IEnumerable<StepNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!StepExecutionPolicy.IsEnabled(node)) continue;
            yield return node;
            foreach (var slot in node.Slots.Values)
                foreach (var child in EnabledNodes(slot))
                    yield return child;
        }
    }

    public static IReadOnlyList<TestTargetMapping> RequiredMappings(TestDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in EnabledSteps(document))
        {
            if (node.Type is "Write" or "Read" or "WaitUntil" or "AssertSignal" or "WaveStart")
            {
                var name = node.Properties.GetValueOrDefault("target", "");
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            if (node.Type == "WaveAnalyze")
            {
                var profile = node.Properties.GetValueOrDefault("profile", "");
                if (!string.IsNullOrWhiteSpace(profile)) names.Add(profile);
            }
        }
        return document.Mappings.Where(mapping => names.Contains(mapping.Name)).ToArray();
    }

    public static bool RequiresPhysicalWrite(TestDocument document) =>
        EnabledSteps(document).Any(node => node.Type == "Write");
}
