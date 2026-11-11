using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class R1ToolWindowPolicyTests
{
    [Theory]
    [InlineData("station")]
    [InlineData("can")]
    [InlineData("dbc")]
    [InlineData("controller")]
    [InlineData("bench")]
    [InlineData("wave")]
    [InlineData("uds")]
    [InlineData("flash")]
    [InlineData("scan")]
    [InlineData("records")]
    [InlineData("operations")]
    public void ProfessionalToolsMustUseIndependentWindows(string key)
    {
        Assert.True(R1ToolWindowPolicy.IsProfessionalTool(key));
        Assert.False(R1ToolWindowPolicy.IsMainDocument(key));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("program")]
    public void TestRunnerAndTestAuthoringStayInMainIdeDocument(string key)
    {
        Assert.True(R1ToolWindowPolicy.IsMainDocument(key));
        Assert.False(R1ToolWindowPolicy.IsProfessionalTool(key));
    }

    [Fact]
    public void StationAndCanShareOneRealDeviceWindow()
    {
        Assert.Equal("station", R1ToolWindowPolicy.Normalize("can"));
        Assert.Equal("station", R1ToolWindowPolicy.Normalize("station"));
        Assert.Equal("flash", R1ToolWindowPolicy.Normalize("flash"));
    }

    [Fact]
    public void UnknownPagesAndHistoricalP01TabsAreNeverTools()
    {
        Assert.False(R1ToolWindowPolicy.IsProfessionalTool("project"));
        Assert.False(R1ToolWindowPolicy.IsProfessionalTool("P01"));
        Assert.False(R1ToolWindowPolicy.IsProfessionalTool("Mock"));
    }
}
