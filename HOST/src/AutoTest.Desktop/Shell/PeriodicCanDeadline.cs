namespace AutoTest.Desktop.Shell;

/// <summary>
/// Pure monotonic periodic scheduling math. A late USB ACK must not cause either
/// accumulated drift or a burst of catch-up writes. No hardware or timer mock.
/// </summary>
public static class PeriodicCanDeadline
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

        var skipped = (nowTicks - next) / periodTicks + 1;
        return new(checked(next + checked(skipped * periodTicks)), skipped);
    }
}
