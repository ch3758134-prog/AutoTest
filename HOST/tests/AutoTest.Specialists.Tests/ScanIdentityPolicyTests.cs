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
        Assert.Equal("SCAN_POLICY_INVALID", unbound.Decode("000000123456").Code);
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
}
