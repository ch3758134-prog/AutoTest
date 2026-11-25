using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class TestProjectPinnedRevisionTests
{
    [Fact]
    public void NewerTestRevisionIsNotAdoptedUntilExplicitAction()
    {
        var item = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 1,
            new string('A', 64), "驻车测试", true);
        var row = new TestProjectPackageRow(item) { RunSelected = false };
        row.RefreshAvailableRevision(2, new string('B', 64));

        Assert.True(row.CanAdoptLatest);
        Assert.True(row.HasReferenceMismatch);
        Assert.Contains("绑定 r1 → 当前 r2", row.PinStatus);
        Assert.Equal(item.PackageSha256, row.ToEntry().PackageSha256);
        Assert.Equal(1, row.ToEntry().PackageRevision);
        Assert.False(row.RunSelected);

        row.AdoptCurrentRevision();
        Assert.False(row.CanAdoptLatest);
        Assert.False(row.HasReferenceMismatch);
        Assert.Equal(2, row.ToEntry().PackageRevision);
        Assert.Equal(new string('B', 64), row.ToEntry().PackageSha256);
        Assert.True(row.ToEntry().Enabled);
        Assert.False(row.RunSelected);
    }

    [Fact]
    public void MissingAssetCannotBeSilentlyRebound()
    {
        var item = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 3,
            new string('C', 64), "释放测试", true);
        var row = new TestProjectPackageRow(item);
        row.RefreshAvailableRevision(null, null);
        Assert.Equal("资源已移除", row.PinStatus);
        Assert.True(row.HasReferenceMismatch);
        Assert.False(row.CanAdoptLatest);
        row.AdoptCurrentRevision();
        Assert.Equal(3, row.Revision);
        Assert.Equal(item.PackageSha256, row.Sha256);
    }

    [Fact]
    public void SameRevisionDifferentHashBlocksUnversionedResourceReplacement()
    {
        var item = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 4,
            new string('A', 64), "功能检查", true);
        var row = new TestProjectPackageRow(item);
        row.RefreshAvailableRevision(4, new string('B', 64));
        Assert.True(row.HasReferenceMismatch);
        Assert.False(row.CanAdoptLatest);
        Assert.Contains("SHA256 不一致", row.PinStatus);
        row.AdoptCurrentRevision(); // Explicit rebind cannot bless an unversioned edit.
        Assert.Equal(4, row.ToEntry().PackageRevision);
        Assert.Equal(new string('A', 64), row.ToEntry().PackageSha256);
    }

    [Fact]
    public void RevisionRefreshDoesNotAlterTemporaryDevelopmentSelection()
    {
        var item = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 1,
            new string('C', 64), "释放检查", true);
        var row = new TestProjectPackageRow(item) { RunSelected = false };
        row.RefreshAvailableRevision(2, new string('D', 64));
        row.AdoptCurrentRevision();
        Assert.False(row.RunSelected);
        Assert.True(row.ToEntry().Enabled);
    }

    [Fact]
    public void OlderResourceRevisionCannotDowngradePinnedExecution()
    {
        var item = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), 8,
            new string('E', 64), "驻车检查", true);
        var row = new TestProjectPackageRow(item);
        row.RefreshAvailableRevision(6, new string('F', 64));
        Assert.True(row.HasReferenceMismatch);
        Assert.False(row.CanAdoptLatest);
        Assert.Contains("修订倒退", row.PinStatus);
        row.AdoptCurrentRevision();
        Assert.Equal(8, row.ToEntry().PackageRevision);
        Assert.Equal(new string('E', 64), row.ToEntry().PackageSha256);
    }

    [Fact]
    public void PinnedProjectCaseRejectsLatestResourceUntilExplicitlyAdopted()
    {
        var row = new TestProjectPackageRow(new TestProjectEntry(
            Guid.NewGuid(), Guid.NewGuid(), 7, new string('A', 64), "原测试", true));
        row.RefreshAvailableRevision(8, new string('B', 64));

        // A test project cannot silently freeze the newer source and claim its
        // old pinned SHA still represents what will be executed.
        Assert.False(row.MatchesPinnedRevision(8, new string('B', 64)));
        Assert.True(row.MatchesPinnedRevision(7, new string('a', 64)));
        row.AdoptCurrentRevision();
        Assert.True(row.MatchesPinnedRevision(8, new string('B', 64)));
        Assert.False(row.MatchesPinnedRevision(7, new string('A', 64)));
    }

    [Fact]
    public void RefreshOfUnchangedAvailableRevisionDoesNotRaiseChangeEvents()
    {
        var row = new TestProjectPackageRow(new TestProjectEntry(
            Guid.NewGuid(), Guid.NewGuid(), 3, new string('C', 64), "原测试", true));
        row.RefreshAvailableRevision(3, new string('C', 64));
        var raised = 0;
        row.PropertyChanged += (_, _) => raised++;
        row.RefreshAvailableRevision(3, new string('c', 64));
        Assert.Equal(0, raised);
    }
}
