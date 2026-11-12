using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProjectResourceManagementTests
{
    [Fact]
    public async Task OpenExistingProject_SharesStoredProjectIdentity()
    {
        var parent = TemporaryDirectory();
        try
        {
            var first = new ProjectWorkspaceService();
            var created = await first.CreateAsync("EPB_Test", parent);
            var second = new ProjectWorkspaceService();
            var opened = await second.OpenAsync(created.ManifestPath);
            Assert.Equal(created.Definition.Id, opened.Definition.Id);
            Assert.Equal("EPB_Test", opened.Definition.Name);
            Assert.Empty(opened.Definition.Assets);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task NewTestAndImportedDbc_AreFormalResources_AndSurviveReopen()
    {
        var parent = TemporaryDirectory();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var created = await workspace.CreateAsync("ECU", parent);
            var source = Path.Combine(parent, "known.dbc");
            await File.WriteAllTextAsync(source, "VERSION \"1.0\"\n");
            var test = await workspace.CreateTestProgramAsync("诊断步骤");
            var dbc = await workspace.ImportDbcAsync(source);
            Assert.Equal(ProjectWorkspaceService.TestProgramKind, test.Kind);
            Assert.Equal(ProjectWorkspaceService.DbcKind, dbc.Kind);
            Assert.True(File.Exists(Path.Combine(created.RootDirectory, test.RelativePath)));
            var copied = Path.Combine(created.RootDirectory, dbc.RelativePath);
            Assert.True(File.Exists(copied));

            var reopened = await new ProjectWorkspaceService().OpenAsync(created.ManifestPath);
            Assert.Equal(2, reopened.Definition.Assets.Length);
            Assert.Equal(3, reopened.Definition.Revision);
            Assert.Contains(reopened.Definition.Assets, a => a.Id == test.Id);
            Assert.Contains(reopened.Definition.Assets, a => a.Id == dbc.Id);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task RenameAndRemoveReference_ChangeManifestButNeverDeleteTheFile()
    {
        var parent = TemporaryDirectory();
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            var test = await workspace.CreateTestProgramAsync("初始名称");
            var target = Path.Combine(workspace.Current!.RootDirectory, test.RelativePath);
            await workspace.RenameAssetAsync(test.Id, "新名称");
            Assert.Contains(workspace.Current!.Definition.Assets, x =>
                x.Id == test.Id && x.Name == "新名称");
            await workspace.RemoveAssetReferenceAsync(test.Id);
            Assert.Empty(workspace.Current!.Definition.Assets);
            Assert.True(File.Exists(target));
            var reopened = await ProjectRepository.OpenDefinitionAsync(workspace.Current.ManifestPath);
            Assert.Empty(reopened.Assets);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ExternalManifestMutation_RejectsStaleWrite()
    {
        var parent = TemporaryDirectory();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var created = await workspace.CreateAsync("ECU", parent);
            await File.AppendAllTextAsync(created.ManifestPath, "\n ");
            await Assert.ThrowsAsync<IOException>(
                () => workspace.CreateTestProgramAsync("不得覆盖旧文件"));
            Assert.Empty(workspace.Current!.Definition.Assets);
            Assert.False(Directory.Exists(Path.Combine(created.RootDirectory, "assets")));
        }
        finally { Directory.Delete(parent, true); }
    }

    private static string TemporaryDirectory()
    {
        var p = Path.Combine(Path.GetTempPath(), "autotest-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }
}
