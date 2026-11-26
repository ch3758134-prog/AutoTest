using AutoTest.Contracts;

namespace AutoTest.Specialists.Wave;

/// <summary>Explicit comparison semantics. The DBC signal name or plot style
/// must never silently determine test judgement rules.</summary>
public enum WaveInterpolationMode
{
    Linear = 0,
    StepHold = 1
}

/// <summary>All percentages are fractions in [0,1]; time thresholds use seconds.</summary>
public sealed record WaveComparisonConfig(
    double AbsoluteTolerance,
    double RelativeTolerance,
    double MinimumCoverage,
    double MinimumPassRatio,
    double MaximumShiftSeconds,
    double MaximumGapFactor,
    double MaximumOutOfToleranceSeconds,
    double MinimumCoveredDurationSeconds)
{
    /// <summary>High-precision tests may only use a synchronized hardware clock, never host arrival.</summary>
    public bool RequireSynchronizedHardwareTime { get; init; }
    /// <summary>Older pinned wave settings remain Linear until explicitly revised.</summary>
    public WaveInterpolationMode InterpolationMode { get; init; } = WaveInterpolationMode.Linear;

    public static WaveComparisonConfig FromContract(CompareProfile profile) =>
        new(profile.Tolerance, 0, profile.MinimumCoverage, 1, 0.050, 1.6, 0, 0);

    public string? ValidateThresholds()
    {
        if (InterpolationMode is not (WaveInterpolationMode.Linear or WaveInterpolationMode.StepHold))
            return "INVALID_INTERPOLATION_MODE";
        if (!double.IsFinite(AbsoluteTolerance) || AbsoluteTolerance < 0) return "INVALID_ABSOLUTE_TOLERANCE";
        if (!double.IsFinite(RelativeTolerance) || RelativeTolerance < 0) return "INVALID_RELATIVE_TOLERANCE";
        if (!double.IsFinite(MinimumCoverage) || MinimumCoverage <= 0 || MinimumCoverage > 1)
            return "INVALID_COVERAGE";
        if (!double.IsFinite(MinimumPassRatio) || MinimumPassRatio <= 0 || MinimumPassRatio > 1)
            return "INVALID_PASS_RATIO";
        if (!double.IsFinite(MaximumShiftSeconds) || MaximumShiftSeconds < 0 || MaximumShiftSeconds > 10)
            return "INVALID_SHIFT";
        if (!double.IsFinite(MaximumGapFactor) || MaximumGapFactor < 1 || MaximumGapFactor > 100)
            return "INVALID_GAP_FACTOR";
        if (!double.IsFinite(MaximumOutOfToleranceSeconds) ||
            MaximumOutOfToleranceSeconds < 0 || MaximumOutOfToleranceSeconds > 3600)
            return "INVALID_OUTSIDE_DURATION";
        if (!double.IsFinite(MinimumCoveredDurationSeconds) ||
            MinimumCoveredDurationSeconds < 0 || MinimumCoveredDurationSeconds > 3600)
            return "INVALID_COVERED_DURATION";
        return null;
    }
}
