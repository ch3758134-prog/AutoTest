using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestProjectPlanWorkflowTests
{
    [Fact]
    public void ProjectSetupAndTeardownUsePinnedTestDslAssets_NotExtraTestCases()
    {
        var setup = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(),
            2, new string('A', 64), "整车 CAN 准备", true);
        var apply = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(),
            3, new string('B', 64), "静态驻车", true);
        var teardown = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(),
            4, new string('C', 64), "停止周期 CAN", true);
        var project = TestProjectDocument.Create("HydEPB 完整测试") with
        {
            ProjectSetup = setup,
            Packages = [apply],
            ProjectTeardown = teardown
        };
        var loaded = TestProjectCodec.Decode(TestProjectCodec.Encode(project));
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(project.Id, loaded.Id);
        Assert.Equal(new[] { setup.ItemId, apply.ItemId, teardown.ItemId },
            loaded.OrderedExecutionEntries().Select(x => x.ItemId).ToArray());
        Assert.Equal(apply.ItemId, Assert.Single(loaded.AutomaticRunEntries()).ItemId);
        Assert.Equal(new string('A', 64), loaded.ProjectSetup!.PackageSha256);
        Assert.Equal(new string('C', 64), loaded.ProjectTeardown!.PackageSha256);
    }

    [Fact]
    public void LegacyV1RequiresExplicitAdoptionBeforeAddingProjectStages()
    {
        var original = new TestProjectDocument(1, Guid.NewGuid(), "旧正式 TEST", []);
        var reread = TestProjectCodec.Decode(TestProjectCodec.Encode(original));
        Assert.Equal(1, reread.SchemaVersion);
        var stage = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(),
            1, new string('A', 64), "准备", true);
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Encode(reread with { ProjectSetup = stage }));
    }

    [Fact]
    public void ProjectStageAndCaseInvocationIdsMustBeUnique()
    {
        var identity = Guid.NewGuid();
        var stage = new TestProjectEntry(identity, Guid.NewGuid(),
            1, new string('A', 64), "准备", true);
        var caseEntry = new TestProjectEntry(identity, Guid.NewGuid(),
            1, new string('B', 64), "驻车", true);
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Encode(TestProjectDocument.Create("异常项目") with
            {
                ProjectSetup = stage, Packages = [caseEntry]
            }));
    }

    [Fact]
    public async Task SavedProjectStagesPreserveIdentityAndResourcePins()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-project-stages-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var root = await workspace.CreateAsync("HydEpbStage", parent);
            var environment = await workspace.CreateTestProgramAsync("整车准备");
            var parking = await workspace.CreateTestProgramAsync("静态驻车");
            var finish = await workspace.CreateTestProgramAsync("整车收尾");
            var project = await workspace.CreateTestProjectAsync("正式 TEST");
            var opened = await workspace.OpenTestProjectEditorAsync(project.Id);
            TestProjectEntry Entry(AutoTest.Projects.Resources.AssetEntry asset,
                string display) => new(Guid.NewGuid(), asset.Id.Value,
                    asset.Revision, asset.Sha256, display, true);
            var document = opened.Document with
            {
                ProjectSetup = Entry(environment, "整车准备"),
                Packages = [Entry(parking, "静态驻车")],
                ProjectTeardown = Entry(finish, "整车收尾")
            };
            await workspace.SaveTestProjectEditorAsync(opened, document);
            var fresh = new ProjectWorkspaceService();
            await fresh.OpenAsync(root.ManifestPath);
            var reread = await fresh.OpenTestProjectEditorAsync(project.Id);
            Assert.Equal(document.ProjectSetup!.ItemId,
                reread.Document.ProjectSetup!.ItemId);
            Assert.Equal(document.ProjectTeardown!.PackageSha256,
                reread.Document.ProjectTeardown!.PackageSha256);
            Assert.Equal(3, reread.Document.OrderedExecutionEntries().Count);
        }
        finally { Directory.Delete(parent, recursive: true); }
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
