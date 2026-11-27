using AutoTest.Application.Production;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class ProductionStartGateTests
{
    [Fact]
    public void UnknownOrHeldButtonCanNeverStartAUnit()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        Assert.Equal("PHYSICAL_FRESH_EDGE_REQUIRED",
            gate.ObservePhysical(run, true).Code);
        Assert.False(gate.ObservePhysical(run, true).Accepted);
    }

    [Fact]
    public void PhysicalStartRequiresReleaseThenOneFreshPress()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        Assert.Equal("PHYSICAL_RELEASE_OBSERVED",
            gate.ObservePhysical(run, false).Code);
        Assert.True(gate.ObservePhysical(run, true).Accepted);
        Assert.Equal("START_ALREADY_CLAIMED",
            gate.ObservePhysical(run, true).Code);
    }

    [Fact]
    public void AnotherUnitNeedsExplicitDisarmAndDistinctRun()
    {
        var gate = new ProductionStartGate();
        var first = RunId.New();
        var second = RunId.New();
        gate.Arm(first);
        Assert.Throws<InvalidOperationException>(() => gate.Arm(first));
        Assert.Throws<InvalidOperationException>(() => gate.Arm(second));
        gate.ObservePhysical(first, false);
        Assert.True(gate.ObservePhysical(first, true).Accepted);
        Assert.Throws<InvalidOperationException>(() => gate.Arm(second));
        gate.Disarm();
        Assert.False(gate.ObservePhysical(first, true).Accepted);
        gate.Arm(second);
        Assert.False(gate.ObservePhysical(second, true).Accepted);
        gate.ObservePhysical(second, false);
        Assert.True(gate.ObservePhysical(second, true).Accepted);
    }

    [Fact]
    public void RepeatedConcurrentPressedSignalsCanClaimAtMostOneStart()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        gate.ObservePhysical(run, false);
        var answers = new System.Collections.Concurrent.ConcurrentBag<bool>();
        Parallel.For(0, 128, _ =>
            answers.Add(gate.ObservePhysical(run, true).Accepted));
        Assert.Equal(1, answers.Count(x => x));
    }
}
