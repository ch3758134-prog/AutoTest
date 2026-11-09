using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Specialists.Tests.Wave;

public sealed class WaveLabAnalysisTests
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
    public void Decimation_PreservesNarrowSpikeAndEndpoints()
    {
        var input = Enumerable.Range(0, 2000)
            .Select(x => new WaveChartPoint(x / 1000.0, x == 777 ? 50 : 1)).ToArray();
        var result = WaveChartAnalysis.InWindow(input, 0, 2, 80);
        Assert.True(result.Length <= 80);
        Assert.Equal(input[0], result[0]);
        Assert.Equal(input[^1], result[^1]);
        Assert.Contains(result, x => x.Value == 50);
        Assert.True(result.Zip(result.Skip(1)).All(x => x.First.Seconds < x.Second.Seconds));
    }

    [Fact]
    public void Decimation_DoesNotNormalizeOutOfOrderEvidence()
    {
        var points = new[] { new WaveChartPoint(0, 0), new WaveChartPoint(.2, 2),
            new WaveChartPoint(.1, 1) };
        Assert.Throws<InvalidDataException>(() => WaveChartAnalysis.InWindow(points, 0, 1, 20));
    }

    [Fact]
    public void CursorMeasurement_UsesRawSamplesNotDownsample()
    {
        var points = Enumerable.Range(0, 1000)
            .Select(i => new WaveChartPoint(i * .01, i == 512 ? 20 : 0)).ToArray();
        var measurement = WaveChartAnalysis.Measure(points, 4.5, 5.8)!;
        Assert.Equal(20, measurement.Maximum);
        Assert.Equal(0, measurement.Minimum);
        Assert.True(measurement.DeltaSeconds > 1);
        Assert.NotNull(WaveChartAnalysis.Nearest(points, 0));
        Assert.Null(WaveChartAnalysis.Nearest(Array.Empty<WaveChartPoint>(), 0));
    }

    [Fact]
    public void HighPrecisionRejectsHostArrivalEvenWithIdenticalSamples()
    {
        var host = Series([0, .1, .2, .3], source: TimeSource.HostArrival);
        var reference = Series([0, .1, .2, .3]);
        var config = WaveComparisonConfig.FromContract(new CompareProfile(.1, 1)) with
        { RequireSynchronizedHardwareTime = true };
        var answer = WaveComparator.Compare(host, reference, config);
        Assert.Equal(Verdict.Inconclusive, answer.Verdict);
        Assert.Equal("HARDWARE_TIME_REQUIRED", answer.Code);
    }

    [Fact]
    public void HighPrecisionAcceptsSynchronizedHardwareWhenSamplesAndQualityAgree()
    {
        var actual = Series([0, .1, .2, .3], source: TimeSource.DeviceSample);
        var config = WaveComparisonConfig.FromContract(new CompareProfile(.1, 1)) with
        { RequireSynchronizedHardwareTime = true };
        Assert.Equal(Verdict.Pass, WaveComparator.Compare(actual, actual, config).Verdict);
    }

    [Fact]
    public void InvalidGapRemainsInconclusiveWithSameValues()
    {
        var actual = Series([0, .1, .2, .3], gap: true);
        var config = WaveComparisonConfig.FromContract(new CompareProfile(.1, 1));
        Assert.Equal("ACTUAL_SOURCE_GAP", WaveComparator.Compare(actual,
            Series([0, .1, .2, .3]), config).Code);
    }

    [Fact]
    public void CatalogPersistsAndDetectsObsoleteDbcRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "wave-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new WaveReferenceCatalog(root);
            var saved = first.Save("baseline", Series([0, .1, .2, .3]));
            var reopened = new WaveReferenceCatalog(root);
            Assert.Equal(saved.Reference, reopened.LatestFor(Signal)!.Reference);
            Assert.Single(reopened.List());
            var changed = Signal with { DbcAsset = Signal.DbcAsset with
            { Revision = AssetRevision.Create(2) } };
            Assert.Null(reopened.LatestFor(changed));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
