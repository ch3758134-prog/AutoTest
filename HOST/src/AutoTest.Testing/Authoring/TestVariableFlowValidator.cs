using System.Collections.Immutable;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// Conservative, path-aware IF variable analysis on the single canonical TEST
/// step tree. No runtime scripting and no synthetic measurements. A variable
/// introduced in only one branch or in a possibly-zero-iteration loop is NOT
/// guaranteed to exist after the control structure.
/// </summary>
public static class TestVariableFlowValidator
{
    public sealed record AvailableVariable(string Name, string Source);

    public static ImmutableArray<string> Validate(TestDocument document)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        var prepare = NewEnvironment();
        Walk(document.Get(TestPhase.Prepare), prepare, null, issues);
        var test = Copy(prepare);
        Walk(document.Get(TestPhase.Test), test, null, issues);
        // Cleanup also runs when preparation fails, so TEST-local variables are
        // not guaranteed on the cleanup path.
        var cleanup = Copy(prepare);
        Walk(document.Get(TestPhase.Cleanup), cleanup, null, issues);
        return issues.ToImmutable();
    }

    /// <summary>Only definitely available earlier variables are offered by
    /// the IF picker; an invalid currently saved value remains text-editable.</summary>
    public static IReadOnlyList<AvailableVariable> Before(TestDocument document, StepId step)
    {
        IReadOnlyList<AvailableVariable>? found = null;
        void Inspect(StepNode current, Dictionary<string, string> values)
        {
            if (current.Id != step) return;
            found = values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AvailableVariable(x.Key, x.Value)).ToArray();
        }
        var prepare = NewEnvironment();
        Walk(document.Get(TestPhase.Prepare), prepare, Inspect, null);
        var test = Copy(prepare);
        Walk(document.Get(TestPhase.Test), test, Inspect, null);
        var cleanup = Copy(prepare);
        Walk(document.Get(TestPhase.Cleanup), cleanup, Inspect, null);
        return found ?? [];
    }

    private static Dictionary<string, string> NewEnvironment() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Copy(Dictionary<string, string> source) =>
        new(source, StringComparer.OrdinalIgnoreCase);

    private static void Record(Dictionary<string, string> values, StepNode step, string property)
    {
        var name = step.Properties.GetValueOrDefault(property, "").Trim();
        if (name.Length > 0) values[name] = step.Name;
    }

    private static void Walk(IEnumerable<StepNode> nodes, Dictionary<string, string> values,
        Action<StepNode, Dictionary<string, string>>? inspect,
        ImmutableArray<string>.Builder? issues)
    {
        foreach (var node in nodes)
        {
            if (!StepExecutionPolicy.IsEnabled(node)) continue;
            inspect?.Invoke(node, values);
            if (node.Type == "If")
            {
                var variable = node.Properties.GetValueOrDefault("variable", "").Trim();
                if (variable.Length > 0 && !values.ContainsKey(variable))
                    issues?.Add(node.Name + " (" + node.Id + "): IF 变量 '" + variable +
                        "' 没有可保证先执行的赋值/读取步骤；请先建立变量来源。");
                var equals = node.Properties.GetValueOrDefault("equals", "").Trim();
                if (equals.StartsWith("$", StringComparison.Ordinal) &&
                    !values.ContainsKey(equals[1..]))
                    issues?.Add(node.Name + " (" + node.Id + "): IF 比较值变量 '" +
                        equals + "' 没有可保证的上游来源。");

                var thenValues = Copy(values);
                var elseValues = Copy(values);
                if (node.Slots.TryGetValue("then", out var thenSteps))
                    Walk(thenSteps, thenValues, inspect, issues);
                if (node.Slots.TryGetValue("else", out var elseSteps))
                    Walk(elseSteps, elseValues, inspect, issues);
                // Only variables assigned on both paths can flow out of IF.
                foreach (var name in thenValues.Keys.Intersect(elseValues.Keys,
                             StringComparer.OrdinalIgnoreCase).ToArray())
                    if (!values.ContainsKey(name))
                        values[name] = "IF 两个分支";
            }
            else if (node.Type == "Switch")
            {
                var variable = node.Properties.GetValueOrDefault("variable", "").Trim();
                if (variable.Length > 0 && !values.ContainsKey(variable))
                    issues?.Add(node.Name + " (" + node.Id + "): SWITCH 变量 '" +
                        variable + "' 没有可保证的上游赋值/读取来源。");
                // SWITCH does not fall through. Each arm sees only variables
                // guaranteed before the SWITCH. A value created in just one
                // arm is not guaranteed downstream. Without Default the
                // unmatched path also remains possible.
                if (node.Slots.TryGetValue("cases", out var cases))
                {
                    var arms = cases.Where(StepExecutionPolicy.IsEnabled).ToArray();
                    var exits = new List<Dictionary<string, string>>();
                    var hasDefault = false;
                    foreach (var arm in arms)
                    {
                        var label = arm.Properties.GetValueOrDefault("equals", "").Trim();
                        if (label.Equals("default", StringComparison.OrdinalIgnoreCase))
                            hasDefault = true;
                        var branch = Copy(values);
                        if (arm.Slots.TryGetValue("body", out var body))
                            Walk(body, branch, inspect, issues);
                        exits.Add(branch);
                    }
                    if (hasDefault && exits.Count > 0)
                        foreach (var name in exits[0].Keys.ToArray())
                            if (!values.ContainsKey(name) &&
                                exits.All(exit => exit.ContainsKey(name)))
                                values[name] = "SWITCH 所有分支";
                }
            }
            else if (node.Type == "Loop")
            {
                // First iteration cannot rely on its prior iterations, and
                // count=0 means body-created variables do not escape the loop.
                if (node.Slots.TryGetValue("body", out var body))
                    Walk(body, Copy(values), inspect, issues);
            }
            else if (node.Type == "Sequence")
            {
                if (node.Slots.TryGetValue("body", out var body))
                    Walk(body, values, inspect, issues);
            }
            else
            {
                // Other nested flow constructs are independently marked
                // unsupported by the formal PlanCompiler and cannot authorize
                // guaranteed downstream variables.
                foreach (var slot in node.Slots.Values)
                    Walk(slot, Copy(values), inspect, issues);
            }

            switch (node.Type)
            {
                case "SetVariable":
                case "Calculate":
                case "Read":
                case "WaitUntil":
                case "WaveStop":
                case "WaveAnalyze":
                case "WaveCompare":
                    Record(values, node, "variable");
                    break;
            }
            if (node.Type == "Read")
            {
                var key = node.Properties.GetValueOrDefault("variable", "");
                if (key.Length > 0) values[key + ".status"] = node.Name;
            }
            if (node.Type == "WaitUntil")
            {
                var key = node.Properties.GetValueOrDefault("variable", "");
                if (key.Length > 0)
                {
                    values[key + ".met"] = node.Name;
                    values[key + ".status"] = node.Name;
                }
            }
        }
    }
}
