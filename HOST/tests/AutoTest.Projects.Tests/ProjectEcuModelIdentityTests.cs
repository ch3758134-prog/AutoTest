using System.IO;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests;

public sealed class ProjectEcuModelIdentityTests
{
    [Fact]
    public async Task LegacyProjectWithoutModelCanBeOpenedThenRoundTripsModelAndRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifest = Path.Combine(root, "project.autotest-project.json");
            var legacy = ProjectDefinition.Create("任意显示名");
            await ProjectRepository.SaveDefinitionAsync(legacy, manifest);
            var opened = await ProjectRepository.OpenDefinitionAsync(manifest);
            Assert.Null(opened.EcuModelCode);
            var updated = (opened with { EcuModelCode = "EPBA" }).WithRevision().Validate();
            await ProjectRepository.SaveDefinitionAsync(updated, manifest);
            var reread = await ProjectRepository.OpenDefinitionAsync(manifest);
            Assert.Equal(legacy.Id, reread.Id);
            Assert.Equal(legacy.Revision + 1, reread.Revision);
            Assert.Equal("任意显示名", reread.Name);
            Assert.Equal("EPBA", reread.EcuModelCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("epba")]
    [InlineData("EPB-A")]
    [InlineData("")]
    public void InvalidModelCodeIsNeverStoredAsProductionIdentity(string code)
    {
        var project = ProjectDefinition.Create("任意工程") with { EcuModelCode = code };
        Assert.Throws<ProjectDataException>(() => project.Validate());
    }
}
