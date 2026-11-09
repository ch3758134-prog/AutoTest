using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Specialists.Tests.Wave;

public sealed class WaveIntervalDiagnosticsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly SignalRef Signal = new(
        new AssetRef(AssetId.New(), AssetRevision.Create(1),
            RevisionFingerprint.FromSha256(new string('e', 64))),
        BusRole.DutCan, 0x321, "Current", "A");

    private static WaveSeries Build(double[] seconds, double[] values, int reportedGap = -1)
    {
        var buffer = new WaveSampleBuffer(Signal, TimePoint.FromReplay(Start, 0), seconds.Length + 4);
        for (var i = 0; i < seconds.Length; i++)
            buffer.Append(new SignalObservation(Signal, values[i], "A",
                TimePoint.FromReplay(Start.AddSeconds(seconds[i]), i + 1),
                EvidenceQuality.Derived, EvidenceRef.New(), i == reportedGap));
        return buffer.Snapshot();
    }

    private static WaveComparisonConfig Strict => new(.1, 0, 1, 1, 0, 2, 0, 0);

    [Fact]
    public void LargeObservedIntervalReportsBoundingSamplesAndBothEvidenceRefs()
    {
        var wave = Build([.02, .04, .06, .20, .22, .24], [1, 1, 1, 1, 1, 1]);
        var issue = Assert.Single(WaveIntervalDiagnostics.FindGaps(wave, 1.6));
        Assert.Equal("SAMPLE_INTERVAL_EXCEEDED", issue.Code);
        Assert.Equal(.06, issue.StartSeconds!.Value, 6);
        Assert.Equal(.20, issue.EndSeconds!.Value, 6);
        Assert.Equal(wave.Samples[2].SourceEvidence.Value.ToString("D"), issue.FirstSourceEvidenceId);
        Assert.Equal(wave.Samples[3].SourceEvidence.Value.ToString("D"), issue.LastSourceEvidenceId);
        Assert.Contains("不能据此断言确切丢帧数量", issue.Detail);
    }

    [Fact]
    public void ReportedGapWithoutPerSampleEvidenceHasUnknownPosition()
    {
        var wave = Build([.02, .04, .06], [1, 1, 1]) with { HasGap = true };
        var issue = Assert.Single(WaveIntervalDiagnostics.FindGaps(wave, 1.6));
        Assert.Equal("SOURCE_GAP_UNLOCATED", issue.Code);
        Assert.Null(issue.StartSeconds);
        Assert.Null(issue.EndSeconds);
        Assert.Equal("UNKNOWN", issue.FirstSourceEvidenceId);
        Assert.StartsWith("UNKNOWN", issue.Window);
    }

    [Fact]
    public void SampleMarkedGapIsBoundedEvenIfIntervalLooksNormal()
    {
        var wave = Build([.02, .04, .06, .08], [1, 1, 1, 1], reportedGap: 2);
        var issue = Assert.Single(WaveIntervalDiagnostics.FindGaps(wave, 1.6));
        Assert.Equal("SOURCE_GAP_AT_SAMPLE", issue.Code);
        Assert.Equal(.04, issue.StartSeconds!.Value, 6);
        Assert.Equal(.06, issue.EndSeconds!.Value, 6);
    }

    [Fact]
    public void NonMonotonicQualityDoesNotInventGapWindow()
    {
        var wave = Build([.02, .04, .06], [1, 1, 1]) with { NonMonotonic = true };
        var issue = Assert.Single(WaveIntervalDiagnostics.FindGaps(wave, 1.6));
        Assert.Equal("NON_MONOTONIC", issue.Code);
        Assert.Null(issue.StartSeconds);
    }

    [Fact]
    public void MismatchWindowsUseExistingComparatorShiftAndOriginalFrameEvidence()
    {
        var times = new[] { .02, .04, .06, .08, .10, .12 };
        var expected = Build(times, [1, 1, 1, 1, 1, 1]);
        var actual = Build(times, [1, 1, 2, 2, 1, 1]);
        var judgement = WaveComparator.Compare(actual, expected, Strict);
        Assert.Equal(Verdict.Fail, judgement.Verdict);
        var window = Assert.Single(WaveIntervalDiagnostics.FindMismatches(actual, expected, Strict, judgement));
        Assert.Equal("OUT_OF_TOLERANCE_SAMPLED_WINDOW", window.Code);
        Assert.Equal(.06, window.StartSeconds!.Value, 6);
        Assert.Equal(.08, window.EndSeconds!.Value, 6);
        Assert.Equal(2, window.AffectedSamples);
        Assert.Equal(1, window.MaximumError!.Value, 6);
        Assert.Equal(actual.Samples[2].SourceEvidence.Value.ToString("D"), window.FirstSourceEvidenceId);
        Assert.Equal(actual.Samples[3].SourceEvidence.Value.ToString("D"), window.LastSourceEvidenceId);
    }

    [Fact]
    public void ShiftedMismatchIsProjectedOntoActualCaptureTimeline()
    {
        var times = new[] { .02, .04, .06, .08, .10, .12, .14, .16, .18, .20 };
        var expected = Build(times, Enumerable.Repeat(1d, times.Length).ToArray());
        var actual = Build(times.Select(x => x + .04).ToArray(),
            [1, 1, 1, 2, 1, 1, 1, 1, 1, 1]);
        var config = Strict with { MaximumShiftSeconds = .05, MinimumCoverage = 1 };
        var judgement = WaveComparator.Compare(actual, expected, config);
        Assert.Equal(Verdict.Fail, judgement.Verdict);
        Assert.Equal(.04, judgement.ShiftSeconds, 3);
        var issue = Assert.Single(WaveIntervalDiagnostics.FindMismatches(actual, expected, config, judgement));
        Assert.Equal(.12, issue.StartSeconds!.Value, 3);
        Assert.Equal(.12, issue.EndSeconds!.Value, 3);
    }

    [Fact]
    public void BadQualityOrInconclusiveNeverProduceInventedMismatch()
    {
        var times = new[] { .02, .04, .06, .08 };
        var expected = Build(times, [1, 1, 1, 1]);
        var actual = Build(times, [1, 2, 1, 1]) with { HasGap = true };
        var judgement = WaveComparator.Compare(actual, expected, Strict);
        Assert.Equal(Verdict.Inconclusive, judgement.Verdict);
        Assert.Empty(WaveIntervalDiagnostics.FindMismatches(actual, expected, Strict, judgement));
    }

    [Fact]
    public void TwentyThousandOrderedSamplesWithoutEvidenceOfGapsCreateNoMarkers()
    {
        var times = Enumerable.Range(1, 20_000).Select(i => i * .001).ToArray();
        var wave = Build(times, Enumerable.Repeat(1d, times.Length).ToArray());
        Assert.Empty(WaveIntervalDiagnostics.FindGaps(wave, 1.6));
    }
}
