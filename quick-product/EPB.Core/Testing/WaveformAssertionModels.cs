using EPB.Core.Configuration;

namespace EPB.Core.Testing;

public sealed record WaveformSample(double TimeMs, double Value);

public sealed class WaveformCompareRule
{
    public double AbsoluteTolerance { get; init; }
    /// <summary>Fractional tolerance. For example, 0.05 means 5%.</summary>
    public double PercentTolerance { get; init; }
    public double TimeShiftToleranceMs { get; init; }
    public double AllowedOutlierRatio { get; init; }
    public int MinimumSampleCount { get; init; } = 2;
}

public sealed class WaveformCompareResult
{
    public bool Passed { get; init; }
    public string Reason { get; init; } = "";
    public double MaxDeviation { get; init; }
    public int OutOfToleranceCount { get; init; }
    public int TotalCompared { get; init; }
    public double? FirstFailureTimeMs { get; init; }
    public double? FirstFailureActualValue { get; init; }
    public double? FirstFailureExpectedValue { get; init; }
    public double? FirstFailureTolerance { get; init; }
}

public sealed class WaveformCompareService
{
    public WaveformCompareResult Compare(
        IReadOnlyList<StandardWaveformPoint> standard,
        IReadOnlyList<WaveformSample> actual,
        WaveformCompareRule rule)
    {
        ArgumentNullException.ThrowIfNull(standard);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(rule);

        var reference = standard
            .Where(x => double.IsFinite(x.TimeMs) && double.IsFinite(x.Value))
            .OrderBy(x => x.TimeMs)
            .ToArray();
        if (reference.Length == 0)
            return Fail("未配置标准波形。");

        var samples = actual
            .Where(x => double.IsFinite(x.TimeMs) && double.IsFinite(x.Value))
            .OrderBy(x => x.TimeMs)
            .ToArray();
        if (samples.Length == 0)
            return Fail("未采集到波形样本。");

        var minimum = Math.Max(1, rule.MinimumSampleCount);
        var allowedOutlierRatio = ClampFraction(rule.AllowedOutlierRatio);
        var absoluteTolerance = Math.Max(0, double.IsFinite(rule.AbsoluteTolerance) ? rule.AbsoluteTolerance : 0);
        var percentTolerance = Math.Max(0, double.IsFinite(rule.PercentTolerance) ? rule.PercentTolerance : 0);
        var timeShiftTolerance = Math.Max(0, double.IsFinite(rule.TimeShiftToleranceMs) ? rule.TimeShiftToleranceMs : 0);

        var compared = 0;
        var outOfTolerance = 0;
        var maxDeviation = 0d;
        FailurePoint? firstFailure = null;

        foreach (var sample in samples)
        {
            if (!TryFindBestExpected(reference, sample.TimeMs, sample.Value, timeShiftTolerance, out var expected))
                continue;

            compared++;
            var tolerance = absoluteTolerance + Math.Abs(expected.Value) * percentTolerance;
            var deviation = Math.Abs(sample.Value - expected.Value);
            if (deviation > maxDeviation)
                maxDeviation = deviation;

            if (deviation <= tolerance)
                continue;

            outOfTolerance++;
            firstFailure ??= new FailurePoint(sample.TimeMs, sample.Value, expected.Value, deviation, tolerance);
        }

        if (compared < minimum)
            return Fail($"波形样本不足：需要至少 {minimum} 个可比较点，实际 {compared} 个。", maxDeviation, outOfTolerance, compared, firstFailure);

        var ratio = compared == 0 ? 1d : (double)outOfTolerance / compared;
        if (ratio > allowedOutlierRatio)
        {
            var first = firstFailure;
            var detail = first is null
                ? $"波形超差：超差 {outOfTolerance}/{compared} 点。"
                : $"波形超差：首个超差点 {first.TimeMs:0.###} ms，实际 {first.Actual:0.###}，标准 {first.Expected:0.###}，偏差 {first.Deviation:0.###}，容差 {first.Tolerance:0.###}；超差 {outOfTolerance}/{compared} 点。";
            return Fail(detail, maxDeviation, outOfTolerance, compared, firstFailure);
        }

        return new WaveformCompareResult
        {
            Passed = true,
            Reason = $"波形判定通过：比较 {compared} 点，最大偏差 {maxDeviation:0.###}，超差 {outOfTolerance} 点。",
            MaxDeviation = maxDeviation,
            OutOfToleranceCount = outOfTolerance,
            TotalCompared = compared
        };
    }

    private static WaveformCompareResult Fail(
        string reason,
        double maxDeviation = 0,
        int outOfToleranceCount = 0,
        int totalCompared = 0,
        FailurePoint? firstFailure = null)
        => new()
        {
            Passed = false,
            Reason = reason,
            MaxDeviation = maxDeviation,
            OutOfToleranceCount = outOfToleranceCount,
            TotalCompared = totalCompared,
            FirstFailureTimeMs = firstFailure?.TimeMs,
            FirstFailureActualValue = firstFailure?.Actual,
            FirstFailureExpectedValue = firstFailure?.Expected,
            FirstFailureTolerance = firstFailure?.Tolerance
        };

