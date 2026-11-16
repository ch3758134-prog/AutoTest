using System.Buffers.Binary;
using System.IO;
using AutoTest.FlashLab.Programming;
using Xunit;

namespace AutoTest.FlashLab.Tests;

// Pure CanTool protocol byte layout and rejection-path checks. No fake ECU/ACK.
public sealed class CanToolF2ProtocolTests
{
    [Fact]
    public void EraseDownloadVerifyAndResetMatchOriginalCanToolLayout()
    {
        Assert.Equal("3101FF001000800000068000",
            Convert.ToHexString(CanToolProgrammingProtocol.Erase(0x10008000,0x68000)));
        Assert.Equal("3400441000800000000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Download(0x10008000,0x200)));
        Assert.Equal("3101FF011000800000000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Verify(0x10008000,0x200)));
        Assert.Equal("37",Convert.ToHexString(CanToolProgrammingProtocol.RequestDownloadExit()));
        Assert.Equal("1101",Convert.ToHexString(CanToolProgrammingProtocol.UdsReset()));
    }
    [Fact]
    public void DownloadMaximumUsesHighNibbleNotOriginalCanToolBug()
    {
        Assert.Equal(0x102u,CanToolProgrammingProtocol.ParseDownloadMaximum(
            Convert.FromHexString("74200102")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("74010102")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("741000")));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.ParseDownloadMaximum(Convert.FromHexString("74200002")));
    }
    [Fact]
    public void NoSuccessWhenFf01ChecksumIsMissingOrMismatched()
    {
        var bytes=new byte[]{0x01,0x02,0x03};
        ushort checksum=CanToolProgrammingProtocol.ComplementOfSum(bytes);
        Assert.Equal((ushort)0xFFF9,checksum);
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF01"),checksum));
        Assert.Throws<InvalidDataException>(()=>
            CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF010000"),checksum));
        CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF01FFF9"),checksum);
    }
    [Fact]
    public void TransferDataCarriesExactSequenceAndImageBytes()
    {
        Assert.Equal("3601010203",
            Convert.ToHexString(CanToolProgrammingProtocol.Transfer(1,[1,2,3])));
        Assert.Equal("3600AA",
            Convert.ToHexString(CanToolProgrammingProtocol.Transfer(0,[0xAA])));
        Assert.Throws<ArgumentException>(()=>
            CanToolProgrammingProtocol.Transfer(1,Array.Empty<byte>()));
    }
}
