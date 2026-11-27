using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class StationFixtureExecutionPlanTests
{
    [Fact]
    public void FixedStationAlwaysPressesBothInOrderAndReleasesInReverse()
    {
        var plan = StationFixtureExecutionPlan.From(ProductionFixtureConfiguration.Draft);
        Assert.Equal((byte)3, plan.AllowedOutputMask);
        Assert.Equal(new[]
        {
            new StationClampAction(1, true), new StationClampAction(2, true)
        }, plan.Press);
        Assert.Equal(new[]
        {
            new StationClampAction(2, false), new StationClampAction(1, false)
        }, plan.Release);
    }

    [Fact]
    public void HistoricalOptionalFlagsCannotSuppressActualFixture()
    {
        var old = ProductionFixtureConfiguration.Draft with
        {
            AutoClampEnabled = false, UseClamp1 = false, UseClamp2 = false,
            FirstClamp = 0
        };
        var plan = StationFixtureExecutionPlan.From(old);
        Assert.Equal((byte)3, plan.AllowedOutputMask);
        Assert.Equal(2, plan.Press.Count);
    }

    [Fact]
    public void FixedHardwarePolarityIsLogicalPressHighForBoth()
    {
        var plan = StationFixtureExecutionPlan.From(
            ProductionFixtureConfiguration.Draft with
            {
                Clamp1PressHigh = false, Clamp2PressHigh = false
            });
        Assert.All(plan.Press, x => Assert.True(plan.OutputLevelHigh(x)));
        Assert.All(plan.Release, x => Assert.False(plan.OutputLevelHigh(x)));
    }

    [Fact]
    public void TimeoutAndConfiguredSettlingRemainBounded()
    {
        var plan = StationFixtureExecutionPlan.From(
            ProductionFixtureConfiguration.Draft with
            {
                MovementTimeoutMs = 6500, SettleDelayMs = 500
            });
        Assert.Equal(6500, plan.MovementTimeoutMs);
        Assert.Equal(500, plan.SettleDelayMs);
    }

    [Fact]
    public void InvalidTimeoutRemainsAnError()
    {
        Assert.Throws<System.IO.InvalidDataException>(() =>
            StationFixtureExecutionPlan.From(
                ProductionFixtureConfiguration.Draft with { MovementTimeoutMs = 1 }));
    }
}
