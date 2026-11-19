using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestProjectPlanWorkflowTests
{
    [Fact]
    public async Task ProjectRefersToOrderedVersionPinnedPackages_AndSurvivesReopen()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-package-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var root = await workspace.CreateAsync("Suite", parent);
            var apply = await workspace.CreateTestProgramAsync("驻车");
            var release = await workspace.CreateTestProgramAsync("释放");
            var project = await workspace.CreateTestProjectAsync("出厂测试");
            var opened = await workspace.OpenTestProjectEditorAsync(project.Id);

            var document = opened.Document with
            {
                Packages =
                [
                    new TestProjectEntry(Guid.NewGuid(), apply.Id.Value,
                        apply.Revision, apply.Sha256, "驻车", true),
                    new TestProjectEntry(Guid.NewGuid(), release.Id.Value,
                        release.Revision, release.Sha256, "释放", true),
                    new TestProjectEntry(Guid.NewGuid(), apply.Id.Value,
                        apply.Revision, apply.Sha256, "驻车复核", false)
                ]
            };
            var saved = await workspace.SaveTestProjectEditorAsync(opened, document);
            Assert.Equal(2, saved.AssetRevision);
            var second = new ProjectWorkspaceService();
            await second.OpenAsync(root.ManifestPath);
            var reopened = await second.OpenTestProjectEditorAsync(project.Id);
            Assert.Equal("出厂测试", reopened.Document.Name);
            Assert.Equal(3, reopened.Document.Packages.Count);
            Assert.Equal(apply.Id.Value, reopened.Document.Packages[0].PackageAssetId);
            Assert.Equal(release.Id.Value, reopened.Document.Packages[1].PackageAssetId);
            Assert.False(reopened.Document.Packages[2].Enabled);
            Assert.Equal(document.Id, reopened.Document.Id);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task StalePackageRevisionCannotBeSilentlyAddedToProject()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-package-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("Suite", parent);
            var p = await workspace.CreateTestProgramAsync("Drive");
            var project = await workspace.CreateTestProjectAsync("Plan");
            var opened = await workspace.OpenTestProjectEditorAsync(project.Id);
            var item = new TestProjectEntry(Guid.NewGuid(), p.Id.Value,
                p.Revision + 1, p.Sha256, p.Name, true);
            var draft = opened.Document with { Packages = [item] };
            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => workspace.SaveTestProjectEditorAsync(opened, draft));
            Assert.Equal("PROJECT_PACKAGE_STALE", error.Code);
            Assert.Equal(1, (await workspace.OpenTestProjectEditorAsync(project.Id)).AssetRevision);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
