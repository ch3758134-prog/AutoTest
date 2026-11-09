using System.IO;
using AutoTest.Desktop.Tools.Flash.Physical;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Flash.Physical;

public sealed class CanToolProgrammingProtocolTests
{
    [Fact]
    public void EraseAndDownloadRespectCanToolPduLayout()
    {
        Assert.Equal("3101FF001234567800000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Erase(0x12345678,0x200)));
        Assert.Equal("3400441234567800000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Download(0x12345678,0x200)));
        Assert.Equal("3101FF011234567800000200",
            Convert.ToHexString(CanToolProgrammingProtocol.Verify(0x12345678,0x200)));
    }

    [Fact]
    public void ParsesIso14229ResponseAndSplitsTransferData()
    {
        Assert.Equal(0x102u,CanToolProgrammingProtocol.ParseDownloadMaximum(
            Convert.FromHexString("74200102")));
        var blocks=CanToolProgrammingProtocol.TransferBlocks(new byte[] {1,2,3,4,5},4,4).ToArray();
        Assert.Equal(3,blocks.Length);
        Assert.Equal("36010102",Convert.ToHexString(blocks[0]));
        Assert.Equal("36020304",Convert.ToHexString(blocks[1]));
        Assert.Equal("360305",Convert.ToHexString(blocks[2]));
    }

    [Fact]
    public void VerificationDoesNotAcceptAnAckWithoutChecksum()
    {
        Assert.Throws<InvalidDataException>(() =>
            CanToolProgrammingProtocol.CheckVerify(Convert.FromHexString("7101FF01"),0));
    }

    [Fact]
    public void ConfigRejectsMissingIds()
    {
        string file=Path.GetTempFileName();
        try
        {
            File.WriteAllText(file,"<Configuration><BootloadPhyID>0</BootloadPhyID>"+
                "<BootRespond>0</BootRespond><APPPhyID>0</APPPhyID><APPRespond>0</APPRespond>"+
                "<FlashStartAddr>1000</FlashStartAddr><FlashEndAddr>2000</FlashEndAddr></Configuration>");
            Assert.Throws<FormatException>(()=>CanToolFlashProfile.Load(file));
        }
        finally { File.Delete(file); }
    }
}