    private static bool TryFindBestExpected(
        IReadOnlyList<StandardWaveformPoint> reference,
        double timeMs,
        double actualValue,
        double timeShiftToleranceMs,
        out (double TimeMs, double Value) expected)
    {
        expected = default;
        var candidates = new List<double> { timeMs };
        if (timeShiftToleranceMs > 0)
        {
            var start = timeMs - timeShiftToleranceMs;
            var end = timeMs + timeShiftToleranceMs;
            candidates.Add(start);
            candidates.Add(end);
            foreach (var point in reference)
                if (point.TimeMs >= start && point.TimeMs <= end)
                    candidates.Add(point.TimeMs);
        }

        var found = false;
        var bestDeviation = double.PositiveInfinity;
        foreach (var candidate in candidates.Distinct())
        {
            if (!TryInterpolate(reference, candidate, out var value))
                continue;

            var deviation = Math.Abs(actualValue - value);
            if (deviation >= bestDeviation)
                continue;

            bestDeviation = deviation;
            expected = (candidate, value);
            found = true;
        }

        return found;
    }

    private static bool TryInterpolate(IReadOnlyList<StandardWaveformPoint> reference, double timeMs, out double value)
    {
        value = 0;
        if (reference.Count == 0)
            return false;
        if (reference.Count == 1)
        {
            value = reference[0].Value;
            return true;
        }
        if (timeMs < reference[0].TimeMs || timeMs > reference[^1].TimeMs)
            return false;

        for (var i = 1; i < reference.Count; i++)
        {
            var previous = reference[i - 1];
            var next = reference[i];
            if (timeMs > next.TimeMs)
                continue;

            if (Math.Abs(next.TimeMs - previous.TimeMs) < double.Epsilon)
            {
                value = next.Value;
                return true;
            }

            var ratio = (timeMs - previous.TimeMs) / (next.TimeMs - previous.TimeMs);
            value = previous.Value + (next.Value - previous.Value) * ratio;
            return true;
        }

        return false;
    }

    private static double ClampFraction(double value)
        => !double.IsFinite(value) ? 0 : Math.Clamp(value, 0, 1);

    private sealed record FailurePoint(double TimeMs, double Actual, double Expected, double Deviation, double Tolerance);
}

public enum WaveformSampleReadStatus
{
    Succeeded,
    SourceNotConfigured,
    NoSamples,
    InsufficientSamples
}

public sealed class WaveformSampleReadResult
{
    private WaveformSampleReadResult(
        WaveformSampleReadStatus status,
        IReadOnlyList<WaveformSample> samples,
        string? error)
    {
        Status = status;
        Samples = samples;
        Error = error;
    }

    public WaveformSampleReadStatus Status { get; }
    public IReadOnlyList<WaveformSample> Samples { get; }
    public string? Error { get; }

    public static WaveformSampleReadResult Success(IReadOnlyList<WaveformSample> samples)
        => new(WaveformSampleReadStatus.Succeeded, samples, null);

    public static WaveformSampleReadResult SourceNotConfigured(string error)
        => new(WaveformSampleReadStatus.SourceNotConfigured, [], error);

    public static WaveformSampleReadResult NoSamples(string error)
        => new(WaveformSampleReadStatus.NoSamples, [], error);

    public static WaveformSampleReadResult InsufficientSamples(IReadOnlyList<WaveformSample> samples, string error)
        => new(WaveformSampleReadStatus.InsufficientSamples, samples, error);
}

public interface IWaveformAssertionSource
{
    Task<WaveformSampleReadResult> ReadSamplesAsync(
        WaveformAssertionParameters assertion,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}

public sealed record WaveformAssertionEvaluationResult(
    bool Passed,
    string Reason,
    WaveformCompareResult CompareResult);

public interface IWaveformAssertionEvaluator
{
    WaveformAssertionEvaluationResult Evaluate(
        WaveformAssertionParameters assertion,
        IReadOnlyList<WaveformSample> samples);
}

public sealed class WaveformAssertionEvaluator : IWaveformAssertionEvaluator
{
    private readonly WaveformCompareService _compareService;

    public WaveformAssertionEvaluator(WaveformCompareService? compareService = null)
        => _compareService = compareService ?? new WaveformCompareService();

    public WaveformAssertionEvaluationResult Evaluate(
        WaveformAssertionParameters assertion,
        IReadOnlyList<WaveformSample> samples)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(samples);

        var result = _compareService.Compare(
            assertion.StandardWaveform.Points.ToArray(),
            samples,
            CreateCompareRule(assertion.Rule));
        return new WaveformAssertionEvaluationResult(result.Passed, result.Reason, result);
    }

    public static WaveformCompareRule CreateCompareRule(WaveformAssertionRule rule)
        => new()
        {
            AbsoluteTolerance = NonNegative(rule.AbsoluteTolerance),
            PercentTolerance = NonNegative(rule.PercentTolerance) / 100d,
            TimeShiftToleranceMs = NonNegative(rule.TimeShiftToleranceMs),
            AllowedOutlierRatio = NonNegative(rule.AllowedOutlierPercent) / 100d,
            MinimumSampleCount = Math.Max(1, rule.MinimumSampleCount)
        };

    private static double NonNegative(double value)
        => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
