using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using Xunit;
namespace AutoTest.Projects.Tests;
public sealed class ProjectRepositoryContractTests
{
    [Fact] public async Task Verify_B1_T01()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "project.json");
            var project = ProjectDefinition.Create("Fixture");
            await ProjectRepository.SaveDefinitionAsync(project, file);
            var read = await ProjectRepository.OpenDefinitionAsync(file);
            Assert.Equal(project.Id, read.Id);
            Assert.Equal(project.Name, read.Name);
            Assert.Equal(project.Revision, read.Revision);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public void Verify_B1_T02()
    {
        var edit = new ResourceEditService(ProjectDefinition.Create("Test"));
        var asset = edit.Add("dbc", "old", "assets/test.dbc", [1, 2]);
        edit.Rename(asset.Id, "new");
        Assert.Equal(asset.Id, edit.Current.Assets.Single().Id);
        Assert.Equal(asset.Sha256, edit.Current.Assets.Single().Sha256);
    }
    [Fact] public void Verify_B1_T03()
    {
        var edit = new ResourceEditService(ProjectDefinition.Create("Test"));
        var source = edit.Add("test", "step", "assets/step.json", [1]);
        var target = edit.Add("dbc", "signal", "assets/test.dbc", [2]);
        edit.Link(source.Id, target.Id);
        var ex = Assert.Throws<ProjectDataException>(() => edit.Remove(target.Id));
        Assert.Equal("ASSET_REFERENCED", ex.Code);
    }
    [Fact] public async Task Verify_B1_T04()
    {
        Assert.Equal("PATH_OUTSIDE_ROOT", Assert.Throws<ProjectDataException>(() =>
            ProjectPathPolicy.Normalize(Path.GetTempPath(), "../outside.dbc")).Code);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{broken");
            var result = await new ProjectRepository().OpenAsync(path, CancellationToken.None);
            Assert.Equal(Outcome.Rejected, result.Outcome);
            Assert.Equal("PROJECT_CORRUPT", result.FailureCode);
        }
        finally { File.Delete(path); }
    }
    [Fact] public void Verify_B1_T05()
    {
        var catalog = new AssetCatalog();
        var a = catalog.Import("dbc", "db", "assets/db.dbc", [1]);
        var b = catalog.ReplaceRevision(a.Id, [2]);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(2, b.Revision);
        Assert.NotEqual(a.Sha256, b.Sha256);
        Assert.Equal(Outcome.Rejected, catalog.Resolve(a.Id, AssetRevision.Create(1)).Outcome);
    }
    [Fact] public async Task Verify_B1_T06()
    {
        // Pure filesystem, no network calls, services or hardware dependencies.
        var path = Path.Combine(Path.GetTempPath(), "offline-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await ProjectRepository.SaveDefinitionAsync(ProjectDefinition.Create("Offline"), path);
            Assert.Equal("Offline", (await ProjectRepository.OpenDefinitionAsync(path)).Name);
        }
        finally { File.Delete(path); }
    }
}
