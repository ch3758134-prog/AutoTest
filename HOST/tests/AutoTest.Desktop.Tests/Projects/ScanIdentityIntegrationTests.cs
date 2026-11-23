using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ScanIdentityIntegrationTests
{
    [Fact]
    public void StationFormatMatchesOfficialModelButNeverAuthorizesProduction()
    {
        var project = ProjectDefinition.Create("随意的工程显示名称") with
        {
            EcuModelCode = "HYDEPB"
        };
        var format = new ScanIdentityPolicy(1, ScanIdentityPolicy.ModelSerial,
            "-", null, 8, 20);
        var match = ProductionBarcodePreflight.Inspect(
            "HYDEPB-000000123456", project, format);
        Assert.True(match.IdentityMatched);
        Assert.Equal("000000123456", match.DutSerial);
        Assert.Equal("SCAN_MODEL_MATCHED", match.Code);

        var foreign = ProductionBarcodePreflight.Inspect(
            "EPB02-000000123456", project, format);
        Assert.False(foreign.IdentityMatched);
        Assert.Equal("SCAN_OTHER_MODEL", foreign.Code);
    }

    [Fact]
    public void SerialOnlyNeverInfersModelFromSerialOrDisplayName()
    {
        var project = ProjectDefinition.Create("HYDEPB");
        var serialOnly = new ScanIdentityPolicy(1, ScanIdentityPolicy.SerialOnly,
            "-", "HYDEPB", 8, 20);
        var unresolved = ProductionBarcodePreflight.Inspect(
            "000000123456", project, serialOnly);
        Assert.Equal("SCAN_PROJECT_MODEL_UNBOUND", unresolved.Code);
        Assert.Equal("000000123456", unresolved.DutSerial);
    }
}
