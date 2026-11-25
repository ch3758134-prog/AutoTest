namespace AutoTest.Specialists.Wave;

/// <summary>
/// Rendering/interactive cursor continuity safeguards only. These heuristics
/// MUST NOT authorize TEST verdicts, replace wave quality metadata, or
/// interpolate unknown ECU observations.
/// </summary>
public static class WaveDisplayContinuity
{
    public static double GapLimit(IReadOnlyList<WaveChartPoint> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var intervals = new List<double>(512);
        for (var i = 1; i < raw.Count && intervals.Count < 512; i++)
        {
            var a = raw[i - 1];
            var b = raw[i];
            if (!double.IsFinite(a.Seconds) || !double.IsFinite(b.Seconds)) continue;
            var gap = b.Seconds - a.Seconds;
            if (gap > 0 && double.IsFinite(gap)) intervals.Add(gap);
        }
        if (intervals.Count == 0) return 0.2;
        intervals.Sort();
        // 10-ms source cadence: 60-ms visible gap threshold, NOT the former
        // fixed 200-ms minimum which concealed short ECU loss-of-signal gaps.
        // No arbitrary upper cap: slow genuine signals (e.g. 5-second
        // samples) must not be presented as disconnected on every sample.
        return Math.Max(0.02, intervals[intervals.Count / 2] * 6d);
    }

    public static bool HasDiscontinuities(IReadOnlyList<WaveChartPoint> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var limit = GapLimit(raw);
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            if (!double.IsFinite(current.Seconds) || !double.IsFinite(current.Value))
                return true;
            if (i != 0 &&
                (current.Seconds <= raw[i - 1].Seconds ||
                 current.Seconds - raw[i - 1].Seconds > limit))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A visual range can report Δt, Δy and extrema only when its input is
    /// finite, strictly ordered and free of sample gaps within that range.
    /// Nearest() uses binary search, thus non-monotonic data anywhere rejects.
    /// This is still a display tool, not a production quality/Pass assertion.
    /// </summary>
    public static bool CanMeasure(IReadOnlyList<WaveChartPoint> raw,
        double first, double second)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count < 2 || !double.IsFinite(first) || !double.IsFinite(second))
            return false;
        var left = Math.Min(first, second);
        var right = Math.Max(first, second);
        // A lane may occupy only part of the shared plot's time axis. Do not
        // extrapolate two cursors outside this lane back onto its endpoints.
        if (right <= left || left < raw[0].Seconds ||
            right > raw[^1].Seconds) return false;
        var maximumGap = GapLimit(raw);
        var samplesWithin = 0;
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            if (!double.IsFinite(current.Seconds) || !double.IsFinite(current.Value))
                return false;
            if (current.Seconds >= left && current.Seconds <= right)
                samplesWithin++;
            if (i == 0) continue;
            var previous = raw[i - 1];
            if (current.Seconds <= previous.Seconds) return false;
            if (previous.Seconds <= right && current.Seconds >= left &&
                current.Seconds - previous.Seconds > maximumGap)
                return false;
        }
        return samplesWithin >= 2;
    }
}
