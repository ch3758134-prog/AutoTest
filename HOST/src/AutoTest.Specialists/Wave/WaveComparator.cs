using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveComparisonDetails(Verdict Verdict, string Code, string Reason,
    double Coverage, double PassRatio, double ShiftSeconds, double Rmse,
    double MaximumAbsoluteError, double OutsideToleranceSeconds,
    int ReferenceSampleCount, int ActualSampleCount, int ComparedSampleCount);

/// <summary>Bounded reference-time alignment and interpolated tolerance comparison.</summary>
public static class WaveComparator
{
    private sealed record Evaluation(double Shift, int Compared, int Passed, double ErrorSquared,
        double MaxError, double OutsideDuration, double CoveredDuration, double Coverage,
        double WeightedTime = 0, double WeightedPass = 0)
    {
        public double Rmse => Compared == 0 ? double.PositiveInfinity :
            Math.Sqrt(ErrorSquared / (WeightedTime > 0 ? WeightedTime : Compared));
        public double PassRatio => Compared == 0 ? 0 :
            WeightedTime > 0 ? WeightedPass / WeightedTime : Passed / (double)Compared;
    }

    public static WaveComparisonDetails Compare(WaveSeries actual, WaveSeries reference,
        WaveComparisonConfig config)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(config);
        WaveComparisonDetails Reject(Verdict v, string code, string reason) =>
            new(v, code, reason, 0, 0, 0, 0, 0, 0, reference.Samples.Length,
                actual.Samples.Length, 0);
        if (config.ValidateThresholds() is { } error)
            return Reject(Verdict.Error, error, "Invalid comparison thresholds.");
        if (actual.Signal != reference.Signal ||
            !actual.SourceRevision.Matches(reference.SourceRevision))
            return Reject(Verdict.Error, "SIGNAL_REVISION_MISMATCH",
                "DBC signal identity, scaling revision or physical unit differs.");
        if (config.RequireSynchronizedHardwareTime &&
            (actual.Origin.Source != TimeSource.DeviceSample ||
             actual.Origin.Quality != TimeQuality.SynchronizedHardware ||
             reference.Origin.Source != TimeSource.DeviceSample ||
             reference.Origin.Quality != TimeQuality.SynchronizedHardware))
            return Reject(Verdict.Inconclusive, "HARDWARE_TIME_REQUIRED",
                "Host arrival, replay and simulation clocks cannot certify high-precision timing.");
        var rq = WaveTimeQuality.ValidateForJudgement(reference);
        if (!rq.CanJudge) return Reject(Verdict.Inconclusive, "REFERENCE_" + rq.Code, rq.Reason);
        var aq = WaveTimeQuality.ValidateForJudgement(actual);
        if (!aq.CanJudge) return Reject(Verdict.Inconclusive, "ACTUAL_" + aq.Code, aq.Reason);
        if (reference.Samples.Length < 2 || actual.Samples.Length < 2)
            return Reject(Verdict.Inconclusive, "INSUFFICIENT_SAMPLES", "At least two points required.");
        if (rq.MaximumIntervalSeconds > rq.MedianIntervalSeconds * config.MaximumGapFactor + 1e-9 ||
            aq.MaximumIntervalSeconds > rq.MedianIntervalSeconds * config.MaximumGapFactor + 1e-9)
            return Reject(Verdict.Inconclusive, "MISSING_SAMPLES",
                "A sampling interval exceeds the reference-based gap limit.");

        if (config.InterpolationMode == WaveInterpolationMode.StepHold &&
            (reference.Samples[^1].Sample.Value != reference.Samples[^2].Sample.Value ||
             actual.Samples[^1].Sample.Value != actual.Samples[^2].Sample.Value))
            return Reject(Verdict.Inconclusive, "UNCONFIRMED_TERMINAL_STATE",
                "A final discrete state transition has no subsequent observation interval.");

