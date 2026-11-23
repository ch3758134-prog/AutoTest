using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class ScanIdentityPolicyTests
{
    [Fact]
    public void DelimitedModelPreservesLeadingZerosAndRejectsAmbiguousInput()
    {
        var policy = new ScanIdentityPolicy(1, ScanIdentityPolicy.ModelSerial,
            "-", null, 8, 20);
        var ok = policy.Decode("HYDEPB-000000123456");
        Assert.True(ok.Parsed);
        Assert.Equal("HYDEPB", ok.ModelCode);
        Assert.Equal("000000123456", ok.Serial);
        Assert.Equal("SCAN_MODEL_SERIAL_FORMAT", policy.Decode("000000123456").Code);
        Assert.Equal("SCAN_PAYLOAD_INVALID", policy.Decode(" HYDEPB-000000123456").Code);
        Assert.Equal("SCAN_MODEL_INVALID", policy.Decode("hydepb-000000123456").Code);
    }

    [Fact]
    public void SerialOnlyRequiresExplicitModel_AndHasNoImplicitFallback()
    {
        var unbound = new ScanIdentityPolicy(1, ScanIdentityPolicy.SerialOnly,
            "-", null, 8, 20);
        // The scanner does not maintain its own ECU-model registration. The
        // explicit current project supplies the model at the product boundary.
        unbound.Validate();
        Assert.Equal("SCAN_PROJECT_MODEL_REQUIRED",
            unbound.Decode("000000123456").Code);
        var bound = unbound with { FixedModelCode = "HYDEPB" };
        var result = bound.Decode("000000123456");
        Assert.True(result.Parsed);
        Assert.Equal("HYDEPB", result.ModelCode);
        Assert.Equal("000000123456", result.Serial);
        Assert.Equal("SCAN_SERIAL_INVALID", bound.Decode("00000001;DROP").Code);
    }

    [Fact]
    public void Ut1RequiresValidCrc_AndCannotFallBackToRawSerial()
    {
        var prefix = "UT1-HYDEPB-H02-000000123456";
        var label = prefix + "-" + Ut1BarcodeCodec.ComputeChecksum(prefix).ToString("X4");
        var parsed = ScanIdentityPolicy.Default.Decode(label);
        Assert.True(parsed.Parsed);
        Assert.Equal("000000123456", parsed.Serial);
        Assert.Equal("H02", parsed.HardwareCode);
        var invalidChecksum = Ut1BarcodeCodec.ComputeChecksum(prefix) == 0
            ? "FFFF" : "0000";
        Assert.Equal("BARCODE_CRC_MISMATCH",
            ScanIdentityPolicy.Default.Decode(prefix + "-" + invalidChecksum).Code);
        Assert.False(ScanIdentityPolicy.Default.Decode("000000123456").Parsed);
    }
    [Fact]
    public void MappedDelimitedBarcodeBindsIdentityAndTraceFieldsWithoutRewritingValues()
    {
        var policy = new ScanIdentityPolicy(1, ScanIdentityPolicy.Mapped, "-",
            null, 1, 64, ScanIdentityPolicy.Delimited,
            [
                new(ScanIdentityPolicy.ModelTarget, 1),
                new(ScanIdentityPolicy.HardwareTarget, 2),
                new(ScanIdentityPolicy.SerialTarget, 3),
                new("BatchCode", 4)
            ]);
        var result = policy.Decode("HYDEPB-EP03-00001234-LOT05");
        Assert.True(result.Parsed);
        Assert.Equal("HYDEPB", result.ModelCode);
        Assert.Equal("00001234", result.Serial);
        Assert.Equal("EP03", result.HardwareCode);
        Assert.Equal("LOT05", result.Fields!["BatchCode"]);
        Assert.Equal("SCAN_FIELD_MISSING",
            policy.Decode("HYDEPB-EP03-00001234").Code);
        Assert.Equal("SCAN_FIELD_EMPTY",
            policy.Decode("HYDEPB--00001234-LOT05").Code);
        Assert.False(policy.Decode("HYDEPB-EP03-00001234-LOT05").Code
            .Contains("AUTHORIZED", StringComparison.Ordinal));
    }

    [Fact]
    public void MappedFixedPositionsRemainOneBasedAndRejectTruncatedFrames()
    {
        var policy = new ScanIdentityPolicy(1, ScanIdentityPolicy.Mapped, "-",
            "HYDEPB", 1, 64, ScanIdentityPolicy.Fixed,
            [
                new(ScanIdentityPolicy.SerialTarget, 1, 1, 8),
                new(ScanIdentityPolicy.HardwareTarget, 1, 9, 4)
            ]);
        var ok = policy.Decode("00001234EP03");
        Assert.True(ok.Parsed);
        Assert.Equal("00001234", ok.Serial);
        Assert.Equal("HYDEPB", ok.ModelCode);
        Assert.Equal("EP03", ok.HardwareCode);
        Assert.Equal("SCAN_FIELD_MISSING", policy.Decode("00001234").Code);
    }

    [Fact]
    public void MappedPolicyRejectsDuplicateSystemTargetsAndMissingDutSerial()
    {
        var duplicate = new ScanIdentityPolicy(1, ScanIdentityPolicy.Mapped,
            "-", null, 1, 64, ScanIdentityPolicy.Delimited,
            [
                new(ScanIdentityPolicy.SerialTarget, 1),
                new(ScanIdentityPolicy.SerialTarget, 2)
            ]);
        Assert.Equal("SCAN_POLICY_INVALID", duplicate.Decode("01-02").Code);
        var withoutSerial = duplicate with
        {
            FieldMappings = [new ScanFieldMapping(ScanIdentityPolicy.ModelTarget, 1)]
        };
        Assert.Equal("SCAN_POLICY_INVALID", withoutSerial.Decode("HYDEPB").Code);
    }

}
