using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveQualityDescription(bool CanJudge, string Code, string Reason,
    TimeSource Source, TimeQuality Quality, double MedianIntervalSeconds,
    double MaximumIntervalSeconds, long Observed, int ValidSamples);

public static class WaveTimeQuality
{
    public static void ValidateSignal(SignalRef signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.DbcAsset.Id.Value == Guid.Empty || signal.DbcAsset.Revision.Value < 1 ||
            string.IsNullOrEmpty(signal.DbcAsset.Fingerprint.Sha256) ||
            string.IsNullOrWhiteSpace(signal.SignalName) || signal.Unit is null ||
            signal.FrameId > 0x1FFFFFFF)
            throw new ArgumentException("A revision-pinned DBC SignalRef and unit are required.", nameof(signal));
    }

    public static WaveQualityDescription Describe(WaveSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var count = series.Samples.Length;
        WaveQualityDescription Bad(string code, string reason, double median = 0, double maximum = 0) =>
            new(false, code, reason, series.Origin.Source, series.Origin.Quality,
                median, maximum, series.ObservedEvents, count);
        if (series.Origin.Quality == TimeQuality.Unknown) return Bad("UNKNOWN_CLOCK", "Unknown time quality.");
        if (!IsCompatibleClock(series.Origin))
            return Bad("INCOMPATIBLE_CLOCK", "Time source and quality are not a coherent pair.");
        if (series.MixedClock) return Bad("MIXED_CLOCK", "Source/time quality changed during capture.");
        if (series.NonMonotonic) return Bad("NON_MONOTONIC", "Arrival timestamps or monotonic counters regressed.");
        if (series.Overflow) return Bad("BUFFER_OVERFLOW", "Bounded waveform buffer overflowed.");
        if (series.HasGap) return Bad("SOURCE_GAP", "Input stream reported one or more gaps.");
        if (series.InvalidInput || series.RejectedEvents != 0) return Bad("INVALID_INPUT", "Invalid value, signal, unit or evidence quality.");
        if (count < 2) return Bad("INSUFFICIENT_SAMPLES", "At least two samples are required.");
        var spans = new double[count - 1];
        for (var i = 0; i < count; i++)
        {
            var item = series.Samples[i];
            if (!double.IsFinite(item.Sample.Value) || item.Sample.Gap ||
                item.Sample.Unit != series.Signal.Unit ||
                item.Sample.Time.Source != series.Origin.Source ||
                item.Sample.Time.Quality != series.Origin.Quality ||
                item.SourceEvidence.Value == Guid.Empty ||
                item.InputQuality is EvidenceQuality.Unknown or EvidenceQuality.AcknowledgedOnly)
                return Bad("INVALID_SAMPLE", "Sample data or origin is not valid for judgement.");
            if (i == 0 && (item.Sample.Time.Utc < series.Origin.Utc ||
                item.Sample.Time.MonotonicTicks < series.Origin.MonotonicTicks))
                return Bad("INVALID_TIME_ORIGIN", "First sample precedes capture origin.");
            if (i > 0)
            {
                var prev = series.Samples[i - 1].Sample.Time;
                var interval = (item.Sample.Time.Utc - prev.Utc).TotalSeconds;
                if (interval <= 0 || item.Sample.Time.MonotonicTicks <= prev.MonotonicTicks)
                    return Bad("NON_MONOTONIC", "Sample order is not strictly monotonic.");
                spans[i - 1] = interval;
            }
        }
        Array.Sort(spans);
        var median = spans.Length % 2 == 0
            ? (spans[spans.Length / 2 - 1] + spans[spans.Length / 2]) / 2
            : spans[spans.Length / 2];
        var max = spans[^1];
        return new(true, "OK", "Valid ordered capture; provenance must still be checked.",
            series.Origin.Source, series.Origin.Quality, median, max,
            series.ObservedEvents, count);
    }

    public static WaveQualityDescription ValidateForJudgement(WaveSeries series) => Describe(series);

    private static bool IsCompatibleClock(TimePoint point) =>
        (point.Source, point.Quality) switch
        {
            (TimeSource.HostArrival, TimeQuality.HostArrivalOnly) => true,
            (TimeSource.Replay, TimeQuality.ReplayDerived) => true,
            (TimeSource.Simulation, TimeQuality.Simulated) => true,
            (TimeSource.DeviceSample, TimeQuality.SynchronizedHardware) => true,
            _ => false
        };
}
