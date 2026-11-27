using System.IO;
using System.Text;
using AutoTest.Desktop.Projects;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestProjectPlanWorkflowTests
{
    [Fact]
    public void ProjectContainsOnlyOrderedCasesAndCanInvokeSameDefinitionTwice()
    {
        var target = Guid.NewGuid();
        var a = new TestProjectEntry(Guid.NewGuid(), target, "驻车");
        var b = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), "释放");
        var c = new TestProjectEntry(Guid.NewGuid(), target, "驻车复核");
        var document = TestProjectDocument.Create("HydEPB TEST") with
        { Packages = [a, b, c] };
        var reopened = TestProjectCodec.Decode(TestProjectCodec.Encode(document));
        Assert.Equal(TestProjectDocument.CurrentSchema, reopened.SchemaVersion);
        Assert.Equal(new[] { a.ItemId, b.ItemId, c.ItemId },
            reopened.AutomaticRunEntries().Select(x => x.ItemId).ToArray());
        Assert.Equal(a.PackageAssetId, reopened.Packages[2].PackageAssetId);
    }

    [Fact]
    public void OldSchemasAndUnrecognizedLegacyFlagsAreRejected()
    {
        var v2 = new TestProjectDocument(2, Guid.NewGuid(), "Old TEST", []);
        Assert.Throws<InvalidDataException>(() => TestProjectCodec.Encode(v2));
        var newDocument = TestProjectDocument.Create("New TEST");
        var json = Encoding.UTF8.GetString(TestProjectCodec.Encode(newDocument));
        var legacy = json.Replace("\"Packages\": []", "\"Packages\": [], \"Enabled\": false");
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Decode(Encoding.UTF8.GetBytes(legacy)));
    }

    [Fact]
    public void InvalidOrDuplicateInvocationIdCannotBePersisted()
    {
        var entry = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), "驻车");
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Encode(TestProjectDocument.Create("TEST") with
            { Packages = [entry, entry] }));
        Assert.Throws<InvalidDataException>(() =>
            TestProjectCodec.Encode(TestProjectDocument.Create("TEST") with
            { Packages = [entry with { ItemId = Guid.Empty }] }));
    }

    [Fact]
    public async Task EditedCaseIsAutomaticallyResolvedByCurrentDefinitionWithoutAdoption()
    {
        var parent = Path.Combine(Path.GetTempPath(),
            "at-current-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var created = await workspace.CreateAsync("Suite", parent);
            var source = await workspace.CreateTestProgramAsync("驻车");
            var project = await workspace.CreateTestProjectAsync("TEST");
            var edit = await workspace.OpenTestProjectEditorAsync(project.Id);
            var item = new TestProjectEntry(Guid.NewGuid(), source.Id.Value, "驻车");
            await workspace.SaveTestProjectEditorAsync(edit, edit.Document with
            { Packages = [item] });

            var caseEditor = await workspace.OpenTestEditorAsync(source.Id);
            await workspace.SaveTestEditorAsync(caseEditor,
                caseEditor.Document with { Name = "驻车 - 已编辑" });

            var reopened = new ProjectWorkspaceService();
            await reopened.OpenAsync(created.ManifestPath);
            var selected = await reopened.OpenTestProjectEditorAsync(project.Id);
            Assert.Equal(item, Assert.Single(selected.Document.Packages));
            var live = await AutomaticTestResourceContract.ResolveAsync(reopened.Current!);
            Assert.Equal("驻车 - 已编辑", Assert.Single(live.Cases).Program.Name);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
