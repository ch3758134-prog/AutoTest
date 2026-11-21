namespace AutoTest.Core;

/// <summary>
/// Bounded fixed-rate monotonic deadline calculation shared by manual CAN and
/// the real TEST execution adapter. No simulated device or wall-clock timing.
/// </summary>
public static class MonotonicDeadline
{
    public readonly record struct Deadline(long NextTicks, long SkippedPeriods);

    public static Deadline Advance(long previousScheduledTicks, long nowTicks, long periodTicks)
    {
        if (periodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodTicks));
        if (nowTicks < previousScheduledTicks)
            throw new ArgumentOutOfRangeException(nameof(nowTicks));
        var next = checked(previousScheduledTicks + periodTicks);
        if (nowTicks <= next) return new(next, 0);
        var skipped = checked((nowTicks - next) / periodTicks + 1);
        return new(checked(next + checked(skipped * periodTicks)), skipped);
    }
}
