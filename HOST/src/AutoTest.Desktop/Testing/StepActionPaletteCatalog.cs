namespace AutoTest.Desktop.Testing;

/// <summary>
/// Pure presentation projection over the canonical action definitions.
/// Category and frequent-action membership are not independently authored here.
/// </summary>
public static class StepActionPaletteCatalog
{
    public static string[] Categories(IReadOnlyList<TestActionOption> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count > 0 && actions.All(x => x.ContextualOnly))
            return [actions[0].Category];
        return ["常用步骤", .. actions.Select(x => x.Category)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)];
    }

    public static TestActionOption[] Filter(IReadOnlyList<TestActionOption> actions,
        string category, string? query)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var search = (query ?? "").Trim();
        return actions
            .Where(action => search.Length > 0
                ? (action.Title + " " + action.Category + " " +
                   action.Description + " " + action.Key)
                    .Contains(search, StringComparison.CurrentCultureIgnoreCase)
                : category == "常用步骤"
                    ? action.IsFrequent
                    : action.Category == category)
            .OrderByDescending(action => search.Length == 0 &&
                category == "常用步骤" && action.IsFrequent)
            .ThenBy(action => action.Title, StringComparer.CurrentCulture)
            .ToArray();
    }
}