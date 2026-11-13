using AutoTest.Devices.Station;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class RawKeyActionTests
{
    [Theory]
    [InlineData("Pulse:0x00001:300", RawKeyOperation.Pulse, 1u, 300)]
    [InlineData("Hold:0xFFFFF", RawKeyOperation.Hold, 0xFFFFFu, 0)]
    [InlineData("Release", RawKeyOperation.Release, 0u, 0)]
    public void ParsesExplicitBounded20KeyOperations(string input,
        RawKeyOperation operation, uint mask, int duration)
    {
        var actual = RawKeyAction.Parse(input);
        Assert.Equal(operation, actual.Action);
        Assert.Equal(mask, actual.Mask);
        Assert.Equal(duration, actual.DurationMs);
    }

    [Theory]
    [InlineData("Pulse:0x00000:300")]
    [InlineData("Pulse:0x100000:300")]
    [InlineData("Pulse:0x00001:0")]
    [InlineData("Pulse:0x00001:60001")]
    [InlineData("Hold:0x00000")]
    [InlineData("Release:0x00001")]
    [InlineData("Pulse:0x00001")]
    public void RejectsUnsafeRawKeyValues(string input) =>
        Assert.Throws<FormatException>(() => RawKeyAction.Parse(input));
}
