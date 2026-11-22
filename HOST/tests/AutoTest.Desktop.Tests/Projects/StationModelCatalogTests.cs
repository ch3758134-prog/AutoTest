using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class StationModelCatalogTests
{
    [Fact]
    public async Task DuplicateModelNeverSilentlySwitchesToAnotherProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "autotest-station-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var firstPath = Path.Combine(dir, "one", ProjectWorkspaceService.ManifestFileName);
            var secondPath = Path.Combine(dir, "two", ProjectWorkspaceService.ManifestFileName);
            var first = ProjectDefinition.Create("同一个显示名") with { EcuModelCode = "EPBA" };
            var second = ProjectDefinition.Create("同一个显示名") with { EcuModelCode = "EPBA" };
            await ProjectRepository.SaveDefinitionAsync(first, firstPath);
            await ProjectRepository.SaveDefinitionAsync(second, secondPath);
            var catalog = new StationModelCatalog(Path.Combine(dir, "installed-models.json"));
            var firstCtx = new OpenProjectContext(first, firstPath,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(firstPath))));
            await catalog.RegisterAsync(firstCtx);
            var secondCtx = new OpenProjectContext(second, secondPath,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(secondPath))));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                catalog.RegisterAsync(secondCtx));
            Assert.Equal(first.Id, (await catalog.ResolveAsync("EPBA")).Definition!.Id);
            Assert.Equal("STATION_MODEL_NOT_REGISTERED",
                (await catalog.ResolveAsync("EPBB")).Code);
            await catalog.UnregisterAsync("EPBA");
            await catalog.RegisterAsync(secondCtx);
            Assert.Equal(second.Id, (await catalog.ResolveAsync("EPBA")).Definition!.Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RegisteredProjectModelCannotBeChangedBehindStation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "autotest-station-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = Path.Combine(dir, ProjectWorkspaceService.ManifestFileName);
            var project = ProjectDefinition.Create("A") with { EcuModelCode = "EPBA" };
            await ProjectRepository.SaveDefinitionAsync(project, manifest);
            var catalog = new StationModelCatalog(Path.Combine(dir, "models.json"));
            await catalog.RegisterAsync(new OpenProjectContext(project, manifest, ""));
            await ProjectRepository.SaveDefinitionAsync(
                (project with { EcuModelCode = "EPBB" }).WithRevision(), manifest);
            Assert.Equal("STATION_MODEL_CHANGED", (await catalog.ResolveAsync("EPBA")).Code);
        }
        finally { Directory.Delete(dir, true); }
    }
}
