using AutoTest.Application.Production;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class ProductionStartGateTests
{
    [Fact]
    public void SoftwareStartClaimsTheInspection_AndPhysicalCannotStartItAgain()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        Assert.False(gate.ObservePhysical(run, false).Accepted);
        Assert.True(gate.RequestSoftware(run).Accepted);
        Assert.False(gate.ObservePhysical(run, true).Accepted);
        Assert.Equal("START_ALREADY_CLAIMED",
            gate.RequestSoftware(run).Code);
    }

    [Fact]
    public void PhysicalStartRequiresReleaseThenFreshPress_AndSuppressesHeldSignal()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        Assert.Equal("PHYSICAL_FRESH_EDGE_REQUIRED",
            gate.ObservePhysical(run, true).Code);
        Assert.Equal("PHYSICAL_RELEASE_OBSERVED",
            gate.ObservePhysical(run, false).Code);
        Assert.True(gate.ObservePhysical(run, true).Accepted);
        Assert.False(gate.ObservePhysical(run, true).Accepted);
        Assert.Equal("START_ALREADY_CLAIMED", gate.RequestSoftware(run).Code);
    }

    [Fact]
    public void AnotherInspectionRequiresExplicitDisarmAndDistinctRun()
    {
        var gate = new ProductionStartGate();
        var first = RunId.New();
        var second = RunId.New();
        gate.Arm(first);
        Assert.Throws<InvalidOperationException>(() => gate.Arm(first));
        Assert.Throws<InvalidOperationException>(() => gate.Arm(second));
        Assert.True(gate.RequestSoftware(first).Accepted);
        gate.Disarm();
        Assert.False(gate.RequestSoftware(first).Accepted);
        gate.Arm(second);
        Assert.False(gate.ObservePhysical(second, true).Accepted);
        Assert.False(gate.ObservePhysical(second, false).Accepted);
        Assert.True(gate.ObservePhysical(second, true).Accepted);
    }

    [Fact]
    public void ConcurrentSoftwareAndPhysicalClaimsAcceptExactlyOne()
    {
        var gate = new ProductionStartGate();
        var run = RunId.New();
        gate.Arm(run);
        gate.ObservePhysical(run, false);
        var answers = new System.Collections.Concurrent.ConcurrentBag<bool>();
        Parallel.For(0, 128, i =>
        {
            var decision = i % 2 == 0
                ? gate.RequestSoftware(run)
                : gate.ObservePhysical(run, true);
            answers.Add(decision.Accepted);
        });
        Assert.Equal(1, answers.Count(x => x));
    }
}
