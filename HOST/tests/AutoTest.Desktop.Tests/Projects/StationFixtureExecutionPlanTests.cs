using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class StationFixtureExecutionPlanTests
{
    [Fact]
    public void DisabledFixtureNeverEmitsAnyOutputCommands()
    {
        var plan = StationFixtureExecutionPlan.From(ProductionFixtureConfiguration.Draft);
        Assert.Empty(plan.Press);
        Assert.Empty(plan.Release);
        Assert.Equal((byte)0, plan.AllowedOutputMask);
    }

    [Theory]
    [InlineData(true, false, 1, 1)]
    [InlineData(false, true, 2, 2)]
    public void SingleClampOperatesOnlyItsExplicitlySelectedOutput(
        bool first, bool second, byte expected, byte mask)
    {
        var station = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = first, UseClamp2 = second
        };
        var plan = StationFixtureExecutionPlan.From(station);
        Assert.Equal(mask, plan.AllowedOutputMask);
        Assert.Equal(new[] { new StationClampAction(expected, true) }, plan.Press);
        Assert.Equal(new[] { new StationClampAction(expected, false) }, plan.Release);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void DualClampUsesDeclaredOrderAndReleasesInReverse(
        byte first, byte second)
    {
        var station = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = true, UseClamp2 = true,
            FirstClamp = first, MovementTimeoutMs = 6500
        };
        var plan = StationFixtureExecutionPlan.From(station);
        Assert.Equal((byte)3, plan.AllowedOutputMask);
        Assert.Equal(new[]
        {
            new StationClampAction(first, true), new StationClampAction(second, true)
        }, plan.Press);
        Assert.Equal(new[]
        {
            new StationClampAction(second, false), new StationClampAction(first, false)
        }, plan.Release);
        Assert.Equal(6500, plan.MovementTimeoutMs);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void OneLogicalFixturePressReleaseRespectsElectricalPolarity(
        bool pressHigh, bool expectedHighWhenPress, bool expectedHighWhenReleased)
    {
        var config = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = true, Clamp1PressHigh = pressHigh,
            SettleDelayMs = 500
        };
        var plan = StationFixtureExecutionPlan.From(config);
        Assert.Equal(expectedHighWhenPress, plan.OutputLevelHigh(plan.Press.Single()));
        Assert.Equal(expectedHighWhenReleased, plan.OutputLevelHigh(plan.Release.Single()));
        Assert.Equal(500, plan.SettleDelayMs);
    }

    [Fact]
    public void Output2PolarityIsIndependentOfOutput1()
    {
        var plan = StationFixtureExecutionPlan.From(ProductionFixtureConfiguration.Draft with
        {
            UseClamp2 = true, Clamp1PressHigh = true, Clamp2PressHigh = false
        });
        Assert.False(plan.OutputLevelHigh(plan.Press.Single()));
        Assert.True(plan.OutputLevelHigh(plan.Release.Single()));
    }

    [Fact]
    public void DisableAndReenableKeepsVerifiedPhysicalOutputRouting()
    {
        var installed = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = false, UseClamp2 = true,
            Clamp2PressHigh = false, AutoClampEnabled = false
        };
        var disabled = StationFixtureExecutionPlan.From(installed);
        Assert.Empty(disabled.Press);
        Assert.Equal((byte)0, disabled.AllowedOutputMask);
        var restored = StationFixtureExecutionPlan.From(installed with
        {
            AutoClampEnabled = true
        });
        Assert.Single(restored.Press);
        Assert.Equal((byte)2, restored.AllowedOutputMask);
        Assert.False(restored.OutputLevelHigh(restored.Press[0]));
    }

    [Fact]
    public void ExcessiveStabilizationDelayIsInvalidNotSilentlyClamped()
    {
        var config = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = true, SettleDelayMs = 30001
        };
        Assert.Throws<System.IO.InvalidDataException>(
            () => StationFixtureExecutionPlan.From(config));
    }

    [Fact]
    public void DualClampMustNotSilentlyChooseOrder()
    {
        var station = ProductionFixtureConfiguration.Draft with
        {
            UseClamp1 = true, UseClamp2 = true
        };
        Assert.Throws<System.IO.InvalidDataException>(
            () => StationFixtureExecutionPlan.From(station));
    }
}
