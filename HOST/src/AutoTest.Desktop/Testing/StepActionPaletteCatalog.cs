namespace AutoTest.Desktop.Testing;

/// <summary>
/// The same real authored action options own both category tabs and visible
/// search results. No secondary taxonomy, hidden selectable action or
/// independent "supported" checkbox is maintained by the palette UI.
/// </summary>
public static class StepActionPaletteCatalog
{
    private static readonly string[] FrequentActionKeys =
        ["can", "stop-can", "key", "ign", "read", "assert-signal",
         "wait", "if", "loop", "wave-start", "wave-analyze", "wave-compare"];

    public static string[] Categories(IReadOnlyList<TestActionOption> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        // Case nodes can only be inserted into a SWITCH; avoid showing
        // unrelated empty categories while composing that branch.
        if (actions.Count == 1 && actions[0].StepType == "Case")
            return [actions[0].Category];
        return ["常用步骤", .. actions.Select(a => a.Category)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)];
    }

    public static TestActionOption[] Filter(IReadOnlyList<TestActionOption> actions,
        string category, string? query)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var search = (query ?? "").Trim();
        return actions
            .Where(a => search.Length > 0
                ? (a.Title + " " + a.Category + " " + a.Description + " " + a.Key)
                    .Contains(search, StringComparison.CurrentCultureIgnoreCase)
                : category == "常用步骤"
                    ? FrequentActionKeys.Contains(a.Key)
                    : a.Category == category)
            .OrderBy(a => search.Length == 0 && category == "常用步骤"
                ? Array.IndexOf(FrequentActionKeys, a.Key) : 0)
            .ThenBy(a => a.Title, StringComparer.CurrentCulture)
            .ToArray();
    }
}
