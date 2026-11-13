using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class WaveProfileMappingTests
{
    [Fact]
    public async Task WaveAnalysisConfiguration_IsSelectableAsRevisionPinnedTestMapping()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-wave-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("WaveMapping", root);
            var saved = await workspace.CreateConfigurationAsync(
                ProjectWorkspaceService.WaveSettingsKind, "CurrentLimit");
            var choices = await TestTargetCatalog.LoadAsync(workspace);
            var wave = Assert.Single(choices, x => x.Mapping.Kind == TestTargetKind.WaveProfile);
            Assert.Equal(saved.Id, wave.Mapping.Asset);
            Assert.Equal(saved.Revision, wave.Mapping.AssetRevision);
            Assert.Equal(saved.Sha256, wave.Mapping.AssetSha256);
            Assert.Equal("CurrentLimit", wave.Mapping.Name);
            wave.Mapping.Validate();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
