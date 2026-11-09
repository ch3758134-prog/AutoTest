using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

public static class StepTreeValidator
{
    public static ImmutableArray<string> ValidateTree(TestDocument document,
        StepDescriptorCatalog catalog, ISet<string>? availableAssets = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = ImmutableArray.CreateBuilder<string>();
        if (document.FormatVersion != TestDocument.CurrentFormat) errors.Add("Unsupported format version");
        if (document.Id.Value == Guid.Empty) errors.Add("Invalid document identity");
        var ids = new HashSet<StepId>();
        var active = new HashSet<StepNode>(ReferenceEqualityComparer.Instance);
        const int maximumNodes = 10000;
        const int maximumDepth = 32;
        var visited = 0;
        void Visit(StepNode n, string location, string? parent = null, int depth = 0)
        {
            if (++visited > maximumNodes) { if (visited == maximumNodes + 1) errors.Add("Program exceeds 10000 nodes"); return; }
            if (depth > maximumDepth) { errors.Add($"{location}: depth exceeds 32"); return; }
            if (!active.Add(n)) { errors.Add($"{location}: reference cycle"); return; }
            try
            {
                if (n.Id.Value == Guid.Empty || !ids.Add(n.Id)) errors.Add($"{location}: empty/duplicate StepId");
                var d = catalog.Lookup(n.Type);
                if (d is null) errors.Add($"{location}: unknown node {n.Type}");
                else
                {
                    foreach (var slot in d.RequiredSlots)
                        if (!n.Slots.ContainsKey(slot)) errors.Add($"{location}: missing slot {slot}");
                    foreach (var slot in n.Slots.Keys)
                        if (!d.RequiredSlots.Contains(slot)) errors.Add($"{location}: illegal slot {slot}");
                    foreach (var key in d.RequiredProperties)
                        if (!n.Properties.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                            errors.Add($"{location}: missing property {key}");
                }
                if ((n.Type is "Case" or "Default") && parent != "Switch")
                    errors.Add($"{location}: {n.Type} outside Switch");
                if (n.Type == "Loop" && (!n.Properties.TryGetValue("count", out var count) ||
                    !int.TryParse(count, out var loops) || loops < 0 || loops > 1000))
                    errors.Add($"{location}: Loop count outside 0..1000");
                if (n.Type == "When" && (!n.Properties.TryGetValue("timeoutMs", out var timeout) ||
                    !int.TryParse(timeout, out var ms) || ms < 0 || ms > 60000))
                    errors.Add($"{location}: When timeoutMs outside 0..60000");
                if (n.Type == "MockObserve" && n.Properties.TryGetValue("quality", out var quality) &&
                    !Enum.TryParse<AutoTest.Testing.Runtime.MockQuality>(quality, out _))
                    errors.Add($"{location}: invalid observation quality");
                if (n.Type == "Wait" && (!n.Properties.TryGetValue("milliseconds", out var delayText) ||
                    !int.TryParse(delayText, out var delay) || delay < 0 || delay > 60000))
                    errors.Add($"{location}: Wait outside 0..60000ms");
                if (n.Properties.TryGetValue("asset", out var asset) &&
                    (availableAssets is null || !availableAssets.Contains(asset)))
                    errors.Add($"{location}: unresolved asset {asset}");
                if (n.Type == "Switch" && n.Slots.TryGetValue("cases", out var arms))
                {
                    if (arms.IsEmpty) errors.Add($"{location}: Switch requires at least one Case or Default");
                    if (arms.Any(x => x.Type is not ("Case" or "Default")))
                        errors.Add($"{location}: Switch cases may contain only Case/Default");
                    if (arms.Count(x => x.Type == "Default") > 1)
                        errors.Add($"{location}: Switch may contain only one Default");
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var arm in arms.Where(x => x.Type == "Case"))
                        if (arm.Properties.TryGetValue("equals", out var v) && !values.Add(v))
                            errors.Add($"{location}: duplicate Switch Case value '{v}'");
                }
                foreach (var slot in n.Slots)
                    foreach (var child in slot.Value)
                        Visit(child, $"{location}/{slot.Key}/{child.Id}", n.Type, depth + 1);
            }
            finally { active.Remove(n); }
        }
        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var n in document.Get(phase)) Visit(n, $"{phase}/{n.Id}");
        return errors.ToImmutable();
    }
}
