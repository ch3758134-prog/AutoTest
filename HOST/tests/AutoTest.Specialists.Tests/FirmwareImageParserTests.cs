using AutoTest.Specialists.Flash;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class FirmwareImageParserTests
{
    private static string HexRecord(byte type,ushort address,params byte[] data)
    {
        var parts=new byte[]{(byte)data.Length,(byte)(address>>8),(byte)address,type}.Concat(data).ToArray();
        byte sum=(byte)unchecked(0-parts.Sum(x=>x));
        return ":"+Convert.ToHexString(parts)+sum.ToString("X2");
    }
    private static string S1(ushort address,params byte[] data)
    {
        byte count=(byte)(data.Length+3);
        var all=new byte[]{count,(byte)(address>>8),(byte)address}.Concat(data).ToArray();
        byte check=(byte)~(byte)all.Sum(x=>x);
        return "S1"+Convert.ToHexString(all)+check.ToString("X2");
    }
    [Fact]
    public void Verify_E4_T01_FormatsAndChecksums()
    {
        var hex=HexRecord(4,0,0,1)+"\n"+HexRecord(0,0x1000,1,2,3,4)+"\n"+HexRecord(1,0);
        var image=FirmwareImageParser.ParseHex(hex);
        Assert.Equal(0x11000u,image.Segments[0].Address); Assert.Equal(4L,image.ByteCount);
        var s19=S1(0x1000,1,2,3,4)+"\nS9030000FC";
        var parsed=FirmwareImageParser.ParseS19(s19);
        Assert.Equal(0x1000u,parsed.Segments[0].Address);
        Assert.Equal(new byte[]{1,2,3,4},parsed.Segments[0].Data.ToArray());
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(hex.Replace("F9","F8")));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseS19(s19.Replace("DE","DF")));
    }
    [Fact]
    public void Verify_E4_T02_OverlapEmptyAndOverflow()
    {
        var duplicate=HexRecord(0,0x1000,1,2)+"\n"+HexRecord(0,0x1001,3,4)+"\n"+HexRecord(1,0);
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(duplicate));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(HexRecord(1,0)));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseBin([1,2,3,4],uint.MaxValue-1));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseS19("S9030000FC"));
    }
}
