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
        // Task.Delay may resume slightly before the previously scheduled tick.
        // The timestamp is monotonic; it is the scheduled deadline that lies
        // in the future. A harmless early wakeup is NOT a device failure.
        if (nowTicks < previousScheduledTicks)
            nowTicks = previousScheduledTicks;
        var next = checked(previousScheduledTicks + periodTicks);
        if (nowTicks <= next) return new(next, 0);
        var skipped = checked((nowTicks - next) / periodTicks + 1);
        return new(checked(next + checked(skipped * periodTicks)), skipped);
    }
}
