using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionBarcodePreflightTests
{
    private const string Barcode = "UT1-EPBA-H02-000000123456-4372";

    [Fact]
    public void MatchingModelPreservesExactSerialAndNeverAuthorizesRun()
    {
        var project = ProjectDefinition.Create("可随意更改的工程名") with { EcuModelCode = "EPBA" };
        var result = ProductionBarcodePreflight.Inspect(Barcode, project);
        Assert.True(result.IdentityMatched);
        Assert.Equal("000000123456", result.DutSerial);
        Assert.Equal("EPBA", result.ModelCode);
        Assert.Contains("仅完成身份预检", result.Explanation);
    }

    [Fact]
    public void SerialOnlyScannerUsesCurrentEcuProjectWithoutSecondModelRegistration()
    {
        var project = ProjectDefinition.Create("任何工程名称") with
        {
            EcuModelCode = "HYDEPB"
        };
        var policy = new AutoTest.Specialists.Scan.ScanIdentityPolicy(1,
            AutoTest.Specialists.Scan.ScanIdentityPolicy.SerialOnly,
            "-", null, 8, 20);
        var valid = ProductionBarcodePreflight.Inspect("000000123456",
            project, policy);
        Assert.True(valid.IdentityMatched);
        Assert.Equal("000000123456", valid.DutSerial);
        Assert.Equal("HYDEPB", valid.ModelCode);

        // Without an explicit ECU project it must never guess a model.
        var withoutProject = ProductionBarcodePreflight.Inspect(
            "000000123456", null, policy);
        Assert.False(withoutProject.IdentityMatched);
        Assert.Equal("SCAN_PROJECT_MODEL_REQUIRED", withoutProject.Code);
    }

    [Fact]
    public void LegacyUnboundProjectMustNotGuessModelFromDisplayName()
    {
        var project = ProjectDefinition.Create("EPBA");
        Assert.Equal("SCAN_PROJECT_MODEL_UNBOUND",
            ProductionBarcodePreflight.Inspect(Barcode, project).Code);
    }

    [Fact]
    public void OtherModelAndBadCrcAreRejectedWithoutRecipeFallback()
    {
        var project = ProjectDefinition.Create("工程") with { EcuModelCode = "EPBB" };
        Assert.Equal("SCAN_OTHER_MODEL", ProductionBarcodePreflight.Inspect(Barcode, project).Code);
        Assert.Equal("BARCODE_CRC_MISMATCH",
            ProductionBarcodePreflight.Inspect("UT1-EPBA-H02-000000123456-4373", project).Code);
    }

    [Fact]
    public void ModelCodeIsValidatedSeparatelyFromDisplayName()
    {
        Assert.Throws<ProjectDataException>(() =>
            (ProjectDefinition.Create("工程") with { EcuModelCode = "epba" }).Validate());
    }
}
