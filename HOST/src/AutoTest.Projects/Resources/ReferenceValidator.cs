using AutoTest.Core;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Resources;

public static class ReferenceValidator
{
    public static IReadOnlyList<AssetDependency> ScanDependencies(ProjectDefinition project, AssetId target) =>
        project.Dependencies.Where(d => d.To == target).ToArray();
    // Stale revisions are valid persisted facts, not dangling identities. They must
    // remain in the project and be reported by ScanStale/blocked at Prepare.
    public static IReadOnlyList<AssetDependency> ScanStale(ProjectDefinition project)
    {
        var assets = project.Assets.ToDictionary(a => a.Id);
        return project.Dependencies.Where(d => assets.TryGetValue(d.To, out var a) &&
            a.Revision != d.RequiredRevision).ToArray();
    }
    public static void RejectDangling(ProjectDefinition project)
    {
        var assets = project.Assets.ToDictionary(a => a.Id);
        foreach (var d in project.Dependencies)
        {
            if (d.From.Value == Guid.Empty || !assets.ContainsKey(d.From) || !assets.TryGetValue(d.To, out var target))
                throw new ProjectDataException("REFERENCE_DANGLING", "Resource dependency points to a missing asset.");
            if (d.RequiredRevision < 1)
                throw new ProjectDataException("REFERENCE_REVISION", "Invalid pinned revision.");
        }
    }
}
