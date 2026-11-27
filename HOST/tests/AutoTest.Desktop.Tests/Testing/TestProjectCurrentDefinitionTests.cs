using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class TestProjectCurrentDefinitionTests
{
    [Fact]
    public void DeveloperSelectionNeverChangesFormalTestProgram()
    {
        var entry = new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), "驻车");
        var row = new TestProjectPackageRow(entry);
        row.RunSelected = false;
        Assert.Equal(entry, row.ToEntry());
        Assert.False(row.RunSelected);
        row.RunSelected = true;
        Assert.Equal(entry, row.ToEntry());
    }

    [Fact]
    public void CaseAvailabilityDoesNotRequireAdoptingNewRevision()
    {
        var row = new TestProjectPackageRow(
            new TestProjectEntry(Guid.NewGuid(), Guid.NewGuid(), "释放"));
        row.RefreshAvailableRevision(1, new string('A', 64));
        Assert.True(row.ResourceAvailable);
        var entry = row.ToEntry();
        row.RefreshAvailableRevision(5, new string('B', 64));
        Assert.Equal(entry, row.ToEntry());
        Assert.True(row.ResourceAvailable);
        row.RefreshAvailableRevision(null, null);
        Assert.False(row.ResourceAvailable);
    }
}
