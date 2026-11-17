using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class WorkspaceStartPolicyTests
{
    [Theory]
    [InlineData(false, 0, false, "none")]
    [InlineData(false, 4, true, "none")]
    [InlineData(true, 0, false, "project")]
    [InlineData(true, 0, true, "project")]
    [InlineData(true, 1, false, "case")]
    [InlineData(true, 30, false, "case")]
    [InlineData(true, 1, true, "ready")]
    [InlineData(true, 30, true, "ready")]
    public void StartActionDependsOnRealProjectReferences(
        bool projectOpen, int projects, bool linked, string expected)
    {
        Assert.Equal(expected, WorkspaceStartPolicy.Choose(projectOpen, projects, linked));
    }
}
