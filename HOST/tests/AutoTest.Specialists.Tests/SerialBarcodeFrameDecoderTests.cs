using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class SerialBarcodeFrameDecoderTests
{
    private static List<SerialBarcodeFrame> Feed(SerialBarcodeFrameDecoder decoder,
        string text, DateTimeOffset? started = null, TimeSpan? gap = null)
    {
        var frames = new List<SerialBarcodeFrame>();
        var time = started ?? DateTimeOffset.UtcNow;
        foreach (var b in System.Text.Encoding.ASCII.GetBytes(text))
        {
            var completed = decoder.Push(b, time);
            if (completed is not null) frames.Add(completed);
            time += gap ?? TimeSpan.FromMilliseconds(2);
        }
        return frames;
    }

    [Fact]
    public void TwoConsecutiveCrLfScansProduceExactlyTwoFramesAndPreserveLeadingZeros()
    {
        var decoder = new SerialBarcodeFrameDecoder();
        var frames = Feed(decoder, "UT1-EPBA-H02-000000123456-4372\r\nUT1-EPBA-H02-000000123457-5373\r\n");
        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.True(f.Accepted));
        Assert.Equal("UT1-EPBA-H02-000000123456-4372", frames[0].Text);
        Assert.Equal("UT1-EPBA-H02-000000123457-5373", frames[1].Text);
    }

    [Fact]
    public void ReadChunksAreNotFramesAndCrOnlyIsAValidTerminator()
    {
        var decoder = new SerialBarcodeFrameDecoder();
        Assert.Empty(Feed(decoder, "UT1-EPBA-"));
        Assert.Equal("UT1-EPBA-H02-000000123456-4372",
            Assert.Single(Feed(decoder, "H02-000000123456-4372\r")).Text);
    }

    [Fact]
    public void OversizeAndNonAsciiAreDiscardedUntilNextTerminator()
    {
        var decoder = new SerialBarcodeFrameDecoder(8);
        Assert.Equal("SERIAL_FRAME_TOO_LONG", Assert.Single(Feed(decoder, "0123456789\r")).Code);
        Assert.True(Assert.Single(Feed(decoder, "0000001\n")).Accepted);
        decoder.Push(0xff, DateTimeOffset.UtcNow);
        Assert.Equal("SERIAL_FRAME_NON_ASCII", Assert.Single(Feed(decoder, "123\n")).Code);
    }

    [Fact]
    public void InterByteGapNeverReinterpretsTrailingSuffixAsBarcode()
    {
        var decoder = new SerialBarcodeFrameDecoder(512, TimeSpan.FromMilliseconds(50));
        var time = DateTimeOffset.UtcNow;
        decoder.Push((byte)'A', time);
        decoder.Push((byte)'B', time.AddMilliseconds(200));
        var result = decoder.Push((byte)'\n', time.AddMilliseconds(210));
        Assert.Equal("SERIAL_FRAME_GAP", result!.Code);
        Assert.Equal("OK", Assert.Single(Feed(decoder, "OK\r")).Text);
    }
}
