using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestEditorWorkflowTests
{
    private static string Fresh()
    {
        var folder = Path.Combine(Path.GetTempPath(), "at-test-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public async Task StaleDbcPin_AllowsEngineeringReopen_ButStrictAuditRejectsUntilRebound()
    {
        var parent = Fresh();
        try
        {
            var source = Path.Combine(parent, "matrix.dbc");
            await File.WriteAllTextAsync(source, """
                VERSION ""
                NS_ :
                BS_:
                BU_: ECU Tester
                BO_ 291 Status: 8 ECU
                 SG_ Speed : 0|16@1+ (0.1,0) [0|6553.5] "km/h" Tester
                """);
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("ECU", parent);
            var dbc = await workspace.ImportDbcAsync(source);
            var test = await workspace.CreateTestProgramAsync("Case");
            var editor = await workspace.OpenTestEditorAsync(test.Id);
            var tx = new StepEditTransaction(editor.Document);
            tx.UpsertMapping(new TestTargetMapping("Speed", TestTargetKind.Signal, dbc.Id,
                dbc.Revision, dbc.Sha256, CanId: 291, SignalName: "Speed", Unit: "km/h"));
            await workspace.SaveTestEditorAsync(editor, tx.Current);
            var dbcEditor = await workspace.OpenDbcEditorAsync(dbc.Id);
            dbcEditor.Document.SetSignalAlias(291, "Speed", "车速");
            var changed = await workspace.SaveDbcEditorAsync(dbcEditor);
            var reopenedWorkspace = new ProjectWorkspaceService();
            var opened = await reopenedWorkspace.OpenAsync(project.ManifestPath);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProjectBundleContractAudit.ValidateAsync(opened.Definition, opened.ManifestPath));
            var reopened = await reopenedWorkspace.OpenTestEditorAsync(test.Id);
            var old = Assert.Single(reopened.Document.Mappings);
            Assert.Equal(dbc.Revision, old.AssetRevision);
            var repair = new StepEditTransaction(reopened.Document);
            repair.ReplaceMappings([old with { AssetRevision = changed.AssetRevision,
                AssetSha256 = changed.FileReceipt.Sha256 }]);
            repair.Undo();
            Assert.Equal(old, Assert.Single(repair.Current.Mappings));
            repair.Redo();
            await reopenedWorkspace.SaveTestEditorAsync(reopened, repair.Current);
            var current = reopenedWorkspace.Current!;
            await ProjectBundleContractAudit.ValidateAsync(current.Definition, current.ManifestPath);

            // Permitting stale pins does not permit damaged asset files.
            await File.AppendAllTextAsync(changed.FileReceipt.Path, "\n");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ProjectWorkspaceService().OpenAsync(project.ManifestPath));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task AuthorIfElseLoopWait_SaveCloseAndReopen_UsesCanonicalStepTree()
    {
        var parent = Fresh();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("TestEditor", parent);
            var asset = await workspace.CreateTestProgramAsync("MainTest");
            var editor = await workspace.OpenTestEditorAsync(asset.Id);
            var tx = new StepEditTransaction(editor.Document);

            var setup = StepUiCatalog.NewNode("SetVariable");
            var condition = StepUiCatalog.NewNode("If");
            var loop = StepUiCatalog.NewNode("Loop");
            var wait = StepUiCatalog.NewNode("Wait");
            var fallback = StepUiCatalog.NewNode("Expect");

            tx.Insert(TestPhase.Prepare, null, null, 0, setup);
            tx.Insert(TestPhase.Test, null, null, 0, condition);
            tx.Insert(TestPhase.Test, condition.Id, "then", 0, loop);
            tx.Insert(TestPhase.Test, loop.Id, "body", 0, wait);
            tx.Insert(TestPhase.Test, condition.Id, "else", 0, fallback);
            tx.ApplyProperties(wait.Id, "等待 300 ms",
                new Dictionary<string, string> { ["milliseconds"] = "300" });
            var expected = tx.Current;
            Assert.Empty(StepTreeValidator.ValidateTree(expected, StepDescriptorCatalog.Builtins()));

            tx.Undo();
            Assert.Equal("500", tx.Current.Walk().Single(x => x.Id == wait.Id).Properties["milliseconds"]);
            tx.Redo();
            Assert.Equal("300", tx.Current.Walk().Single(x => x.Id == wait.Id).Properties["milliseconds"]);

            var saved = await workspace.SaveTestEditorAsync(editor, tx.Current);
            Assert.Equal(2, saved.AssetRevision);
            Assert.Equal(expected.Id, saved.Document.Id);

            var checkProject = await ProjectRepository.OpenDefinitionAsync(project.ManifestPath);
            var updatedAsset = Assert.Single(checkProject.Assets);
            Assert.Equal(2, updatedAsset.Revision);
            Assert.Equal(saved.FileHash, updatedAsset.Sha256, ignoreCase: true);

            var anotherWorkspace = new ProjectWorkspaceService();
            await anotherWorkspace.OpenAsync(project.ManifestPath);
            var reopened = await anotherWorkspace.OpenTestEditorAsync(asset.Id);
            Assert.Equal(expected.Id, reopened.Document.Id);
            Assert.Equal(expected.Walk().Count(), reopened.Document.Walk().Count());
            var actualIf = Assert.Single(reopened.Document.Get(TestPhase.Test));
            Assert.Equal("If", actualIf.Type);
            Assert.Single(actualIf.Slots["then"]);
            Assert.Single(actualIf.Slots["else"]);
            Assert.Equal("Expect", actualIf.Slots["else"][0].Type);
            var savedLoop = Assert.Single(actualIf.Slots["then"]);
            Assert.Equal("Loop", savedLoop.Type);
            Assert.Equal("300", Assert.Single(savedLoop.Slots["body"]).Properties["milliseconds"]);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task ExternalFileChange_RefusesSave_AndKeepsProjectRevision()
    {
        var parent = Fresh();
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("TestEditor", parent);
            var asset = await workspace.CreateTestProgramAsync("Main");
            var editor = await workspace.OpenTestEditorAsync(asset.Id);
            var tx = new StepEditTransaction(editor.Document);
            tx.Insert(TestPhase.Test, null, null, 0, StepUiCatalog.NewNode("Wait"));
            await File.AppendAllTextAsync(editor.FilePath, "\n ");
            var revision = workspace.Current!.Definition.Revision;

            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => workspace.SaveTestEditorAsync(editor, tx.Current));
            Assert.Equal("TEST_FILE_CONFLICT", error.Code);
            Assert.Equal(revision, workspace.Current!.Definition.Revision);
            Assert.Contains(" ", await File.ReadAllTextAsync(editor.FilePath));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void EmptyBranchesAndUnknownProperty_AreValidatedByTestingBackend()
    {
        var tx = new StepEditTransaction(TestDocument.Create("Case"));
        var condition = StepUiCatalog.NewNode("If");
        tx.Insert(TestPhase.Test, null, null, 0, condition);
        Assert.Empty(StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()));

        var branch = StepUiCatalog.NewNode("Loop");
        tx.Insert(TestPhase.Test, condition.Id, "else", 0, branch);
        tx.Insert(TestPhase.Test, branch.Id, "body", 0, StepUiCatalog.NewNode("Wait"));
        Assert.Empty(StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()));

        var saved = TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(tx.Current));
        Assert.Equal(tx.Current.Walk().Count(), saved.Walk().Count());
    }

    [Fact]
    public void RecentProjectStore_PersistsAbsolutePath_WithoutCreatingProjectFiles()
    {
        var parent = Fresh();
        try
        {
            var store = new RecentProjectStore(Path.Combine(parent, "settings.json"));
            var manifest = Path.Combine(parent, "LvTong", "project.autotest-project.json");
            store.Remember(manifest);
            Assert.Equal(Path.GetFullPath(manifest), store.GetLastProjectManifest());
            Assert.Single(Directory.GetFiles(parent));
            var second = new RecentProjectStore(Path.Combine(parent, "settings.json"));
            Assert.Equal(manifest, second.GetLastProjectManifest());
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
