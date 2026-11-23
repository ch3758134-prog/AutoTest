using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestSystemConfigurationTests
{
    [Theory]
    [InlineData(ProjectWorkspaceService.TestConfigurationKind)]
    [InlineData(ProjectWorkspaceService.BenchConfigurationKind)]
    public async Task RedundantSutAndFixedBenchConfigurations_AreNotNewProjectAssets(string kind)
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "at-retired-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU Test", folder);
            var before = workspace.Current!.Definition.Assets.Length;
            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => workspace.CreateConfigurationAsync(kind, "NotNeeded"));
            Assert.Equal("LEGACY_CONFIGURATION_RETIRED", error.Code);
            Assert.Equal(before, workspace.Current!.Definition.Assets.Length);
            Assert.Null(workspace.SelectedTestConfiguration);
            Assert.Null(workspace.SelectedBenchConfiguration);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(ProjectWorkspaceService.TestConfigurationKind)]
    [InlineData(ProjectWorkspaceService.BenchConfigurationKind)]
    public void ExistingTcfsAndTbcs_KeepTheirCodecForBackwardCompatibility(string kind)
    {
        var bytes = ProjectConfigurationCodec.Create(kind, "Old Asset");
        var decoded = ProjectConfigurationCodec.Decode(kind, bytes);
        Assert.Equal(bytes, ProjectConfigurationCodec.Encode(kind, decoded));
        if (kind == ProjectWorkspaceService.BenchConfigurationKind)
        {
            Assert.IsType<TestBenchConfigurationDocument>(decoded);
            Assert.Equal(0, TestBenchConfigurationDocument.KeySimCanPort);
            Assert.Equal(1, TestBenchConfigurationDocument.DutCanPort);
        }
        else Assert.IsType<TestSystemConfigurationDocument>(decoded);
    }

    [Fact]
    public void TcfDeclinesIncompletePinnedDbcReference()
    {
        var invalid = TestSystemConfigurationDocument.Create("ECU") with
        {
            DbcAssetId = Guid.NewGuid(),
            DbcRevision = 1
        };
        var error = Assert.Throws<ProjectDataException>(() => invalid.Validate());
        Assert.Equal("TCF_PIN", error.Code);
    }
}
