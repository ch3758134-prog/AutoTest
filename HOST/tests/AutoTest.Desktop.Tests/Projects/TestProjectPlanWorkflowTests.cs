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
    public async Task EditedTestWithOlderProjectPin_CanReopenAndExplicitlyRepair()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-project-pin-repair-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var root = await workspace.CreateAsync("Suite", parent);
            var program = await workspace.CreateTestProgramAsync("驻车动作");
            var project = await workspace.CreateTestProjectAsync("绿通测试项目");
            var opened = await workspace.OpenTestProjectEditorAsync(project.Id);
            var pinned = new TestProjectEntry(Guid.NewGuid(), program.Id.Value,
                program.Revision, program.Sha256, "驻车动作", true);
            await workspace.SaveTestProjectEditorAsync(opened,
                opened.Document with { Packages = [pinned] });

            // This exact action is common in TEST editing. It advances the
            // asset revision, but cannot silently rewrite project references.
            var editor = await workspace.OpenTestEditorAsync(program.Id);
            var savedTest = await workspace.SaveTestEditorAsync(editor,
                editor.Document with { Name = "驻车动作 - 已编辑" });
            Assert.True(savedTest.AssetRevision > pinned.PackageRevision);

            // The workspace must remain accessible for an engineer to repair
            // the now-stale project pin. No TEST execution is involved.
            var reopenedWorkspace = new ProjectWorkspaceService();
            await reopenedWorkspace.OpenAsync(root.ManifestPath);
            var staleProject = await reopenedWorkspace.OpenTestProjectEditorAsync(project.Id);
            var oldReference = Assert.Single(staleProject.Document.Packages);
            Assert.Equal(pinned.PackageRevision, oldReference.PackageRevision);
            Assert.Equal(pinned.PackageSha256, oldReference.PackageSha256);

            // Editing unrelated plan metadata must NOT make an outdated pin
            // eligible for saving or running.
            var invalid = staleProject.Document with { Name = "尚未修复的项目" };
            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => reopenedWorkspace.SaveTestProjectEditorAsync(staleProject, invalid));
            Assert.Equal("PROJECT_PACKAGE_STALE", error.Code);

            // Explicit adopt-and-save is the only way to advance the pin.
            var updated = staleProject.Document with
            {
                Packages =
                [
                    oldReference with
                    {
                        PackageRevision = savedTest.AssetRevision,
                        PackageSha256 = savedTest.FileHash
                    }
                ]
            };
            var repaired = await reopenedWorkspace.SaveTestProjectEditorAsync(staleProject, updated);
            Assert.Equal(savedTest.AssetRevision,
                Assert.Single(repaired.Document.Packages).PackageRevision);
            var reopenedAgain = new ProjectWorkspaceService();
            await reopenedAgain.OpenAsync(root.ManifestPath);
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
