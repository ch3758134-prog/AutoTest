using System.Collections.Immutable;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Preparation;

/// <summary>
/// Preflight for callable declarations in the one official TestDocument.
/// Calls are statically linked by name; recursion is prohibited, including
/// unexecuted code paths, so Mock cannot accidentally bypass the call budget.
/// </summary>
public static class SubroutineValidator
{
    public static ImmutableArray<string> Validate(TestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = ImmutableArray.CreateBuilder<string>();
        var definitions = new Dictionary<string, StepNode>(StringComparer.Ordinal);
        foreach (var node in document.Walk().Where(n => n.Type == "Subroutine"))
        {
            if (!node.Properties.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                continue; // The catalog already emits a missing-property error.
            if (!definitions.TryAdd(name, node))
                errors.Add($"{node.Id}: duplicate subroutine declaration '{name}'");
        }
        foreach (var node in document.Walk().Where(n => n.Type == "CallSubroutine"))
        {
            if (node.Properties.TryGetValue("target", out var name) &&
                !string.IsNullOrWhiteSpace(name) && !definitions.ContainsKey(name))
                errors.Add($"{node.Id}: missing subroutine target '{name}'");
        }

        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (name, sub) in definitions)
        {
            graph[name] = sub.Slots.TryGetValue("body", out var children)
                ? TestDocument.WalkNodes(children)
                    .Where(n => n.Type == "CallSubroutine")
                    .Select(n => n.Properties.GetValueOrDefault("target", ""))
                    .Where(definitions.ContainsKey)
                    .Distinct(StringComparer.Ordinal).ToArray()
                : [];
        }
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name, int depth)
        {
            if (depth > 32) { errors.Add($"{name}: subroutine call depth exceeds 32"); return; }
            if (active.Contains(name))
            {
                errors.Add($"{name}: recursive subroutine call cycle");
                return;
            }
            if (!visited.Add(name)) return;
            active.Add(name);
            foreach (var target in graph[name]) Visit(target, depth + 1);
            active.Remove(name);
        }
        foreach (var name in definitions.Keys) Visit(name, 0);
        return errors.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }
}
