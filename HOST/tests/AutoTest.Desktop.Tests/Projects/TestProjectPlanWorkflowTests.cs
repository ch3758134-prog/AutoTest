using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestProjectPlanWorkflowTests
{
    [Fact]
    public void TestProjectIsOnlyOneOrderedExecutionList()
    {
        var first = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 1,
            new string('A', 64), "驻车", true);
        var last = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 1,
            new string('B', 64), "释放", true);
        var document = TestProjectDocument.Create("HydEPB TEST") with
        {
            Packages = [first, last, first with { ItemId = Guid.NewGuid() }]
        };
        var reopened = TestProjectCodec.Decode(TestProjectCodec.Encode(document));
        Assert.Equal(3, reopened.SchemaVersion);
        Assert.Equal(3, reopened.OrderedExecutionEntries().Count);
        Assert.Equal(3, reopened.AutomaticRunEntries().Count);
        Assert.Equal(first.PackageAssetId, reopened.Packages[2].PackageAssetId);
    }

    [Fact]
    public void OldTestProjectSchemasCannotBeOpenedAsProductionProjects()
    {
        var old = new TestProjectDocument(2, Guid.NewGuid(), "Old", []);
        Assert.Throws<InvalidDataException>(() => TestProjectCodec.Encode(old));
    }

    [Fact]
    public void DuplicateInvocationIdentityIsRejected()
    {
        var id = Guid.NewGuid();
        var first = new TestProjectEntry(id, Guid.NewGuid(), 1,
            new string('A', 64), "驻车", true);
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Encode(TestProjectDocument.Create("TEST") with
            { Packages = [first, first] }));
    }

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
                        apply.Revision, apply.Sha256, "驻车复核", true)
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
            Assert.True(reopened.Document.Packages[2].Enabled);
            Assert.Equal(document.Id, reopened.Document.Id);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task EditedTestAutomaticallyUpdatesOrderedProjectPinAndReopensStrictly()
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

            var editor = await workspace.OpenTestEditorAsync(program.Id);
            var savedTest = await workspace.SaveTestEditorAsync(editor,
                editor.Document with { Name = "驻车动作 - 已编辑" });
            Assert.True(savedTest.AssetRevision > pinned.PackageRevision);

            var reopenedWorkspace = new ProjectWorkspaceService();
            await reopenedWorkspace.OpenAsync(root.ManifestPath);
            var updatedProject = await reopenedWorkspace.OpenTestProjectEditorAsync(project.Id);
            var reference = Assert.Single(updatedProject.Document.Packages);
            Assert.Equal(pinned.ItemId, reference.ItemId);
            Assert.Equal(pinned.PackageAssetId, reference.PackageAssetId);
            Assert.Equal(pinned.DisplayName, reference.DisplayName);
            Assert.Equal(pinned.Enabled, reference.Enabled);
            Assert.Equal(savedTest.AssetRevision, reference.PackageRevision);
            Assert.Equal(savedTest.FileHash, reference.PackageSha256, ignoreCase: true);
            Assert.Equal(3, updatedProject.AssetRevision);
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
