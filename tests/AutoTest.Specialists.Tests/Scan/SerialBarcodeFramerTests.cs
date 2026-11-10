using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Specialists.Tests.Scan;

public sealed class SerialBarcodeFramerTests
{
    [Fact]
    public void SplitCrLfAcrossReads_YieldsOneCompleteBarcode()
    {
        var f = new SerialBarcodeFramer(); var now = DateTimeOffset.UtcNow;
        Assert.Empty(f.Accept("ABC"u8,now));
        var frames = f.Accept("123\r"u8,now.AddMilliseconds(25));
        Assert.Single(frames);
        Assert.Equal("ABC123",System.Text.Encoding.ASCII.GetString(frames[0].Data.AsSpan()));
        Assert.Equal("CR",frames[0].Boundary);
        Assert.Empty(f.Accept("\n"u8,now.AddMilliseconds(30)));
    }
    [Fact]
    public void MultipleBarcodesInSingleChunk_AreDistinct()
    {
        var f = new SerialBarcodeFramer();
        var frames = f.Accept("A1\r\nB2\r"u8,DateTimeOffset.UtcNow);
        Assert.Equal(2,frames.Count);
        Assert.Equal("A1",System.Text.Encoding.ASCII.GetString(frames[0].Data.AsSpan()));
        Assert.Equal("B2",System.Text.Encoding.ASCII.GetString(frames[1].Data.AsSpan()));
    }
    [Fact]
    public void NoTerminator_FlushesOnlyAfterQuietPeriod()
    {
        var now=DateTimeOffset.UtcNow; var f=new SerialBarcodeFramer();
        Assert.Empty(f.Accept("AB12"u8,now));
        Assert.Null(f.FlushAfterIdle(now.AddMilliseconds(100),TimeSpan.FromMilliseconds(450)));
        var packet=f.FlushAfterIdle(now.AddMilliseconds(500),TimeSpan.FromMilliseconds(450));
        Assert.NotNull(packet);
        Assert.Equal("IDLE",packet.Boundary);
        Assert.Equal("AB12",System.Text.Encoding.ASCII.GetString(packet.Data.AsSpan()));
    }
    [Fact]
    public void OversizedPacket_IsNeverPublishedAsPartialBarcode()
    {
        var f=new SerialBarcodeFramer();
        Assert.Empty(f.Accept(System.Text.Encoding.ASCII.GetBytes(new string('A',513)),DateTimeOffset.UtcNow));
        Assert.Empty(f.Accept("\r"u8,DateTimeOffset.UtcNow));
        Assert.Equal(1,f.DroppedFrames);
        Assert.Single(f.Accept("SAFE\r"u8,DateTimeOffset.UtcNow));
    }
    [Fact]
    public void StxEtxFrame_IsSupported()
    {
        var f=new SerialBarcodeFramer();
        var frames=f.Accept([0x02,0x41,0x42,0x03],DateTimeOffset.UtcNow);
        Assert.Single(frames);
        Assert.Equal("AB",System.Text.Encoding.ASCII.GetString(frames[0].Data.AsSpan()));
    }
}