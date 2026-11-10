using AutoTest.Flash;
using System.IO;
using System.Text;
using AutoTest.Devices.Diagnostics;
using AutoTest.FlashDiag;
using AutoTest.Specialists.Flash;
using Xunit;

namespace AutoTest.FlashDiag.Tests;

public sealed class CanToolFlashLogicTests
{
    private static CanToolConfigurationSnapshot Config(uint start = 0x1000, uint end = 0x1FFF)
    {
        var xml = $"""
            <Configuration>
              <BootloadPhyID>0x700</BootloadPhyID>
              <BootRespond>0x708</BootRespond>
              <APPPhyID>0x710</APPPhyID>
              <APPRespond>0x718</APPRespond>
              <FlashStartAddr>0x{start:X}</FlashStartAddr>
              <FlashEndAddr>0x{end:X}</FlashEndAddr>
              <BaudRate>500K</BaudRate>
            </Configuration>
            """;
        return CanToolConfigurationImporter.Parse(Encoding.UTF8.GetBytes(xml));
    }

    [Fact]
    public void AlignedCanToolBlockPreservesFirmwareAndFillsGapsWithErasedBytes()
    {
        var image = FirmwareImageParser.ParseBin(new byte[]{0xAB, 0xCD, 0xEF}, 0x1003);
        var blocks = CanToolFlashEngine.BuildCanToolAlignedBlocks(Config(), image, 32, false);
        var block = Assert.Single(blocks);
        Assert.Equal(0x1000u, block.Address);
        Assert.Equal(32, block.Data.Length);
        Assert.Equal(0xFF, block.Data[0]);
        Assert.Equal(0xAB, block.Data[3]);
        Assert.Equal(0xCD, block.Data[4]);
        Assert.Equal(0xEF, block.Data[5]);
        Assert.Equal(0xFF, block.Data[31]);
    }

    [Fact]
    public void OptionalTerminalCanToolSignatureIsNeverWrittenByDefault()
    {
        var image = FirmwareImageParser.ParseBin(new byte[]{1,2,3}, 0x1000);
        var normal = CanToolFlashEngine.BuildCanToolAlignedBlocks(Config(), image, 32, false);
        Assert.Single(normal);
        var withSignature = CanToolFlashEngine.BuildCanToolAlignedBlocks(Config(), image, 32, true);
        Assert.Equal(2, withSignature.Length);
        var last = withSignature[1];
        Assert.Equal(0x1FE0u, last.Address);
        Assert.Equal(new byte[]{0x12,0x34,0x56,0x78},
            last.Data.AsSpan(28, 4).ToArray());
    }

    [Fact]
    public void AlignedFlashBlockOutsideConfiguredRegionIsRejectedBeforeErase()
    {
        var image = FirmwareImageParser.ParseBin(new byte[]{0x20}, 0x1003);
        Assert.Throws<InvalidDataException>(() =>
            CanToolFlashEngine.BuildCanToolAlignedBlocks(Config(0x1003, 0x1FFF),
                image, 32, false));
    }

    [Fact]
    public void CanToolChecksumUsesTheReferenceSixteenBitOnesComplement()
    {
        Assert.Equal((ushort)0xFFF9,
            CanToolFlashEngine.CanToolChecksum(new byte[]{1,2,3}));
        Assert.Equal((ushort)0xFFFF,
            CanToolFlashEngine.CanToolChecksum(Array.Empty<byte>()));
    }

    [Fact]
    public void ProbeAuthorizationIsBoundToRealSessionAndCannotBeEmpty()
    {
        var xmlSha = new string('A',64);
        var actual = HardwareBenchController.PhysicalCanToolProbeConfirmation(
            xmlSha, 0x710, 0x700, 11);
        Assert.Contains("APP=0x710", actual);
        Assert.Contains("GEN=11", actual);
        Assert.Throws<ArgumentException>(() =>
            HardwareBenchController.PhysicalCanToolProbeConfirmation(
                xmlSha, 0x710, 0x700, 0));
    }
}
