using System.IO;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>
/// V2 station identity only. All inputs here are pure data; no test mocks,
/// fake accepted production records, real device commands, or TEST execution.
/// </summary>
public sealed class ProductionIdentityConfigTests
{
    private static ProductionPublishedProduct Pin(string productKey = "", string hardware = "") =>
        new(productKey, hardware, @"C:\offline-tests\project.autotest-project.json",
            Guid.NewGuid(), new string('A', 64), Guid.NewGuid(),
            new string('B', 64), new string('C', 64), new string('D', 64),
            new string('E', 64), 500, new string('F', 64), ["驻车", "释放"]);

    private static ProductionProductBinding Bind(ProductionPublishedProduct pin) =>
        new(pin.ProductKey, pin.HardwareCode, pin.ManifestPath, pin.ProjectId);

    private static StationProductionConfiguration Fixed(ProductionPublishedProduct pin) =>
        StationProductionConfiguration.New() with
        {
            FixedProjectId = pin.ProjectId, Products = [Bind(pin)]
        };

    private static PublishedStationProductionConfiguration Published(
        StationProductionConfiguration config, params ProductionPublishedProduct[] pins) =>
        new(StationProductionConfiguration.CurrentSchema,
            StationProductionConfigurationStore.ConfigurationSha(config),
            Guid.NewGuid(), DateTimeOffset.UtcNow, config, pins);

    [Fact]
    public void FixedWorkcellNeedsNoModelOrBarcodeProductCode()
    {
        var pin = Pin();
        var config = Fixed(pin);
        Assert.Empty(StationProductionConfigurationStore.InspectRules(config));
        var result = ProductionIdentityResolver.Resolve(Published(config, pin), "SN0001234");
        Assert.True(result.Accepted);
        Assert.Equal("SN0001234", result.Serial);
        Assert.Equal(pin.ProjectId, result.Project!.ProjectId);
        Assert.Equal("", result.ProductKey);
        Assert.Equal("FIXED_PROJECT_ID", result.ProjectBasis);
    }

    [Fact]
    public void FixedWorkcellRejectsMissingOrAmbiguousProjectBinding()
    {
        var a = Pin();
        Assert.Throws<InvalidDataException>(() =>
            (Fixed(a) with { FixedProjectId = Guid.Empty }).Validate());
        Assert.Throws<InvalidDataException>(() =>
            (Fixed(a) with { Products = [Bind(a), Bind(Pin())] }).Validate());
        Assert.Throws<InvalidDataException>(() =>
            (Fixed(a) with { Products = [Bind(Pin())] }).Validate());
    }

    [Fact]
    public void SchemaOneIsRejectedRatherThanMigrated()
    {
        Assert.Throws<InvalidDataException>(() =>
            (StationProductionConfiguration.New() with { SchemaVersion = 1 })
                .ValidateDraft());
    }

    [Fact]
    public void MixedLineBarcodeProductSelectsOnlyUniquePublishedProject()
    {
        var p1 = Pin("P01");
        var p2 = Pin("P02");
        var rule = ProductionBarcodeRule.NewSerial() with
        {
            Format = ProductionBarcodeRule.Delimited,
            AllowedCharacters = @"[A-Za-z0-9_.|-]+",
            Separator = "|",
            SerialField = 2,
            ModelField = 1,
            Samples =
            [
                new("P01|UNIT0001", true, "UNIT0001", "P01"),
                new("UNKNOWN", false)
            ]
        };
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.BarcodeModel,
            Rules = [rule],
            Products = [Bind(p1), Bind(p2)]
        };
        Assert.Empty(StationProductionConfigurationStore.InspectRules(config));
        var one = ProductionIdentityResolver.Resolve(Published(config, p1, p2), "P01|UNIT0001");
        Assert.True(one.Accepted);
        Assert.Equal(p1.ProjectId, one.Project!.ProjectId);
        Assert.Equal("UNIT0001", one.Serial);
        Assert.Equal("P01", one.ProductKey);
        var other = ProductionIdentityResolver.Resolve(Published(config, p1, p2), "P99|UNIT0001");
        Assert.False(other.Accepted);
        Assert.Equal("PROJECT_MAPPING_NOT_UNIQUE", other.Code);
    }

    [Fact]
    public void RegistryWithoutTrustedOwnershipCannotGuessProject()
    {
        var p1 = Pin("P01");
        var p2 = Pin("P02");
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.RegistryModel,
            Products = [Bind(p1), Bind(p2)],
            IdentityRows = [new("UNIT0001", "P02", "")]
        };
        var found = ProductionIdentityResolver.Resolve(
            Published(config, p1, p2), "UNIT0001");
        Assert.True(found.Accepted);
        Assert.Equal(p2.ProjectId, found.Project!.ProjectId);
        var unknown = ProductionIdentityResolver.Resolve(
            Published(config, p1, p2), "UNIT0002");
        Assert.Equal("IDENTITY_REGISTRY_NOT_FOUND", unknown.Code);
    }

    [Fact]
    public void DifferentRuleInterpretationsNeverFallBackToFirstMatch()
    {
        var pin = Pin("P01");
        var baseRule = ProductionBarcodeRule.NewSerial() with
        {
            Format = ProductionBarcodeRule.Delimited,
            AllowedCharacters = @"[A-Za-z0-9_.|-]+",
            Separator = "|", SerialField = 2, ModelField = 1
        };
        var conflicting = baseRule with
        {
            RuleId = Guid.NewGuid().ToString("N"),
            SerialField = 1
        };
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.BarcodeModel,
            Products = [Bind(pin)], Rules = [baseRule, conflicting]
        };
        var result = ProductionIdentityResolver.Resolve(Published(config, pin), "P01|UNIT0001");
        Assert.False(result.Accepted);
        Assert.Equal("BARCODE_RULE_CONFLICT", result.Code);
    }

    [Fact]
    public void RetestIdentityComesFromSameRawSerialAndDoesNotCreateANewDut()
    {
        var pin = Pin();
        var config = Fixed(pin);
        var release = Published(config, pin);
        var a = ProductionIdentityResolver.Resolve(release, "ABC123");
        var b = ProductionIdentityResolver.Resolve(release, "ABC123");
        Assert.Equal(a.Serial, b.Serial);
        Assert.Equal(a.Project!.ProjectId, b.Project!.ProjectId);
    }
}
