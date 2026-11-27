using System.IO;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class TestSystemConfigurationTests
{
    [Theory]
    [InlineData("test-configuration")]
    [InlineData("bench-configuration")]
    public async Task RetiredConfigurationTypesCannotBeCreatedOrDecoded(string kind)
    {
        var parent = Path.Combine(Path.GetTempPath(),
            "at-removed-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("EPB", parent);
            var before = workspace.Current!.Definition.Assets.Length;
            await Assert.ThrowsAnyAsync<ArgumentException>(
                () => workspace.CreateConfigurationAsync(kind, "Legacy"));
            Assert.Equal(before, workspace.Current!.Definition.Assets.Length);
            Assert.False(ProjectConfigurationCodec.IsConfiguration(kind));
            Assert.Throws<ArgumentException>(() =>
                ProjectConfigurationCodec.Decode(kind, [1,2,3]));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
