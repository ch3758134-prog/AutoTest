using System.Globalization;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

/// <summary>
/// A read-only explanation of existing WAVE evidence. These intervals are observed sampling
/// brackets, NOT inferred exact physical fault onset/clearance times or a second verdict engine.
/// All seconds are relative to the ACTUAL capture origin; raw monotonic counters are never
/// treated as calibrated time units.
/// </summary>
public sealed record WaveDiagnosticIssue(
    string Signal, string Category, string Code,
    double? StartSeconds, double? EndSeconds,
    int AffectedSamples, double? MaximumError, double? AllowedError,
    string FirstSourceEvidenceId, string LastSourceEvidenceId,
    string ReferenceEvidenceId, string Detail)
{
    public string Window => StartSeconds is { } start && EndSeconds is { } end
        ? start.ToString("0.000000", CultureInfo.InvariantCulture) + "–" +
          end.ToString("0.000000", CultureInfo.InvariantCulture) + " s"
        : "UNKNOWN / 无法从现有证据定位";
}

public static class WaveIntervalDiagnostics
{
    private const string None = "UNKNOWN";

    /// <summary>
    /// Locate only intervals bracketed by actual observed samples. A reported lost-frame
    /// flag without a per-sample gap marker is explicitly UNLOCATED; no timestamp is guessed.
    /// </summary>
    public static IReadOnlyList<WaveDiagnosticIssue> FindGaps(WaveSeries series, double gapFactor)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!double.IsFinite(gapFactor) || gapFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(gapFactor));
        var samples = series.Samples;
        var results = new List<WaveDiagnosticIssue>();
        var intervals = new double[Math.Max(samples.Length - 1, 0)];
        var ordered = true;
        for (var i = 1; i < samples.Length; i++)
        {
            var before = samples[i - 1].Sample.Time;
            var after = samples[i].Sample.Time;
            var elapsed = (after.Utc - before.Utc).TotalSeconds;
            if (!double.IsFinite(elapsed) || elapsed <= 0 ||
                after.MonotonicTicks <= before.MonotonicTicks ||
                before.Source != after.Source || before.Quality != after.Quality)
            {
                ordered = false;
                break;
            }
            intervals[i - 1] = elapsed;
        }
        if (series.NonMonotonic || !ordered)
        {
            results.Add(Unlocated(series, "NON_MONOTONIC", "原始采样顺序无效；不能推算缺样发生时间"));
        }
        else
        {
            var median = intervals.Length == 0 ? 0 : Median(intervals);
            var threshold = median * gapFactor;
            var marked = false;
            for (var i = 0; i < samples.Length; i++)
            {
                var current = samples[i];
                var preceding = i > 0 ? samples[i - 1] : current;
                var reported = current.Sample.Gap;
                var longInterval = i > 0 && median > 0 && intervals[i - 1] > threshold + 1e-9;
                if (!reported && !longInterval) continue;
                if (reported) marked = true;
                var code = reported ? "SOURCE_GAP_AT_SAMPLE" : "SAMPLE_INTERVAL_EXCEEDED";
                results.Add(new WaveDiagnosticIssue(series.Signal.SignalName, "GAP", code,
                    (preceding.Sample.Time.Utc - series.Origin.Utc).TotalSeconds,
                    (current.Sample.Time.Utc - series.Origin.Utc).TotalSeconds,
                    1, null, null, Evidence(preceding.SourceEvidence), Evidence(current.SourceEvidence), None,
                    reported ? "采样源明确报告 Gap；区间仅为相邻已知采样的界限" :
                    $"观察间隔 {intervals[i - 1]:0.######} s 超过中位采样间隔×{gapFactor:0.###} ({threshold:0.######} s)；不能据此断言确切丢帧数量"));
            }
            if (series.HasGap && !marked)
                results.Add(Unlocated(series, "SOURCE_GAP_UNLOCATED", "源报告丢帧，但样本未标出位置；不推断时窗"));
        }
        if (series.Overflow) results.Add(Unlocated(series, "BUFFER_OVERFLOW", "采样容量溢出；丢失区间不可定位"));
        if (series.RejectedEvents > 0 || series.InvalidInput)
            results.Add(Unlocated(series, "INVALID_INPUT", "存在被拒绝或无效输入；具体时间未保存在有效样本序列中"));
        if (series.MixedClock) results.Add(Unlocated(series, "MIXED_CLOCK", "混用时间源或时间质量；不计算定位时窗"));
        if (samples.Length < 2) results.Add(Unlocated(series, "INSUFFICIENT_SAMPLES", "不足两个有效样本，无法计算采样间隔"));
        return results;
    }

    /// <summary>
    /// Explains FAIL using the exact shift/absolute-relative tolerance and interpolation
    /// rules used by WaveComparator. Never produces mismatch windows for INCONCLUSIVE/ERROR,
    /// poor time quality or a different DBC SignalRef.
    /// </summary>
    public static IReadOnlyList<WaveDiagnosticIssue> FindMismatches(
        WaveSeries actual, WaveSeries reference, WaveComparisonConfig config,
        WaveComparisonDetails result)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Verdict != Verdict.Fail || config.ValidateThresholds() is not null ||
            actual.Signal != reference.Signal ||
            !actual.SourceRevision.Matches(reference.SourceRevision) ||
            !WaveTimeQuality.ValidateForJudgement(actual).CanJudge ||
            !WaveTimeQuality.ValidateForJudgement(reference).CanJudge)
            return Array.Empty<WaveDiagnosticIssue>();

        var actualTimes = actual.Samples.Select(x => (x.Sample.Time.Utc - actual.Origin.Utc).TotalSeconds).ToArray();
        var referenceTimes = reference.Samples.Select(x => (x.Sample.Time.Utc - reference.Origin.Utc).TotalSeconds).ToArray();
        var issues = new List<WaveDiagnosticIssue>();
        var first = -1;
        var last = -1;
        var worst = 0d;
        var allowedAtWorst = 0d;
        var firstEvidence = None;
        var lastEvidence = None;
        var peakReferenceEvidence = None;

        void Flush()
        {
            if (first < 0) return;
            issues.Add(new WaveDiagnosticIssue(actual.Signal.SignalName, "MISMATCH",
                "OUT_OF_TOLERANCE_SAMPLED_WINDOW",
                referenceTimes[first] + result.ShiftSeconds,
                referenceTimes[last] + result.ShiftSeconds,
                last - first + 1, worst, allowedAtWorst, firstEvidence, lastEvidence,
                peakReferenceEvidence,
                "比较器对齐后连续参考采样锚点超差；区间不代表物理故障的精确起止时间。"));
            first = -1;
            last = -1;
            worst = 0;
            allowedAtWorst = 0;
            firstEvidence = None;
            lastEvidence = None;
            peakReferenceEvidence = None;
        }

        for (var i = 0; i < referenceTimes.Length; i++)
        {
            var target = referenceTimes[i] + result.ShiftSeconds;
            if (!TryInterpolated(actual, actualTimes, target, out var value, out var lower, out var upper))
            {
                Flush();
                continue;
            }
            var expected = reference.Samples[i].Sample.Value;
            var error = Math.Abs(value - expected);
            var allowed = config.AbsoluteTolerance + Math.Abs(expected) * config.RelativeTolerance;
            if (error <= allowed + 1e-12)
            {
                Flush();
                continue;
            }
            if (first < 0)
            {
                first = i;
                firstEvidence = Evidence(actual.Samples[lower].SourceEvidence);
            }
            last = i;
            lastEvidence = Evidence(actual.Samples[upper].SourceEvidence);
            if (error >= worst)
            {
                worst = error;
                allowedAtWorst = allowed;
                peakReferenceEvidence = Evidence(reference.Samples[i].SourceEvidence);
            }
        }
        Flush();
        return issues;
    }

    private static bool TryInterpolated(WaveSeries series, double[] times, double target,
        out double value, out int lower, out int upper)
    {
        value = 0;
        lower = upper = -1;
        if (times.Length == 0 || target < times[0] - 1e-12 || target > times[^1] + 1e-12)
            return false;
        var exact = Array.BinarySearch(times, target);
        if (exact >= 0)
        {
            lower = upper = exact;
            value = series.Samples[exact].Sample.Value;
            return true;
        }
        upper = ~exact;
        if (upper == 0) upper = lower = 0;
        else if (upper == times.Length) upper = lower = times.Length - 1;
        else lower = upper - 1;
        if (lower == upper) value = series.Samples[lower].Sample.Value;
        else
        {
            var ratio = (target - times[lower]) / (times[upper] - times[lower]);
            value = series.Samples[lower].Sample.Value +
                (series.Samples[upper].Sample.Value - series.Samples[lower].Sample.Value) * ratio;
        }
        return double.IsFinite(value);
    }

    private static WaveDiagnosticIssue Unlocated(WaveSeries series, string code, string reason) =>
        new(series.Signal.SignalName, "QUALITY", code, null, null, 0, null, null,
            None, None, None, reason);

    private static string Evidence(EvidenceRef source) => source.Value == Guid.Empty
        ? None : source.Value.ToString("D");

    private static double Median(double[] intervals)
    {
        var sorted = intervals.ToArray();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
