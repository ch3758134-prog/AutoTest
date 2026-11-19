using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProjectCreationSmokeTests
{
    [Fact]
    public async Task CreateProject_PersistsAndReopensSameIdentity()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-project-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var current = await workspace.CreateAsync("EPB_Test", parent);

            Assert.Equal("EPB_Test", current.Definition.Name);
            Assert.Equal(current, workspace.Current);
            Assert.Equal(Path.Combine(parent, "EPB_Test", ProjectWorkspaceService.ManifestFileName),
                current.ManifestPath);
            Assert.True(File.Exists(current.ManifestPath));

            var reopened = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath);
            Assert.Equal(current.Definition.Id, reopened.Id);
            Assert.Equal(current.Definition.Revision, reopened.Revision);
            Assert.Empty(reopened.Assets);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateName_DoesNotOverwriteOriginalOrSwitchWorkspace()
    {
        var parent = Path.Combine(Path.GetTempPath(), "at-project-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var original = await workspace.CreateAsync("Fixture", parent);
            var before = await File.ReadAllBytesAsync(original.ManifestPath);

            await Assert.ThrowsAsync<IOException>(() => workspace.CreateAsync("Fixture", parent));

            Assert.Equal(original.Definition.Id, workspace.Current!.Definition.Id);
            Assert.Equal(before, await File.ReadAllBytesAsync(original.ManifestPath));
            Assert.Single(Directory.GetDirectories(parent));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("A/B")]
    [InlineData("name.")]
    public void InvalidName_ProducesValidationMessage(string name)
    {
        Assert.NotEmpty(ProjectWorkspaceService.ValidateInput(name, Path.GetTempPath()));
    }
}
