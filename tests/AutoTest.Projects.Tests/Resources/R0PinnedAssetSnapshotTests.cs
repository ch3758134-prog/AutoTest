using System.Collections.Immutable;
using System.Text;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using Xunit;

namespace AutoTest.Projects.Tests.Resources;

public sealed class R0PinnedAssetSnapshotTests
{
    private static async Task<(string root, string manifest, string file, AssetEntry asset, R0ProjectAssetWorkspace ws)> SetupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "R0Pinned_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manifest = Path.Combine(root, "project.json");
        var file = Path.Combine(root, "asset.dbc");
        var bytes = Encoding.UTF8.GetBytes("source-bytes");
        await File.WriteAllBytesAsync(file, bytes);
        var asset = AssetEntry.Create("DBC", "Signals", "asset.dbc", bytes);
        await ProjectRepository.SaveDefinitionAsync(ProjectDefinition.Create("Local") with
        { Assets = ImmutableArray.Create(asset) }, manifest);
        var ws = await R0ProjectAssetWorkspace.OpenAsync(manifest);
        return (root, manifest, file, asset, ws);
    }

    [Fact]
    public async Task MatchingSnapshot_IsAcceptedWithoutIncrementingRevision()
    {
        var x = await SetupAsync();
        try
        {
            var verified = await x.ws.VerifyPinnedAssetAsync(x.asset.Id, x.asset.Revision, x.asset.Sha256);
            Assert.True(verified.IsValid);
            Assert.Equal(x.asset.Id, verified.Asset.Id);
            Assert.Equal(1, x.ws.Definition.Revision);
        }
        finally { Directory.Delete(x.root, true); }
    }

    [Fact]
    public async Task ChangedAssetFile_RejectsPinnedReplay()
    {
        var x = await SetupAsync();
        try
        {
            await File.AppendAllTextAsync(x.file, "!", Encoding.UTF8);
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                x.ws.VerifyPinnedAssetAsync(x.asset.Id, x.asset.Revision, x.asset.Sha256));
            Assert.Equal("ASSET_STALE", error.Code);
        }
        finally { Directory.Delete(x.root, true); }
    }

    [Fact]
    public async Task ExternalManifestEdit_RejectsPreviouslyBoundRevision()
    {
        var x = await SetupAsync();
        try
        {
            await File.AppendAllTextAsync(x.manifest, " ", Encoding.UTF8);
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                x.ws.VerifyPinnedAssetAsync(x.asset.Id, x.asset.Revision, x.asset.Sha256));
            Assert.Equal("PROJECT_CONFLICT", error.Code);
        }
        finally { Directory.Delete(x.root, true); }
    }

    [Fact]
    public async Task WrongExpectedRevision_RejectsWithoutAnyFileWrites()
    {
        var x = await SetupAsync();
        try
        {
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                x.ws.VerifyPinnedAssetAsync(x.asset.Id, x.asset.Revision + 1, x.asset.Sha256));
            Assert.Equal("REFERENCE_STALE", error.Code);
            Assert.Equal("source-bytes", await File.ReadAllTextAsync(x.file));
        }
        finally { Directory.Delete(x.root, true); }
    }
}
