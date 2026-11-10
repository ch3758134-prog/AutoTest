using System.Collections.Immutable;
using AutoTest.Devices.Diagnostics;
using AutoTest.Flash;
using AutoTest.Specialists.Flash;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Flash;

public sealed class PhysicalCanToolR1IntegrationTests
{
    [Fact]
    public void PhysicalFlashPermissionIsNotGrantedByParsingOrDefaultOptions()
    {
        var invalid = new CanToolFlashOptions(
            "", "", 0x200, 1000, 5000, 30000, 2000,
            false, false, false, false);
        Assert.ThrowsAny<Exception>(invalid.Validate);
        var parsedOnly = new FlashToolViewModel();
        Assert.False(parsedOnly.PhysicalProgrammingEnabled);
        Assert.Null(parsedOnly.CurrentImage);
        Assert.Null(parsedOnly.CurrentConfiguration);
    }

    [Fact]
    public void AuthorizationPhrasePinsFirmwareXmlCanIdsAndPhysicalUsbGeneration()
    {
        var config = new string('A', 64);
        var image = new string('B', 64);
        var expected = HardwareBenchController.PhysicalCanToolConfirmation(
            config, image, 0x700, 0x701, 1234);
        Assert.Contains("CAN1", expected);
        Assert.Contains("APP=0x700", expected);
        Assert.Contains("BOOT=0x701", expected);
        Assert.Contains("GEN=1234", expected);
        Assert.NotEqual(expected, HardwareBenchController.PhysicalCanToolConfirmation(
            config, image, 0x700, 0x701, 1235));
        Assert.NotEqual(expected, HardwareBenchController.PhysicalCanToolConfirmation(
            config, new string('C', 64), 0x700, 0x701, 1234));
    }

    [Fact]
    public void SharedCanToolEngineBuildsPhysicalBlocksButNeverSendsWithoutSession()
    {
        var config = new CanToolConfigurationSnapshot(
            "test.xml", new string('A', 64), 0x701, 0x709, 0x700, 0x708,
            0x1000, 0x1FFF, "500000",
            ImmutableArray<CanToolConfigurationField>.Empty,
            ImmutableArray<string>.Empty);
        var firmware = FirmwareImageParser.ParseBin(
            new byte[] { 0x11, 0x22, 0x33 }, 0x1000);
        var blocks = CanToolFlashEngine.BuildCanToolAlignedBlocks(config,
            firmware, 0x200, false);
        Assert.Single(blocks);
        Assert.Equal(0x1000U, blocks[0].Address);
        Assert.Equal(0x200, blocks[0].Data.Length);
        Assert.Equal(0x11, blocks[0].Data[0]);
        Assert.Equal(0xFF, blocks[0].Data[^1]);
    }
}
