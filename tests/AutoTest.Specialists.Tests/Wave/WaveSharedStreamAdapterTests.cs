using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Specialists.Tests.Wave;

public sealed class WaveSharedStreamAdapterTests
{
    private static readonly SignalRef Signal = new(
        new AssetRef(AssetId.New(), AssetRevision.Create(1),
            RevisionFingerprint.FromSha256(new string('a', 64))),
        BusRole.DutCan, 0x321, "MotorCurrent", "A");
    private static readonly DateTimeOffset Origin = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    private static WaveSeries Series(double[] seconds, double[]? values = null,
        bool gap = false, TimeSource source = TimeSource.Replay)
    {
        var timeQuality = source switch
        {
            TimeSource.Replay => TimeQuality.ReplayDerived,
            TimeSource.HostArrival => TimeQuality.HostArrivalOnly,
            TimeSource.DeviceSample => TimeQuality.SynchronizedHardware,
            _ => TimeQuality.Simulated
        };
        var start = new TimePoint(Origin, 0, source, timeQuality);
        var buffer = new WaveSampleBuffer(Signal, start, Math.Max(16, seconds.Length + 1));
        for (var i = 0; i < seconds.Length; i++)
            buffer.Append(new SignalObservation(Signal, values?[i] ?? i, "A",
                new TimePoint(Origin.AddSeconds(seconds[i]), i + 1, source, timeQuality),
                EvidenceQuality.Derived, EvidenceRef.New(), gap && i == 2));
        return buffer.Snapshot();
    }

    [Fact]
    public async Task LiveAdapter_RejectsReconnectAndDbcEdit()
    {
        var generation = 5L;
        var current = true;
        async IAsyncEnumerable<SignalObservation> Generate()
        {
            yield return new SignalObservation(Signal, 1, "A",
                TimePoint.FromHostArrival(Origin.AddMilliseconds(10), 1),
                EvidenceQuality.HostObserved, EvidenceRef.New(), false);
            await Task.Yield();
            yield return new SignalObservation(Signal, 2, "A",
                TimePoint.FromHostArrival(Origin.AddMilliseconds(20), 2),
                EvidenceQuality.HostObserved, EvidenceRef.New(), false);
        }
        var source = new WaveSharedStreamAdapter((_, _) => ValueTask.FromResult(
            new WaveSignalSubscription(TimePoint.FromHostArrival(Origin, 0), Generate())),
            _ => current, () => generation);
        var opened = await source.OpenAsync(Signal, CancellationToken.None);
        await using var iterator = opened.Observations.GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        generation++;
        var ex = await Assert.ThrowsAsync<WaveCaptureException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal("SESSION_GENERATION_CHANGED", ex.Code);
        generation--;
        current = false;
        var dbc = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
            await source.OpenAsync(Signal, CancellationToken.None));
        Assert.Equal("DBC_REVISION_STALE", dbc.Code);
    }

    [Fact]
    public async Task LiveAdapter_RejectsUnpinnedSignal()
    {
        async IAsyncEnumerable<SignalObservation> Bad()
        {
            yield return new SignalObservation(Signal with { Unit = "mA" }, 1, "mA",
                TimePoint.FromHostArrival(Origin.AddMilliseconds(10), 1),
                EvidenceQuality.HostObserved, EvidenceRef.New(), false);
            await Task.CompletedTask;
        }
        var adapter = new WaveSharedStreamAdapter((_, _) => ValueTask.FromResult(
            new WaveSignalSubscription(TimePoint.FromHostArrival(Origin, 0), Bad())),
            _ => true, () => 1);
        var opened = await adapter.OpenAsync(Signal, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
        {
            await foreach (var _ in opened.Observations) { }
        });
        Assert.Equal("SIGNAL_IDENTITY_MISMATCH", ex.Code);
    }
}
