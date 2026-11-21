using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class Ut1BarcodeCodecTests
{
    [Theory]
    [InlineData("UT1-EPBA-H02-000000123456-4372", "EPBA", "H02", "000000123456")]
    [InlineData("UT1-EPBB-H01-000000123457-DE82", "EPBB", "H01", "000000123457")]
    [InlineData("UT1-EPBA-H03-000000123458-D9DD", "EPBA", "H03", "000000123458")]
    public void PublishedResearchChecksumVectorsParseWithoutChangingSerial(
        string data, string expectedProduct, string expectedHardware, string expectedSerial)
    {
        Assert.True(Ut1BarcodeCodec.TryParse(data, out var parsed, out var code));
        Assert.Equal("BARCODE_UT1_OK", code);
        Assert.Equal(expectedProduct, parsed!.ProductCode);
        Assert.Equal(expectedHardware, parsed.HardwareCode);
        Assert.Equal(expectedSerial, parsed.Serial);
    }

    [Theory]
    [InlineData("UT1-EPBA-H02-000000123456-4373", "BARCODE_CRC_MISMATCH")]
    [InlineData("UT1-EPBA-H02-000000123456-4372 ", "BARCODE_FORMAT")]
    [InlineData("UT1-EPBA-H02-000000123456-437a", "BARCODE_FORMAT")]
    [InlineData("UT1-EPBA-H02-000000123456", "BARCODE_FORMAT")]
    public void InvalidInputNeverFallsBackToALegacyProductMatch(string text, string expected)
    {
        Assert.False(Ut1BarcodeCodec.TryParse(text, out var parsed, out var code));
        Assert.Null(parsed);
        Assert.Equal(expected, code);
    }
}
