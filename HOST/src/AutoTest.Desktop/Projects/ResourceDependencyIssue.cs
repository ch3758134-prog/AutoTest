namespace AutoTest.Desktop.Projects;

/// <summary>
/// An authoring-only explanation of a problem already found by the canonical
/// TEST resource closure resolver. The product admission decision still uses
/// AutomaticTestResourceSnapshot.Issues, not this UI projection.
/// </summary>
public sealed record ResourceDependencyIssue(
    string RootKey, string Category, string RootTitle,
    string Affected, string Reason, Guid? ResourceId = null,
    Guid? InvocationId = null);

/// <summary>One upstream change may affect many steps or case invocations.</summary>
public sealed record ResourceDependencyGroup(
    string RootKey, string Category, string Title,
    IReadOnlyList<ResourceDependencyIssue> AffectedIssues,
    Guid? ResourceId, Guid? InvocationId)
{
    public int AffectedCount => AffectedIssues.Count;
    public string Explanation => string.Join(Environment.NewLine,
        AffectedIssues.Select(x => "• " + x.Affected + "：" + x.Reason));

    public static IReadOnlyList<ResourceDependencyGroup> Group(
        IEnumerable<ResourceDependencyIssue> issues) =>
        issues.GroupBy(x => x.RootKey, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return new ResourceDependencyGroup(g.Key, first.Category,
                    first.RootTitle, g.ToArray(), first.ResourceId,
                    first.InvocationId);
            })
            .OrderBy(x => x.Category, StringComparer.CurrentCulture)
            .ThenBy(x => x.Title, StringComparer.CurrentCulture).ToArray();
}