        var referenceTime = RelativeSeconds(reference);
        var actualTime = RelativeSeconds(actual);
        var best = SelectAlignment(reference, referenceTime, actual, actualTime, config);
        if (best is null || best.Coverage + 1e-12 < config.MinimumCoverage ||
            best.CoveredDuration + 1e-12 < config.MinimumCoveredDurationSeconds)
            return Reject(Verdict.Inconclusive, "INSUFFICIENT_COVERAGE",
                "Not enough reference points/time span are supported by real observed samples.");
        var pass = best.PassRatio + 1e-12 >= config.MinimumPassRatio &&
            best.OutsideDuration <= config.MaximumOutOfToleranceSeconds + 1e-12;
        var code = pass ? "PASS" : "WAVE_OUT_OF_TOLERANCE";
        return new(pass ? Verdict.Pass : Verdict.Fail, code,
            pass ? "Waveform is within the configured tolerance, coverage and duration limits."
                 : "Measured deviations exceed the value or duration limits.",
            best.Coverage, best.PassRatio, best.Shift, best.Rmse,
            best.MaxError, best.OutsideDuration, reference.Samples.Length,
            actual.Samples.Length, best.Compared);
    }

    // UTC deltas are used ONLY as checked elapsed-time coordinates. Opaque monotonic counters
    // are checked for ordering, never misinterpreted as microseconds or Stopwatch ticks.
    private static double[] RelativeSeconds(WaveSeries series) =>
        series.Samples.Select(s => (s.Sample.Time.Utc - series.Origin.Utc).TotalSeconds).ToArray();

    private static Evaluation? SelectAlignment(WaveSeries reference, double[] rt,
        WaveSeries actual, double[] at, WaveComparisonConfig config)
    {
        Evaluation? best = null;
        var limit = config.MaximumShiftSeconds;
        void Consider(double shift)
        {
            var candidate = config.InterpolationMode == WaveInterpolationMode.StepHold
                ? EvaluateStepHold(reference, rt, actual, at, shift, config)
                : Evaluate(reference, rt, actual, at, shift, config);
            if (candidate.Compared == 0 || candidate.Coverage + 1e-12 < config.MinimumCoverage ||
                candidate.CoveredDuration + 1e-12 < config.MinimumCoveredDurationSeconds) return;
            var score = candidate.Rmse + (1 - candidate.Coverage) * Math.Max(config.AbsoluteTolerance, 0.001);
            var oldScore = best is null ? double.PositiveInfinity :
                best.Rmse + (1 - best.Coverage) * Math.Max(config.AbsoluteTolerance, 0.001);
            if (best is null || score < oldScore - 1e-12 ||
                (Math.Abs(score - oldScore) <= 1e-12 &&
                 (candidate.Coverage > best.Coverage + 1e-12 ||
                  (Math.Abs(candidate.Coverage - best.Coverage) <= 1e-12 &&
                   Math.Abs(shift) < Math.Abs(best.Shift)))))
                best = candidate;
        }
        Consider(0);
        if (limit == 0) return best;

        // 同时检查首尾时间戳对应的精确偏移，避免网格遗漏有效对齐。
        var startShift = at[0] - rt[0];
        if (Math.Abs(startShift) <= limit + 1e-12)
            Consider(Math.Clamp(startShift, -limit, limit));

        var endShift = at[^1] - rt[^1];
        if (Math.Abs(endShift) <= limit + 1e-12)
            Consider(Math.Clamp(endShift, -limit, limit));

        // 保留原有固定网格搜索。
        for (var index = -32; index <= 32; index++)
            Consider(limit * index / 32);
        var halfWidth = limit / 32;
        for (var level = 0; level < 3 && best is not null; level++)
        {
            var center = best.Shift;
            for (var index = -8; index <= 8; index++)
                Consider(Math.Clamp(center + halfWidth * index / 8, -limit, limit));
            halfWidth /= 8;
        }
        return best;
    }

    private static Evaluation Evaluate(WaveSeries reference, double[] rt, WaveSeries actual,
        double[] at, double shift, WaveComparisonConfig config)
    {
        var count = 0;
        var passed = 0;
        var squared = 0.0;
        var max = 0.0;
        var outside = 0.0;
        var coveredDuration = 0.0;
        var previousCovered = -1;
        for (var i = 0; i < rt.Length; i++)
        {
            if (!Interpolate(at, actual, rt[i] + shift, out var actualValue)) continue;
            if (previousCovered == i - 1 && previousCovered >= 0)
                coveredDuration += rt[i] - rt[i - 1];
            previousCovered = i;
            count++;
            var referenceValue = reference.Samples[i].Sample.Value;
            var delta = Math.Abs(actualValue - referenceValue);
            var allowed = config.AbsoluteTolerance + Math.Abs(referenceValue) * config.RelativeTolerance;
            squared += delta * delta;
            max = Math.Max(max, delta);
            if (delta <= allowed + 1e-12) passed++;
            else if (i + 1 < rt.Length) outside += rt[i + 1] - rt[i];
        }
        return new Evaluation(shift, count, passed, squared, max, outside, coveredDuration,
            count / (double)rt.Length);
    }

    /// <summary>
    /// Piecewise-constant state comparison over the union of REAL observed
    /// transition timestamps. Looking at reference sample instants alone
    /// can miss short DUT state glitches between those instants.
    /// </summary>
    private static Evaluation EvaluateStepHold(WaveSeries reference, double[] rt,
        WaveSeries actual, double[] at, double shift, WaveComparisonConfig config)
    {
        var begin = Math.Max(rt[0], at[0] - shift);
        var end = Math.Min(rt[^1], at[^1] - shift);
        var referenceDuration = rt[^1] - rt[0];
        if (end - begin <= 1e-12 || referenceDuration <= 0)
            return new Evaluation(shift, 0, 0, 0, 0, 0, 0, 0);

        var boundaries = new List<double>(rt.Length + at.Length + 2) { begin, end };
        boundaries.AddRange(rt.Where(t => t > begin && t < end));
        boundaries.AddRange(at.Select(t => t - shift)
            .Where(t => t > begin && t < end));
        boundaries.Sort();
        var count = 0;
        var passed = 0;
        var outside = 0.0;
        var passingSeconds = 0.0;
        var errorSeconds = 0.0;
        var max = 0.0;
        var duration = 0.0;
        for (var index = 1; index < boundaries.Count; index++)
        {
            var from = boundaries[index - 1];
            var to = boundaries[index];
            var seconds = to - from;
            if (seconds <= 1e-12) continue;
            var midpoint = from + seconds / 2;
            var referenceValue = HeldValue(rt, reference, midpoint);
            var actualValue = HeldValue(at, actual, midpoint + shift);
            var difference = Math.Abs(actualValue - referenceValue);
            var tolerance = config.AbsoluteTolerance +
                Math.Abs(referenceValue) * config.RelativeTolerance;
            duration += seconds;
            errorSeconds += difference * difference * seconds;
            max = Math.Max(max, difference);
            count++;
            if (difference <= tolerance + 1e-12)
            {
                passed++;
                passingSeconds += seconds;
            }
            else outside += seconds;
        }
        return new Evaluation(shift, count, passed, errorSeconds, max,
            outside, duration, duration / referenceDuration,
            duration, passingSeconds);
    }

    private static double HeldValue(double[] times, WaveSeries series, double at)
    {
        var index = Array.BinarySearch(times, at);
        if (index < 0) index = ~index - 1;
        index = Math.Clamp(index, 0, series.Samples.Length - 1);
        return series.Samples[index].Sample.Value;
    }

    private static bool Interpolate(double[] times, WaveSeries data, double at, out double value)
    {
        value = 0;
        if (at < times[0] - 1e-12 || at > times[^1] + 1e-12) return false;
        var index = Array.BinarySearch(times, at);
        if (index >= 0) { value = data.Samples[index].Sample.Value; return true; }
        var high = ~index;
        if (high == 0) { value = data.Samples[0].Sample.Value; return true; }
        if (high == times.Length) { value = data.Samples[^1].Sample.Value; return true; }
        var low = high - 1;
        var portion = (at - times[low]) / (times[high] - times[low]);
        value = data.Samples[low].Sample.Value +
            (data.Samples[high].Sample.Value - data.Samples[low].Sample.Value) * portion;
        return double.IsFinite(value);
    }
}
