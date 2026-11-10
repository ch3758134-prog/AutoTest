using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Uds;
using AutoTest.Contracts;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class PhysicalProgrammingBoundariesTests
{
    [Fact]
    public void ReadDid_MismatchedIdentifierCannotBeAccepted()
    {
        Assert.Throws<IsoTpProtocolException>(() =>
            UdsReadOnlyResponseValidator.Validate([0x22,0xF1,0x90],[0x62,0xF1,0x91,0x11]));
        UdsReadOnlyResponseValidator.Validate([0x22,0xF1,0x90],
            [0x62,0xF1,0x90,0x31,0x32]);
    }
    [Fact]
    public void ReportDtcRejectsWrongSubFunctionAndTruncatedRecords()
    {
        Assert.Throws<IsoTpProtocolException>(() =>
            UdsReadOnlyResponseValidator.Validate([0x19,0x02,0xFF],[0x59,0x06,0xFF]));
        Assert.Throws<IsoTpProtocolException>(() =>
            UdsReadOnlyResponseValidator.Validate([0x19,0x02,0xFF],[0x59,0x02,0xFF,0x01]));
        UdsReadOnlyResponseValidator.Validate([0x19,0x02,0xFF],[0x59,0x02,0xFF,0x01,0x02,0x03,0x04]);
    }
    [Theory]
    [InlineData(new byte[]{0x74,0x20,0x02,0x00},512u)]
    [InlineData(new byte[]{0x74,0x10,0x08},8u)]
    [InlineData(new byte[]{0x74,0x30,0x00,0x01,0x02},258u)]
    public void DownloadLengthUsesHighNibbleAndCountsWholePdu(byte[] answer,uint expected) =>
        Assert.Equal(expected,UdsDownloadResponseParser.MaxTransferRequestBytes(answer));
    [Theory]
    [InlineData(new byte[]{0x74,0x02,0x00,0x20})]
    [InlineData(new byte[]{0x74,0x20,0x00})]
    [InlineData(new byte[]{0x74,0x10,0x02})]
    [InlineData(new byte[]{0x74,0x10,0x00})]
    public void InvalidDownloadResponseFailsClosed(byte[] bytes) =>
        Assert.Throws<FormatException>(() => UdsDownloadResponseParser.MaxTransferRequestBytes(bytes));
    [Fact]
    public void UnprovenFlashNeverEnablesWriteEvenIfControllerIsConnected()
    {
        var input = new FlashFieldEvidence(true,true,4,false,false,false,false,false,false,false,false);
        var result=FlashFieldPreflightEvaluator.Evaluate(null,null,input);
        Assert.False(result.PhysicalProgrammingAllowed);
        Assert.False(result.ReadOnlyCommunicationProven);
        Assert.Contains(result.Items, x=>x.Category=="PHYSICAL_WRITE" && x.State=="BLOCKED");
        Assert.Contains(result.Items, x=>x.Category=="FLASH_MAP" && x.State=="BLOCKED");
    }
}
