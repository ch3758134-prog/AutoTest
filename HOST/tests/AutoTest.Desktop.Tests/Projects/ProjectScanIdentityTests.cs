using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>Real persisted project identities, no pretend scanner or ECU.</summary>
public sealed class ProjectScanIdentityTests
{
    [Fact]
    public async Task SameRevisionFromDifferentProjectCannotReusePublishedScanMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "scan-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = await new ProjectWorkspaceService().CreateAsync("First", root);
            var b = await new ProjectWorkspaceService().CreateAsync("Second", root);
            Assert.Equal(a.Definition.Revision, b.Definition.Revision);
            Assert.False(ProjectScanMatcher.SameProjectSnapshot(a, b));
            Assert.True(ProjectScanMatcher.SameProjectSnapshot(a, a));
            Assert.False(ProjectScanMatcher.SameProjectSnapshot(a, null));
            Assert.False(ProjectScanMatcher.SameProjectSnapshot(a,
                a with { ManifestSha256 = new string('0', 64) }));
            Assert.False(ProjectScanMatcher.SameProjectSnapshot(a,
                a with { ManifestPath = b.ManifestPath }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
