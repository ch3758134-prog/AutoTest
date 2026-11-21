using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class PeriodicCanDeadlineTests
{
    [Theory]
    [InlineData(0, 10, 100, 100, 0)]
    [InlineData(0, 100, 100, 100, 0)]
    [InlineData(0, 151, 100, 200, 1)]
    [InlineData(0, 250, 100, 300, 2)]
    [InlineData(100, 299, 100, 300, 1)]
    public void UsesFixedMonotonicDeadlinesAndSkipsLateCycles(
        long scheduled, long now, long period, long expectedNext, long expectedSkipped)
    {
        var next = PeriodicCanDeadline.Advance(scheduled, now, period);
        Assert.Equal(expectedNext, next.NextTicks);
        Assert.Equal(expectedSkipped, next.SkippedPeriods);
    }

    [Fact]
    public void RejectsInvalidPeriodWithoutScheduling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PeriodicCanDeadline.Advance(0, 10, 0));
    }
}
