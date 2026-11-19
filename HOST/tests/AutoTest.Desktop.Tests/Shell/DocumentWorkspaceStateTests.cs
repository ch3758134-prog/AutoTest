using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class DocumentWorkspaceStateTests
{
    [Fact]
    public void FailedTabSwitchOrOpenNeverChangesActiveOrAddsHiddenTab()
    {
        var state = new DocumentWorkspaceState<string>();
        Assert.True(state.TryOpen("TEST", _ => true));
        Assert.False(state.TryOpen("DBC", _ => false));
        Assert.Equal("TEST", state.Active);
        Assert.Equal(new[] { "TEST" }, state.Tabs);
        Assert.False(state.TryActivate(null, _ => false));
        Assert.Equal("TEST", state.Active);
    }

    [Fact]
    public void ApprovedCloseSelectsNeighborWithoutSecondPrompt()
    {
        var state = new DocumentWorkspaceState<string>();
        state.TryOpen("TEST", _ => true);
        state.TryOpen("PROJECT", _ => true);
        Assert.True(state.RemoveApproved("PROJECT"));
        Assert.Equal("TEST", state.Active);
        Assert.Equal(new[] { "TEST" }, state.Tabs);
    }

    [Fact]
    public void ProjectSwitchClearsAllDocumentSessionsOnlyAfterApproval()
    {
        var state = new DocumentWorkspaceState<string>();
        state.TryOpen("TEST", _ => true);
        state.ClearApproved();
        Assert.Null(state.Active);
        Assert.Empty(state.Tabs);
    }
}
