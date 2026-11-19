using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class WaveComparatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly SignalRef Signal = new(new AssetRef(AssetId.New(),
        AssetRevision.Create(1), RevisionFingerprint.FromSha256(new string('a', 64))),
        BusRole.DutCan, 0x123, "Motor_Current", "A");
    private static readonly double[] Times = [0, 0.1, 0.2, 0.3, 0.4];
    private static readonly double[] Values = [0, 1, 3, 1, 0];
    private static readonly WaveComparisonConfig Strict =
        new(0.001, 0, 1, 1, 0.05, 1.6, 0, 0.35);

    private static TimePoint Point(double seconds, TimeSource source = TimeSource.Replay,
        TimeQuality quality = TimeQuality.ReplayDerived) => new(
        T0.AddSeconds(seconds), 1_000_000 + (long)Math.Round(seconds * 1_000_000),
        source, quality);

    private static WaveSeries Series(double[]? times = null, double[]? values = null,
        SignalRef? signal = null, bool gap = false)
    {
        times ??= Times;
        values ??= Values;
        Assert.Equal(times.Length, values.Length);
        var s = signal ?? Signal;
        var obs = times.Select((t, i) => new SignalObservation(s, values[i], s.Unit,
            Point(t), EvidenceQuality.Derived, EvidenceRef.New(), gap && i == 2)).ToArray();
        var buffer = new WaveSampleBuffer(s, Point(0), 128);
        foreach (var sample in obs) buffer.Append(sample);
        return buffer.Snapshot();
    }

    private static (WaveCaptureService Service, WaveEvidenceStore Evidence,
        WaveReferenceRepository References, string Path) MakeService(WaveSeries fixture)
    {
        var path = Path.Combine(Path.GetTempPath(), "wave-test-" + Guid.NewGuid().ToString("N"));
        var evidence = new WaveEvidenceStore(Path.Combine(path, "evidence"));
        var references = new WaveReferenceRepository(Path.Combine(path, "references"));
        var observations = fixture.Samples.Select(x => new SignalObservation(fixture.Signal,
            x.Sample.Value, x.Sample.Unit, x.Sample.Time, x.InputQuality,
            x.SourceEvidence, x.Sample.Gap));
        return (new WaveCaptureService(new WaveReplaySignalSource(fixture.Signal,
            fixture.Origin, observations), evidence, references), evidence, references, path);
    }

    [Fact]
    public void E3_T01_IdenticalWaveformsPassWithFullCoverage()
    {
        var result = WaveComparator.Compare(Series(), Series(), Strict);
        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Equal(1, result.Coverage);
        Assert.Equal(1, result.PassRatio);
        Assert.Equal(0, result.MaximumAbsoluteError, 8);
        Assert.Equal(0, result.ShiftSeconds, 8);
    }

    [Fact]
    public void E3_T02_BoundedTimeShiftPassesAndOutsideLimitIsInconclusive()
    {
        var shifted = Series(Times.Select(x => x + .02).ToArray());
        var result = WaveComparator.Compare(shifted, Series(), Strict);
        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.InRange(result.ShiftSeconds, .019, .021);
        Assert.Equal(1, result.Coverage);
        var beyond = WaveComparator.Compare(Series(Times.Select(x => x + .08).ToArray()),
            Series(), Strict);
        Assert.Equal(Verdict.Inconclusive, beyond.Verdict);
        Assert.Equal("INSUFFICIENT_COVERAGE", beyond.Code);
    }

    [Fact]
    public void E3_T03_MissingSampleDoesNotGetInterpolatedIntoPass()
    {
        var missing = Series([0, .1, .3, .4], [0, 1, 1, 0]);
        var result = WaveComparator.Compare(missing, Series(), Strict);
        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.Equal("MISSING_SAMPLES", result.Code);
    }

    [Fact]
    public void E3_T03_NonMonotonicSourceOrderIsNeverSortedAway()
    {
        var outOfOrder = Series([0, .2, .1, .3, .4], [0, 3, 1, 1, 0]);
        Assert.True(outOfOrder.NonMonotonic);
        Assert.Equal(Verdict.Inconclusive, WaveComparator.Compare(outOfOrder, Series(), Strict).Verdict);
    }

    [Fact]
    public void E3_T04_PhysicalUnitMismatchIsErrorWithoutImplicitConversion()
    {
        var another = Signal with { Unit = "mA" };
        var result = WaveComparator.Compare(Series(signal: another), Series(), Strict);
        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal("SIGNAL_REVISION_MISMATCH", result.Code);
    }

    [Fact]
    public void E3_T04_DbcRevisionChangeInvalidatesReference()
    {
        var another = Signal with { DbcAsset = Signal.DbcAsset with
        { Revision = AssetRevision.Create(2) } };
        Assert.Equal("SIGNAL_REVISION_MISMATCH",
            WaveComparator.Compare(Series(signal: another), Series(), Strict).Code);
    }

    [Fact]
    public async Task E3_T05_ReplayHasReplayProvenanceNotRealCanEvidence()
    {
        var (service, store, _, path) = MakeService(Series());
        try
        {
            var capture = await service.CaptureAsync(Signal,
                new WaveCaptureConfig(TimeSpan.FromSeconds(1), 64), CancellationToken.None);
            var document = store.Read(capture.Evidence);
            Assert.Equal("Capture", document.Kind);
            Assert.Equal(TimeSource.Replay, document.Actual.Origin.Source);
            Assert.Equal(TimeQuality.ReplayDerived, capture.Quality);
            Assert.Equal(5, document.Actual.Samples.Length);
            Assert.All(document.Actual.Samples, x => Assert.NotEqual(Guid.Empty,
                x.SourceEvidence.Value));
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task E3_T06_PinnedEvidenceCanBeReopenedAndReevaluated()
    {
        var reference = Series();
        var (service, store, refs, path) = MakeService(reference);
        try
        {
            var asset = refs.SaveReference(AssetId.New(), AssetRevision.Create(1),
                "Motor apply reference", reference);
            var capture = await service.CaptureAsync(Signal,
                new WaveCaptureConfig(TimeSpan.FromSeconds(1), 64), CancellationToken.None);
            var result = await service.CompareAsync(capture, asset.Reference,
                new CompareProfile(.001, 1));
            Assert.Equal(Verdict.Pass, result.Verdict);
            Assert.Equal(2, result.Evidence.Length);
            var doc = store.Read(result.Evidence[1]);
            Assert.Equal(capture.Evidence, doc.CaptureEvidence);
            Assert.Equal(asset.Reference, doc.ReferenceAsset);
            Assert.True(store.Verify(result.Evidence[1]));
            Assert.Equal(Verdict.Pass, doc.Result!.Verdict);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void GapEvidenceForcesInconclusiveEvenIfValuesFit()
    {
        var actual = Series(gap: true);
        Assert.Equal("ACTUAL_SOURCE_GAP", WaveComparator.Compare(actual, Series(), Strict).Code);
    }

    [Fact]
    public void UnknownClockFailsSafely()
    {
        var points = Series();
        var unknown = points with { Origin = points.Origin with { Quality = TimeQuality.Unknown } };
        Assert.Equal("ACTUAL_UNKNOWN_CLOCK", WaveComparator.Compare(unknown, points, Strict).Code);
    }

    [Fact]
    public void BufferOverflowIsVisibleAndBlocksJudgement()
    {
        var buffer = new WaveSampleBuffer(Signal, Point(0), 2);
        foreach (var (t, i) in Times.Select((t, i) => (t, i)))
            buffer.Append(new SignalObservation(Signal, Values[i], "A", Point(t),
                EvidenceQuality.Derived, EvidenceRef.New(), false));
        var result = buffer.Snapshot();
        Assert.True(result.Overflow);
        Assert.Equal(5, result.ObservedEvents);
        Assert.Equal("ACTUAL_BUFFER_OVERFLOW",
            WaveComparator.Compare(result, Series(), Strict).Code);
    }

    [Fact]
    public void UntrustedSignalObservationNeverBecomesValidMeasurement()
    {
        var buffer = new WaveSampleBuffer(Signal, Point(0), 16);
        foreach (var (t, i) in Times.Select((t, i) => (t, i)))
            buffer.Append(new SignalObservation(Signal, Values[i], "A", Point(t),
                EvidenceQuality.AcknowledgedOnly, EvidenceRef.New(), false));
        Assert.Equal("ACTUAL_INVALID_INPUT",
            WaveComparator.Compare(buffer.Snapshot(), Series(), Strict).Code);
    }

    [Fact]
    public void OutOfToleranceCurveFailsWithDurationEvidence()
    {
        var actual = Series(values: [0, 10, 30, 10, 0]);
        var result = WaveComparator.Compare(actual, Series(), Strict);
        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal("WAVE_OUT_OF_TOLERANCE", result.Code);
        Assert.True(result.OutsideToleranceSeconds > 0);
    }

    [Fact]
    public void InvalidComparisonThresholdsCannotPass()
    {
        var config = Strict with { MinimumCoverage = 1.4 };
        var result = WaveComparator.Compare(Series(), Series(), config);
        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal("INVALID_COVERAGE", result.Code);
    }

    [Fact]
    public void EvidenceJsonRejectsNaNAndCorruptedSchema()
    {
        var document = new WaveEvidenceDocument(EvidenceRef.New(), "Capture",
            Series(), null, null, null, null, null, null, null, null, T0);
        var bytes = WaveEvidenceCodec.Write(document);
        var roundtrip = WaveEvidenceCodec.Read(bytes);
        Assert.Equal(document.Id, roundtrip.Id);
        Assert.Equal(document.Actual.Samples.Length, roundtrip.Actual.Samples.Length);
        Assert.Equal(document.Actual.Signal, roundtrip.Actual.Signal);
        Assert.True(WaveEvidenceCodec.Verify(document));
        Assert.Throws<InvalidDataException>(() => WaveEvidenceCodec.Read("{}"u8));
    }

    [Fact]
    public void ReferenceAssetIsImmutableAndPinnedToSha256()
    {
        var path = Path.Combine(Path.GetTempPath(), "wave-reference-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WaveReferenceRepository(path);
            var id = AssetId.New();
            var saved = repo.SaveReference(id, AssetRevision.Create(1), "r1", Series());
            Assert.Equal(saved.Reference, repo.LoadReference(saved.Reference).Reference);
            Assert.Throws<IOException>(() => repo.SaveReference(id, AssetRevision.Create(1), "r1", Series()));
            var tampered = saved.Reference with { Asset = saved.Reference.Asset with
            { Fingerprint = RevisionFingerprint.FromSha256(new string('b', 64)) } };
            Assert.Throws<InvalidDataException>(() => repo.LoadReference(tampered));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task StepHandlerKeepsRunAndStepEvidenceLinks()
    {
        var source = Series();
        var (service, store, refs, path) = MakeService(source);
        try
        {
            var reference = refs.SaveReference(AssetId.New(), AssetRevision.Create(1), "test", source);
            var request = new WaveStepRequest(RunId.New(), StepId.New(), Signal,
                new WaveCaptureConfig(TimeSpan.FromSeconds(1), 64), reference.Reference,
                new CompareProfile(.001, 1));
            var result = await new WaveStepHandler(service).ExecuteAsync(request, CancellationToken.None);
            Assert.Equal(Verdict.Pass, result.Verdict);
            var evidence = store.Read(result.Evidence[^1]);
            Assert.Equal(request.Run, evidence.Run);
            Assert.Equal(request.Step, evidence.Step);
        }
        finally { Directory.Delete(path, true); }
    }
    [Fact]
    public async Task FixedJsonReplayFixture_IsDeterministicAndTraceable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "samples", "wave", "fixed_signal_replay.json")))
            root = root.Parent;
        Assert.NotNull(root);
        var bytes = File.ReadAllBytes(Path.Combine(root!.FullName, "samples", "wave",
            "fixed_signal_replay.json"));
        var fixture = WaveReplayFixtureCodec.Read(bytes);
        var location = Path.Combine(Path.GetTempPath(), "wave-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new WaveCaptureService(fixture.Source,
                new WaveEvidenceStore(Path.Combine(location, "evidence")),
                new WaveReferenceRepository(Path.Combine(location, "references")));
            var captured = await service.CaptureAsync(fixture.Signal,
                new WaveCaptureConfig(TimeSpan.FromSeconds(1), 20), CancellationToken.None);
            var evidence = service.ReadEvidence(captured.Evidence);
            Assert.Equal(5, evidence.Actual.Samples.Length);
            Assert.Equal(3, evidence.Actual.Samples[2].Sample.Value);
            Assert.Equal(TimeSource.Replay, evidence.Actual.Origin.Source);
            Assert.Equal(Guid.Parse("a3333333-3333-4333-8333-333333333333"),
                evidence.Actual.Samples[2].SourceEvidence.Value);
        }
        finally { if (Directory.Exists(location)) Directory.Delete(location, true); }
    }

    [Fact]
    public async Task CompleteConfiguredCriteria_AreUsedAndPersistedInWaveAnalysisEvidence()
    {
        var series = Series();
        var (service, evidence, references, directory) = MakeService(series);
        try
        {
            var reference = references.SaveReference(AssetId.New(),
                AssetRevision.Create(1), "approved", series);
            var captured = await service.CaptureAsync(Signal,
                new WaveCaptureConfig(TimeSpan.FromSeconds(1), 64), CancellationToken.None);
            var configured = Strict with
            { MaximumOutOfToleranceSeconds = 0, RequireSynchronizedHardwareTime = false };
            var result = await service.CompareWithConfigForRunAsync(
                captured, reference.Reference, configured);
            Assert.Equal(Verdict.Pass, result.Verdict);
            var comparison = evidence.Read(result.Evidence[1]);
            Assert.Equal(configured, comparison.Configuration);
            Assert.True(evidence.Verify(result.Evidence[1]));
        }
        finally { Directory.Delete(directory, true); }
    }

}
