using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

public static class StepTreeValidator
{
    public static ImmutableArray<string> ValidateTree(TestDocument document,
        StepDescriptorCatalog catalog, ISet<string>? availableAssets = null)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (document.FormatVersion != TestDocument.CurrentFormat) errors.Add("Unsupported format version");
        if (document.Id.Value == Guid.Empty) errors.Add("Invalid document identity");
        var ids = new HashSet<StepId>();
        var active = new HashSet<StepNode>(ReferenceEqualityComparer.Instance);
        void Visit(StepNode n, string location, string? parent = null)
        {
            if (!active.Add(n)) { errors.Add($"{location}: reference cycle"); return; }
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
            if (n.Type == "Case" && parent != "Switch") errors.Add($"{location}: Case outside Switch");
            if (n.Type == "Switch" && n.Slots.TryGetValue("cases", out var cases))
            {
                if (cases.Length == 0)
                    errors.Add($"{location}: Switch requires at least one Case");
                var values = cases.Where(x => x.Type == "Case")
                    .Select(x => x.Properties.GetValueOrDefault("equals") ?? "").ToArray();
                if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                    errors.Add($"{location}: duplicate Switch Case values");
            }
            if (n.Type == "Loop" && (!n.Properties.TryGetValue("count", out var count) ||
                !int.TryParse(count, out var loops) || loops < 0 || loops > 1000))
                errors.Add($"{location}: Loop count outside 0..1000");
            if (n.Type == "MockObserve" && n.Properties.TryGetValue("quality", out var quality) &&
                !Enum.TryParse<AutoTest.Testing.Runtime.MockQuality>(quality, out _))
                errors.Add($"{location}: invalid observation quality");
            if (n.Type == "Wait" && (!n.Properties.TryGetValue("milliseconds", out var ms) ||
                !int.TryParse(ms, out var delay) || delay < 0 || delay > 60000))
                errors.Add($"{location}: Wait outside 0..60000ms");
            if (n.Properties.TryGetValue("asset", out var asset) &&
                (availableAssets is null || !availableAssets.Contains(asset)))
                errors.Add($"{location}: unresolved asset {asset}");
            foreach (var slot in n.Slots)
            {
                if (n.Type == "Switch" && slot.Key == "cases" && slot.Value.Any(x => x.Type != "Case"))
                    errors.Add($"{location}: Switch cases must contain Case nodes");
                foreach (var child in slot.Value) Visit(child, $"{location}/{slot.Key}/{child.Id}", n.Type);
            }
            active.Remove(n);
        }
        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var n in document.Get(phase)) Visit(n, $"{phase}/{n.Id}");
        return errors.ToImmutable();
    }
}
