using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionTestInvocationPinTests
{
    private static (TestProjectEntry Entry, AssetEntry Asset) Pinned()
    {
        var id = AssetId.New();
        var sha = new string('A', 64);
        var asset = new AssetEntry(id, 4, ProjectWorkspaceService.TestProgramKind,
            "ECU 基础检查", "test/base.json", sha);
        var entry = new TestProjectEntry(Guid.NewGuid(), id.Value, 4,
            sha, "ECU 基础检查", true);
        return (entry, asset);
    }

    [Fact]
    public void ProductionAcceptanceRequiresExactCaseAssetRevisionAndFingerprint()
    {
        var (entry, asset) = Pinned();
        AutomaticTestResourceContract.RequirePinnedInvocation(entry, asset);
        AutomaticTestResourceContract.RequirePinnedInvocation(
            entry with { PackageSha256 = entry.PackageSha256.ToLowerInvariant() },
            asset);
    }

    [Fact]
    public void ProductionMustRejectImplicitLatestRevisionAdoption()
    {
        var (entry, asset) = Pinned();
        var error = Assert.Throws<InvalidDataException>(() =>
            AutomaticTestResourceContract.RequirePinnedInvocation(entry,
                asset with { Revision = asset.Revision + 1 }));
        Assert.Contains("禁止静默", error.Message);
    }

    [Fact]
    public void ProductionMustRejectEqualRevisionDifferentShaAndWrongSource()
    {
        var (entry, asset) = Pinned();
        Assert.Throws<InvalidDataException>(() =>
            AutomaticTestResourceContract.RequirePinnedInvocation(entry,
                asset with { Sha256 = new string('B', 64) }));
        Assert.Throws<InvalidDataException>(() =>
            AutomaticTestResourceContract.RequirePinnedInvocation(entry,
                asset with { Id = AssetId.New() }));
        Assert.Throws<InvalidDataException>(() =>
            AutomaticTestResourceContract.RequirePinnedInvocation(entry,
                asset with { Revision = asset.Revision - 1 }));
    }
}
