using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.TestFixtures;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class MockTransportTests
{
    [Fact]
    public void FragmentationIsDeterministic_WithoutUsbHandles()
    {
        var link = new FakeUsbByteLink();
        var chunks = link.Deliver(new byte[] { 1, 2, 3, 4, 5 }, 2);
        Assert.Equal(new[] { 2, 2, 1 }, chunks.Select(c => c.Length));
        link.DropLastChunk = true;
        Assert.Equal(2, link.Deliver(new byte[] { 1, 2, 3, 4, 5 }, 2).Length);
        link.DisconnectInjected = true;
        Assert.Throws<IOException>(() => link.Deliver(new byte[] { 1 }, 1));
    }

    [Fact]
    public async Task ReplayPreservesProvenanceAndLossFacts()
    {
        var frame = CanFrame.Create(BusRole.DutCan, false, 0x321, new byte[] { 10 });
        var evt = new CanObservation(frame,
            TimePoint.FromReplay(DateTimeOffset.UtcNow, 12),
            EvidenceQuality.Derived, 1, 4, EvidenceRef.New());
        var replay = new ReplayCanStream(new[] { evt });
        var readings = new List<CanObservation>();
        await foreach (var item in replay.PlayAsync()) readings.Add(item);
        Assert.Single(readings);
        Assert.Equal(4, readings[0].LostFrames);
        Assert.Equal(TimeQuality.ReplayDerived, readings[0].Time.Quality);
    }
}
