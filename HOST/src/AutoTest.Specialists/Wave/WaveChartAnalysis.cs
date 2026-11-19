using System.Collections.Immutable;

namespace AutoTest.Specialists.Wave;

public readonly record struct WaveChartPoint(double Seconds, double Value);
public sealed record WaveCursorReading(double Seconds, double Value, int Index);
public sealed record WaveCursorMeasurement(double StartSeconds, double EndSeconds,
    double DeltaSeconds, double StartValue, double EndValue, double DeltaValue,
    double Minimum, double Maximum);

/// <summary>
/// Stable display-only projection. Never mutates stored capture/evidence samples; value extrema are
/// preserved in the rendering budget. A downsampled curve must never be used as test evidence.
/// </summary>
public static class WaveChartAnalysis
{
    public static ImmutableArray<WaveChartPoint> FromSeries(WaveSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.Samples.Select(x => new WaveChartPoint(
            (x.Sample.Time.Utc - series.Origin.Utc).TotalSeconds, x.Sample.Value))
            .ToImmutableArray();
    }

    public static ImmutableArray<WaveChartPoint> InWindow(IReadOnlyList<WaveChartPoint> points,
        double startSeconds, double endSeconds, int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) ||
            endSeconds <= startSeconds) throw new ArgumentOutOfRangeException(nameof(endSeconds));
        if (maxPoints is < 8 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxPoints));
        var visible = new List<WaveChartPoint>();
        var previous = double.NegativeInfinity;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.Seconds) || !double.IsFinite(point.Value) ||
                point.Seconds <= previous)
                throw new InvalidDataException("Wave display input must be finite, strictly ordered evidence.");
            previous = point.Seconds;
            if (point.Seconds >= startSeconds && point.Seconds <= endSeconds) visible.Add(point);
        }
        if (visible.Count <= maxPoints) return visible.ToImmutableArray();
        // Preserve exact endpoints and bucket extrema; do not blur peaks or sort corrupted input.
        var result = new List<(int Index, WaveChartPoint Point)> { (0, visible[0]) };
        var slots = Math.Max(1, (maxPoints - 2) / 2);
        var inner = visible.Count - 2;
        for (var b = 0; b < slots; b++)
        {
            var from = 1 + (int)((long)b * inner / slots);
            var to = 1 + (int)((long)(b + 1) * inner / slots);
            if (to <= from) continue;
            var low = from;
            var high = from;
            for (var i = from + 1; i < to; i++)
            {
                if (visible[i].Value < visible[low].Value) low = i;
                if (visible[i].Value > visible[high].Value) high = i;
            }
            result.Add((low, visible[low]));
            if (high != low) result.Add((high, visible[high]));
        }
        result.Add((visible.Count - 1, visible[^1]));
        return result.DistinctBy(x => x.Index).OrderBy(x => x.Index)
            .Select(x => x.Point).ToImmutableArray();
    }

    public static WaveCursorReading? Nearest(IReadOnlyList<WaveChartPoint> points, double seconds)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (points.Count == 0) return null;
        var low = 0;
        var high = points.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (points[middle].Seconds < seconds) low = middle + 1;
            else high = middle;
        }
        var index = low >= points.Count ? points.Count - 1 : low;
        if (index > 0 && Math.Abs(points[index - 1].Seconds - seconds) <=
            Math.Abs(points[index].Seconds - seconds)) index--;
        return new WaveCursorReading(points[index].Seconds, points[index].Value, index);
    }

    public static WaveCursorMeasurement? Measure(IReadOnlyList<WaveChartPoint> points,
        double firstSeconds, double secondSeconds)
    {
        ArgumentNullException.ThrowIfNull(points);
        var a = Nearest(points, firstSeconds);
        var b = Nearest(points, secondSeconds);
        if (a is null || b is null) return null;
        var begin = Math.Min(a.Index, b.Index);
        var end = Math.Max(a.Index, b.Index);
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var i = begin; i <= end; i++)
        {
            min = Math.Min(min, points[i].Value);
            max = Math.Max(max, points[i].Value);
        }
        return new WaveCursorMeasurement(a.Seconds, b.Seconds, b.Seconds - a.Seconds,
            a.Value, b.Value, b.Value - a.Value, min, max);
    }
}
