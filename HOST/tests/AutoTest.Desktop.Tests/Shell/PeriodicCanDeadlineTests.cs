using AutoTest.Desktop.Shell;
using AutoTest.Core;
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
    public void ManualAndTestWorkersShareIdenticalDeadlinePolicy()
    {
        for (long now = 0; now <= 1000; now += 13)
        {
            var manual = PeriodicCanDeadline.Advance(0, now, 65);
            var automated = MonotonicDeadline.Advance(0, now, 65);
            Assert.Equal(manual.NextTicks, automated.NextTicks);
            Assert.Equal(manual.SkippedPeriods, automated.SkippedPeriods);
        }
    }

    [Fact]
    public void RejectsInvalidPeriodWithoutScheduling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PeriodicCanDeadline.Advance(0, 10, 0));
    }
}
