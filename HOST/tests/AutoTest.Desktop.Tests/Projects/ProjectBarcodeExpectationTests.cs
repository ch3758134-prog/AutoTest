using System.Collections.Immutable;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProjectBarcodeExpectationTests
{
    private static ProjectBarcodeExpectation Rule() => new("|",
        [new("ModelCode", "Match", "HYDEPB"), new("HardwareCode", "Match", "EP03"),
         new("DutSerial", "Identity"), new("BatchCode", "Record"),
         new("ProductionDate", "Record"), new("CustomerNote", "Record")]);

    private static ProjectDefinition Project() => (ProjectDefinition.Create("工程显示名") with
    { EcuModelCode = "HYDEPB", BarcodeExpectation = Rule() }).Validate();

    [Fact]
    public void OneBarcodeMatchesAllExpectedFieldsAndPreservesLeadingZeroes()
    {
        var result = ProductionBarcodePreflight.InspectExpected(
            "HYDEPB|EP03|00001234|LOT01|20261123|A01", Project());
        Assert.True(result.IdentityMatched);
        Assert.Equal("00001234", result.DutSerial);
        Assert.Equal("EP03", result.HardwareCode);
        Assert.Equal("LOT01", result.MappedFields!["BatchCode"]);
        Assert.Equal("20261123", result.MappedFields!["ProductionDate"]);
        Assert.Equal("A01", result.MappedFields!["CustomerNote"]);
    }

    [Theory]
    [InlineData("HYDEPB|EP04|00001234|LOT01|20261123|A01")]
    [InlineData("OTHER|EP03|00001234|LOT01|20261123|A01")]
    [InlineData("HYDEPB|EP03||LOT01|20261123|A01")]
    [InlineData("HYDEPB|EP03|00001234|LOT01|20261123")]
    [InlineData("HYDEPB|EP03|00001234|LOT01|20261123|A01|EXTRA")]
    public void WrongHardwareModelMissingSerialOrWrongFieldCountRejects(string raw)
    {
        var result = ProductionBarcodePreflight.InspectExpected(raw, Project());
        Assert.False(result.IdentityMatched);
        Assert.Equal("SCAN_EXPECTATION_MISMATCH", result.Code);
    }

    [Fact]
    public void MissingProjectExpectationCannotFallbackToLegacySerialOrStationRule()
    {
        var p = ProjectDefinition.Create("旧工程") with { EcuModelCode = "HYDEPB" };
        Assert.Equal("SCAN_EXPECTATION_MISSING",
            ProductionBarcodePreflight.InspectExpected("00001234", p).Code);
    }

    [Fact]
    public void CustomFieldMayBeAnExactMatchRatherThanJustTraceData()
    {
        var rule = Rule();
        var exact = rule with { Fields = rule.Fields.SetItem(3, new("BatchCode", "Match", "LOT01")) };
        Assert.True(exact.Evaluate("HYDEPB|EP03|00001234|LOT01|20261123|A01", "HYDEPB").Accepted);
        Assert.False(exact.Evaluate("HYDEPB|EP03|00001234|LOT02|20261123|A01", "HYDEPB").Accepted);
    }

    [Fact]
    public async Task ManifestRoundTripKeepsExpectationInsideProjectAndRejectsDifferentHardware()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "project.autotest-project.json");
            await ProjectRepository.SaveDefinitionAsync(Project(), path);
            var reopened = await ProjectRepository.OpenDefinitionAsync(path);
            Assert.NotNull(reopened.BarcodeExpectation);
            Assert.Equal("|", reopened.BarcodeExpectation!.Separator);
            Assert.Equal(6, reopened.BarcodeExpectation.Fields.Length);
            Assert.False(ProductionBarcodePreflight.InspectExpected(
                "HYDEPB|EP04|00001234|LOT01|20261123|A01", reopened).IdentityMatched);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidOrMissingMandatoryRulesAreRejectedBeforeSave()
    {
        var rule = Rule();
        Assert.Throws<ProjectDataException>(() =>
            (rule with { Fields = rule.Fields.RemoveAt(1) }).Validate("HYDEPB"));
        Assert.Throws<ProjectDataException>(() => rule.Validate("OTHER"));
        Assert.Throws<ProjectDataException>(() =>
            (rule with { Fields = rule.Fields.SetItem(2, new("DutSerial", "Record")) }).Validate("HYDEPB"));
        Assert.Throws<ProjectDataException>(() =>
            (rule with { Fields = rule.Fields.Add(new("BatchCode", "Record")) }).Validate("HYDEPB"));
    }
}
