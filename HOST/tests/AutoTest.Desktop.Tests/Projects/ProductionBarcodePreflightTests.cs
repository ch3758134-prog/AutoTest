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
    [Fact]
    public void MappedBarcodeWithoutModelFieldUsesOnlyCurrentProjectIdentity()
    {
        var project = ProjectDefinition.Create("工程显示名可改") with
        {
            EcuModelCode = "HYDEPB"
        };
        var policy = new AutoTest.Specialists.Scan.ScanIdentityPolicy(1,
            AutoTest.Specialists.Scan.ScanIdentityPolicy.Mapped, "-",
            null, 1, 64,
            AutoTest.Specialists.Scan.ScanIdentityPolicy.Delimited,
            [
                new(AutoTest.Specialists.Scan.ScanIdentityPolicy.SerialTarget, 1),
                new("BatchCode", 2)
            ]);
        var matched = ProductionBarcodePreflight.Inspect(
            "00001234-LOT05", project, policy);
        Assert.True(matched.IdentityMatched);
        Assert.Equal("HYDEPB", matched.ModelCode);
        Assert.Equal("00001234", matched.DutSerial);
        Assert.Equal("LOT05", matched.MappedFields!["BatchCode"]);
        Assert.False(ProductionBarcodePreflight.Inspect(
            "00001234-LOT05", null, policy).IdentityMatched);
    }

    [Fact]
    public void MappedModelFieldCannotSilentlySwitchToOtherProject()
    {
        var project = ProjectDefinition.Create("HydEPB") with
        {
            EcuModelCode = "HYDEPB"
        };
        var policy = new AutoTest.Specialists.Scan.ScanIdentityPolicy(1,
            AutoTest.Specialists.Scan.ScanIdentityPolicy.Mapped, "|",
            null, 1, 64,
            AutoTest.Specialists.Scan.ScanIdentityPolicy.Delimited,
            [
                new(AutoTest.Specialists.Scan.ScanIdentityPolicy.ModelTarget, 1),
                new(AutoTest.Specialists.Scan.ScanIdentityPolicy.SerialTarget, 2)
            ]);
        var rejected = ProductionBarcodePreflight.Inspect(
            "OTHER|00001234", project, policy);
        Assert.False(rejected.IdentityMatched);
        Assert.Equal("SCAN_OTHER_MODEL", rejected.Code);
        Assert.Equal("00001234", rejected.MappedFields![
            AutoTest.Specialists.Scan.ScanIdentityPolicy.SerialTarget]);
    }

}
