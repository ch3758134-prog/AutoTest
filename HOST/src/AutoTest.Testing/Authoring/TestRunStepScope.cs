using System.Collections.Immutable;
using AutoTest.Core;

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
            if (node.Type is "WaveAnalyze" or "WaveCompare")
            {
                var profile = node.Properties.GetValueOrDefault("profile", "");
                if (!string.IsNullOrWhiteSpace(profile)) names.Add(profile);
            }
        }
        return document.Mappings.Where(mapping => names.Contains(mapping.Name)).ToArray();
    }

    public static bool RequiresPhysicalWrite(TestDocument document) =>
        EnabledSteps(document).Any(node => node.Type == "Write");

    /// <summary>
    /// A one-step diagnostic uses the same immutable TEST model, PlanCompiler,
    /// real hardware ports and RECORD executor as a full case. It does not
    /// replay unrelated preparation/cleanup scripts. Station fail-safe cleanup
    /// still runs in RealRunCoordinator and RealTestWorkbenchService.
    /// </summary>
    public static TestDocument ForSingleStep(TestDocument source, StepId selectedId)
    {
        static bool Find(IEnumerable<StepNode> nodes, StepId target,
            bool parentEnabled, out StepNode? matched, out bool enabled)
        {
            foreach (var node in nodes)
            {
                var currentEnabled = parentEnabled && StepExecutionPolicy.IsEnabled(node);
                if (node.Id == target)
                {
                    matched = node;
                    enabled = currentEnabled;
                    return true;
                }
                foreach (var slot in node.Slots.Values)
                    if (Find(slot, target, currentEnabled, out matched, out enabled))
                        return true;
            }
            matched = null;
            enabled = false;
            return false;
        }

        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            if (!Find(source.Get(phase), selectedId, true, out var matched,
                    out var enabled)) continue;
            if (!enabled || matched is null)
                throw new InvalidOperationException(
                    "选中步骤或其所属流程已关闭，不能单独执行。");
            var isolated = Enum.GetValues<TestPhase>().ToImmutableDictionary(
                p => p, p => p == phase
                    ? ImmutableArray.Create(matched) : ImmutableArray<StepNode>.Empty);
            // Do not let unused mappings from unrelated case steps widen the
            // physical write lease or reject an otherwise independent
            // diagnostic because an unrelated asset revision is stale.
            var oneStepDocument = source with { Phases = isolated };
            return oneStepDocument with
            {
                Mappings = RequiredMappings(oneStepDocument).ToImmutableArray()
            };
        }
        throw new KeyNotFoundException("选中的 TEST 步骤已经不存在，请重新选择。");
    }
}
