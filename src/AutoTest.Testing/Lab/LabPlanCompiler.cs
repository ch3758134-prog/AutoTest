using System.Collections.Immutable;

namespace AutoTest.Testing.Lab;

public readonly record struct LabControllerRef(string ControllerId, string StateId, string Revision);
public readonly record struct LabControllerResolution(bool Found, string CurrentRevision);
public interface ILabControllerCatalog
{
    LabControllerResolution Resolve(LabControllerRef reference);
}
public interface ILabAssetCatalog
{
    bool Exists(string assetId, string revision);
}

public sealed record LabPreparedPlan(string CanonicalJson, string Digest,
    ImmutableArray<string> Diagnostics, ImmutableArray<LabControllerRef> ControllerBindings)
{
    public bool Ready => Diagnostics.IsEmpty;
    public LabDocument Restore()
    {
        if (!string.Equals(LabDocumentCodec.Digest(CanonicalJson), Digest, StringComparison.Ordinal))
            throw new InvalidOperationException("Plan fingerprint changed");
        return LabDocumentCodec.Open(CanonicalJson);
    }
}

public sealed class LabPlanCompiler
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Sequence"] = ["body"], ["TestCase"] = ["body"], ["If"] = ["then", "else"],
        ["Switch"] = ["cases"], ["Case"] = ["body"], ["Default"] = ["body"],
        ["When"] = ["body"], ["Loop"] = ["body"],
        ["TryCatch"] = ["try", "catch", "finally"],
        ["Subroutine"] = ["body"], ["CallSubroutine"] = [],
        ["SetVariable"] = [], ["MockObserve"] = [], ["Observe"] = [],
        ["Assert"] = [], ["Wait"] = [], ["ControllerState"] = [],
        ["Log"] = [], ["Fail"] = []
    };
    private readonly ILabControllerCatalog? _controllers;
    private readonly ILabAssetCatalog? _assets;
    public LabPlanCompiler(ILabControllerCatalog? controllers = null, ILabAssetCatalog? assets = null)
        => (_controllers, _assets) = (controllers, assets);
    public static IReadOnlyCollection<string> ExecutableNodes => Allowed.Keys;

    public LabPreparedPlan Prepare(LabDocument document)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        var bindings = ImmutableArray.CreateBuilder<LabControllerRef>();
        var snapshot = LabDocumentCodec.Open(LabDocumentCodec.Save(document));
        var ids = new HashSet<Guid>();
        var subroutines = new Dictionary<string, LabNode>(StringComparer.Ordinal);
        var calls = new List<(string name, string location)>();
        var callEdges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var stepCount = 0;
        void Verify(LabNode node, string where, string? owner, int depth)
        {
            if (++stepCount > 10000 || depth > 32) { problems.Add("Program exceeds 10000 nodes / depth 32"); return; }
            if (node.Id == Guid.Empty || !ids.Add(node.Id)) problems.Add($"{where}: duplicate/empty node ID");
            if (node.Parameters is null || node.Slots is null || string.IsNullOrWhiteSpace(node.Type))
            { problems.Add($"{where}: invalid node"); return; }
            if (!Allowed.TryGetValue(node.Type, out var slots))
            { problems.Add($"{where}: {node.Type} not supported in LAB runtime"); return; }
            foreach (var required in slots)
                if (!node.Slots.ContainsKey(required)) problems.Add($"{where}: missing {required} slot");
            foreach (var extra in node.Slots.Keys)
                if (!slots.Contains(extra)) problems.Add($"{where}: illegal slot {extra}");
            string Param(string key)
            {
                if (!node.Parameters.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                { problems.Add($"{where}: missing parameter {key}"); return ""; }
                return v;
            }
            void IntWithin(string key, int min, int max)
            {
                var raw = Param(key);
                if (!int.TryParse(raw, out var number) || number < min || number > max)
                    problems.Add($"{where}: {key} must be {min}..{max}");
            }
            switch (node.Type)
            {
                case "If": case "When":
                    Param("variable"); Param("equals");
                    if (node.Type == "When") IntWithin("timeoutMs", 0, 60000);
                    break;
                case "Switch": Param("variable"); break;
                case "Case":
                    Param("equals");
                    if (owner != "Switch") problems.Add($"{where}: Case must be a Switch child");
                    break;
                case "Default":
                    if (owner != "Switch") problems.Add($"{where}: Default must be a Switch child");
                    break;
                case "Loop": IntWithin("count", 0, 1000); break;
                case "Wait": IntWithin("milliseconds", 0, 60000); break;
                case "SetVariable": Param("variable"); Param("value"); break;
                case "MockObserve":
                    Param("variable"); Param("value");
                    if (!Enum.TryParse<LabQuality>(Param("quality"), out _))
                        problems.Add($"{where}: invalid quality");
                    break;
                case "Observe": case "Assert": Param("variable");
                    if (node.Type == "Assert") Param("equals");
                    break;
                case "Subroutine":
                    var name = Param("name");
                    if (!subroutines.TryAdd(name, node)) problems.Add($"Duplicate subroutine {name}");
                    break;
                case "CallSubroutine":
                    var target = Param("target");
                    calls.Add((target, where));
                    break;
                case "ControllerState":
                    var reference = new LabControllerRef(Param("controllerId"), Param("stateId"), Param("revision"));
                    bindings.Add(reference);
                    var resolution = _controllers?.Resolve(reference);
                    if (resolution is null || !resolution.Value.Found ||
                        !string.Equals(resolution.Value.CurrentRevision, reference.Revision, StringComparison.Ordinal))
                        problems.Add($"{where}: controller/state missing or stale revision");
                    break;
                case "TestCase":
                    if (owner is not null || !where.StartsWith("Test/", StringComparison.Ordinal))
                        problems.Add($"{where}: TestCase must be a top-level TEST node");
                    if (string.IsNullOrWhiteSpace(node.Name))
                        problems.Add($"{where}: TestCase name is required");
                    break;
                case "TryCatch": case "Sequence": case "Log": case "Fail": break;
            }
            if (node.Parameters.TryGetValue("assetId", out var asset))
            {
                node.Parameters.TryGetValue("assetRevision", out var revision);
                if (string.IsNullOrWhiteSpace(revision) || _assets?.Exists(asset, revision) != true)
                    problems.Add($"{where}: missing/stale asset binding");
            }
            if (node.Type == "Switch" && node.Slots.TryGetValue("cases", out var entries))
            {
                if (entries.Count(x => x.Type == "Default") > 1) problems.Add($"{where}: more than one Default");
                if (entries.Any(x => x.Type != "Case" && x.Type != "Default")) problems.Add($"{where}: Switch may contain only Case/Default");
            }
            foreach (var pair in node.Slots)
            {
                if (pair.Value is null) { problems.Add($"{where}: null child list"); continue; }
                foreach (var child in pair.Value)
                    Verify(child, $"{where}/{pair.Key}/{child.Id}", node.Type, depth + 1);
            }
        }
        foreach (var phase in Enum.GetValues<LabPhase>())
            foreach (var node in snapshot.Phases[phase]) Verify(node, $"{phase}/{node.Id}", null, 0);
        foreach (var (name, location) in calls)
            if (!subroutines.ContainsKey(name)) problems.Add($"{location}: missing subroutine {name}");
        // Resolve nested call relationships independent of tree declaration order.
        foreach (var sub in subroutines)
        {
            var edges = new List<string>();
            void FindCalls(IEnumerable<LabNode> steps)
            {
                foreach (var step in steps)
                {
                    if (step.Type == "CallSubroutine") edges.Add(step.Parameters.GetValueOrDefault("target", ""));
                    foreach (var nested in step.Slots.Values) FindCalls(nested);
                }
            }
            FindCalls(sub.Value.Slots.GetValueOrDefault("body", new List<LabNode>()));
            callEdges[sub.Key] = edges;
        }
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        void CheckCalls(string name)
        {
            if (active.Contains(name)) { problems.Add($"Recursive subroutine cycle at {name}"); return; }
            if (!visited.Add(name)) return;
            active.Add(name);
            if (callEdges.TryGetValue(name, out var outgoing))
                foreach (var next in outgoing) if (subroutines.ContainsKey(next)) CheckCalls(next);
            active.Remove(name);
        }
        foreach (var name in subroutines.Keys) CheckCalls(name);
        var json = LabDocumentCodec.Save(snapshot);
        return new LabPreparedPlan(json, LabDocumentCodec.Digest(json), problems.Distinct().ToImmutableArray(),
            bindings.Distinct().ToImmutableArray());
    }
}
