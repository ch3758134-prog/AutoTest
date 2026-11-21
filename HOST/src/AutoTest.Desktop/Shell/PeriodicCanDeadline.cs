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
        var deadline = AutoTest.Core.MonotonicDeadline.Advance(
            previousScheduledTicks, nowTicks, periodTicks);
        return new(deadline.NextTicks, deadline.SkippedPeriods);
    }
}
