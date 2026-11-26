using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>Pure deterministic parsing/identity contracts. These tests never
/// connect to a station, claim published ECU resources or issue CAN/FLASH.</summary>
public sealed class ProductionIdentityConfigTests
{
    private static ProductionPublishedProduct Pin(string model, string hardware = "") =>
        new(model, hardware, @"C:\offline-tests\project.autotest-project.json",
            Guid.NewGuid(), new string('A', 64), Guid.NewGuid(),
            new string('B', 64), new string('C', 64), new string('D', 64),
            new string('E', 64), 500, new string('F', 64), ["准备", "驻车", "释放"]);

    private static PublishedStationProductionConfiguration Fixture(
        StationProductionConfiguration config, params ProductionPublishedProduct[] pins) =>
        new(1, StationProductionConfigurationStore.ConfigurationSha(config),
            Guid.NewGuid(), DateTimeOffset.UtcNow, config, pins);

    private static ProductionBarcodeRule Serial(string name) =>
        ProductionBarcodeRule.NewSerial() with { Name = name };

    [Fact]
    public void NewStationRuleDoesNotPretendToHaveRealScannerSamples()
    {
        var blank = ProductionBarcodeRule.NewSerial();
        Assert.Empty(blank.Samples);
        var pin = Pin("HYDEPB");
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = pin.ModelCode,
            Products = [new(pin.ModelCode, "", pin.ManifestPath)]
        };
        Assert.Contains(StationProductionConfigurationStore.InspectRules(config),
            issue => issue.Contains("正例和反例"));
    }

    [Fact]
    public void LegacyArtificialStarterSamplesCannotReleaseStation()
    {
        var pin = Pin("HYDEPB");
        var legacy = ProductionBarcodeRule.NewSerial() with
        {
            Samples = [new ProductionBarcodeSample("SN001234", true, "SN001234"),
                new ProductionBarcodeSample("SN|BAD", false)]
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = pin.ModelCode,
            Products = [new(pin.ModelCode, "", pin.ManifestPath)],
            Rules = [legacy]
        };
        Assert.Contains(StationProductionConfigurationStore.InspectRules(config),
            issue => issue.Contains("虚构"));
    }

    [Fact]
    public void ActualSerialSampleAndExplicitGeneratedRejectPassStaticParser()
    {
        var pin = Pin("HYDEPB");
        var rule = ProductionBarcodeRule.NewSerial() with
        {
            Samples = [new ProductionBarcodeSample("EPB000126", true, "EPB000126"),
                new ProductionBarcodeSample("EPB000126|", false)]
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = pin.ModelCode,
            Products = [new(pin.ModelCode, "", pin.ManifestPath)],
            Rules = [rule]
        };
        Assert.Empty(StationProductionConfigurationStore.InspectRules(config));
        Assert.True(rule.Parse("EPB000126").Accepted);
        Assert.False(rule.Parse("EPB000126|").Accepted);
    }

    [Fact]
    public void FixedStationSerialOnlyDoesNotRequireModelOrHardwareFields()
    {
        var pin = Pin("HYDEPB");
        var rule = Serial("序列编号");
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = pin.ModelCode,
            Rules = [rule],
            Products = [new(pin.ModelCode, "", pin.ManifestPath)]
        };
        config.Validate();
        var result = ProductionIdentityResolver.Resolve(Fixture(config, pin), "00000012");
        Assert.True(result.Accepted);
        Assert.Equal("00000012", result.Serial);
        Assert.Equal("HYDEPB", result.Model);
        Assert.Equal("FIXED_STATION_MODEL", result.ModelBasis);
        Assert.Equal(pin.ProjectId, result.Project!.ProjectId);
    }

    [Fact]
    public void TwoDifferentFormsMayIdentifySameEcuIdentity()
    {
        var pin = Pin("HYDEPB");
        var serial = Serial("独立编号") with { Prefix = "SN" };
        var delimited = Serial("分隔字段") with
        {
            RuleId = Guid.NewGuid().ToString("N"),
            Format = ProductionBarcodeRule.Delimited,
            Prefix = "H|", Separator = "|",
            AllowedCharacters = @"[A-Za-z0-9|]+",
            SerialField = 2, ModelField = 0
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB",
            Rules = [serial, delimited],
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        Assert.True(ProductionIdentityResolver.Resolve(Fixture(config, pin), "SN001").Accepted);
        var answer = ProductionIdentityResolver.Resolve(Fixture(config, pin), "H|SN001");
        Assert.True(answer.Accepted);
        Assert.Equal("SN001", answer.Serial);
    }

    [Fact]
    public void PublishedBarcodeModelResolvesOnlyMatchingVariant()
    {
        var a = Pin("HYDEPB", "HW1");
        var b = Pin("PARK2", "HW2");
        var rule = Serial("字段识别") with
        {
            Format = ProductionBarcodeRule.Delimited,
            Separator = "|",
            SerialField = 3, ModelField = 1, HardwareField = 2,
            AllowedCharacters = @"[A-Za-z0-9|]+"
        };
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.BarcodeModel,
            Rules = [rule],
            Products = [new(a.ModelCode, a.HardwareCode, a.ManifestPath),
                new(b.ModelCode, b.HardwareCode, b.ManifestPath)]
        };
        var result = ProductionIdentityResolver.Resolve(Fixture(config, a, b),
            "PARK2|HW2|SERIAL789");
        Assert.True(result.Accepted);
        Assert.Equal(b.ProjectId, result.Project!.ProjectId);
        Assert.Equal("PUBLISHED_BARCODE_MODEL_FIELD", result.ModelBasis);
    }

    [Fact]
    public void RegistryRequiresExplicitMatchingRowNoSerialGuessing()
    {
        var a = Pin("HYDEPB");
        var b = Pin("PARK2");
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.RegistryModel,
            Products = [new(a.ModelCode, "", a.ManifestPath),
                new(b.ModelCode, "", b.ManifestPath)],
            IdentityRows = [new("UNIT0001", "PARK2", "")]
        };
        Assert.Equal("IDENTITY_REGISTRY_NOT_FOUND",
            ProductionIdentityResolver.Resolve(Fixture(config, a, b),
                "UNIT9999").Code);
        Assert.Equal(b.ProjectId,
            ProductionIdentityResolver.Resolve(Fixture(config, a, b),
                "UNIT0001").Project!.ProjectId);
    }

    [Fact]
    public void ConflictingSimultaneousBarcodeRulesRejectInsteadOfFirstMatch()
    {
        var pin = Pin("HYDEPB");
        var r1 = Serial("all");
        var r2 = Serial("substring") with
        {
            RuleId = Guid.NewGuid().ToString("N"),
            Format = ProductionBarcodeRule.Fixed,
            SerialStart = 2, SerialLength = 5
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB",
            Rules = [r1, r2],
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        Assert.Equal("BARCODE_RULE_CONFLICT",
            ProductionIdentityResolver.Resolve(Fixture(config, pin),
                "ABCDEF").Code);
    }

    [Fact]
    public void DuplicateSerialAndExpiredMappingsCannotBePublished()
    {
        var pin = Pin("HYDEPB");
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.RegistryModel,
            Products = [new("HYDEPB", "", pin.ManifestPath)],
            IdentityRows = [new("ABC", "HYDEPB", ""),
                new("ABC", "HYDEPB", "")]
        };
        Assert.Throws<InvalidDataException>(config.Validate);
        config = config with
        {
            IdentityRows = [new("ABC", "HYDEPB", "",
                DateTimeOffset.UtcNow.AddDays(-1))]
        };
        Assert.Throws<InvalidDataException>(config.Validate);
    }

    [Fact]
    public void NoValidRuleOrUnknownModelNeverSelectsUnrelatedProject()
    {
        var pin = Pin("HYDEPB");
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB",
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        Assert.Equal("NO_BARCODE_RULE_MATCHED",
            ProductionIdentityResolver.Resolve(Fixture(config, pin),
                "WRONG|MODEL").Code);
        config = config with
        {
            Mode = StationProductionConfiguration.BarcodeModel,
            Rules = [Serial("code")],
        };
        Assert.Equal("BARCODE_MODEL_MISSING",
            ProductionIdentityResolver.Resolve(Fixture(config, pin),
                "UNIT1234").Code);
    }

    [Fact]
    public void FixedPositionFormatExtractsOnlyConfiguredSerialRegion()
    {
        var pin = Pin("HYDEPB");
        var rule = Serial("定长编号") with
        {
            Format = ProductionBarcodeRule.Fixed,
            ExactLength = 8,
            SerialStart = 3, SerialLength = 6
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB", Rules = [rule],
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        var decision = ProductionIdentityResolver.Resolve(
            Fixture(config, pin), "AB000123");
        Assert.True(decision.Accepted);
        Assert.Equal("000123", decision.Serial);
        Assert.Equal("NO_BARCODE_RULE_MATCHED",
            ProductionIdentityResolver.Resolve(Fixture(config, pin),
                "AB0001239").Code);
    }

    [Fact]
    public void NamedRegexExtractsModelHardwareBatchAndSerial()
    {
        var pin = Pin("HYDEPB", "HW1");
        var rule = Serial("型号硬件序列号") with
        {
            Format = ProductionBarcodeRule.RegexFormat,
            AllowedCharacters = @"[A-Z0-9|]+",
            RegexExpression = @"(?<model>[A-Z0-9]+)\|(?<hardware>[A-Z0-9]+)\|(?<serial>[A-Z0-9]+)\|(?<batch>[A-Z0-9]+)"
        };
        var config = StationProductionConfiguration.New() with
        {
            Mode = StationProductionConfiguration.BarcodeModel,
            Rules = [rule], Products = [new("HYDEPB", "HW1", pin.ManifestPath)]
        };
        var decision = ProductionIdentityResolver.Resolve(
            Fixture(config, pin), "HYDEPB|HW1|S001|B7");
        Assert.True(decision.Accepted);
        Assert.Equal("S001", decision.Serial);
        Assert.Equal("B7", decision.Batch);
        Assert.Equal("HW1", decision.Hardware);
    }

    [Fact]
    public void SameSerialButDifferentBatchFromOverlappingRulesRejects()
    {
        var pin = Pin("HYDEPB");
        var rule = Serial("带批次") with
        {
            Format = ProductionBarcodeRule.Delimited,
            Separator = "|", SerialField = 1, BatchField = 2,
            AllowedCharacters = @"[A-Za-z0-9|]+"
        };
        var other = rule with
        {
            RuleId = Guid.NewGuid().ToString("N"),
            Name = "读取不同批次",
            BatchField = 3
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB", Rules = [rule, other],
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        Assert.Equal("BARCODE_RULE_CONFLICT",
            ProductionIdentityResolver.Resolve(Fixture(config, pin),
                "SER123|B09|X22").Code);
    }

    [Fact]
    public void FailedRuleSamplesBlockPublicationBeforeAnyProjectInspection()
    {
        var pin = Pin("HYDEPB");
        var rule = Serial("样例") with
        {
            Samples = [new ProductionBarcodeSample("SN001", true, "SN001"),
                new ProductionBarcodeSample("SN001", false)]
        };
        var config = StationProductionConfiguration.New() with
        {
            FixedModel = "HYDEPB", Rules = [rule],
            Products = [new("HYDEPB", "", pin.ManifestPath)]
        };
        Assert.Contains(StationProductionConfigurationStore.InspectRules(config),
            x => x.Contains("样例"));
    }

    [Fact]
    public void StartAndClampFeedbackCannotBeAssertedFromUsbAck()
    {
        var contract = ProductionFixtureConfiguration.Draft;
        Assert.Contains(contract.PhysicalBlockers(),
            x => x.Contains("START"));
        Assert.Equal("MCU:PTB1/J1-B5", contract.Clamp1Output);
        Assert.Equal("MCU:PTC2/J1-B4", contract.Clamp2Output);
    }
}
