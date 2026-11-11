namespace AutoTest.Desktop.Bench;

/// <summary>
/// Best-effort host scheduling after the previous real CAN_SEND acknowledgement.
/// Delayed Windows/USB operations never cause a burst of catch-up transmissions.
/// This is NOT a claim of board-side or hard real-time CAN timing.
/// </summary>
public static class BenchCanSchedule
{
    public static long NextDue(long acknowledgementTicks, long periodTicks)
    {
        if (acknowledgementTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTicks));
        if (periodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodTicks));
        return checked(acknowledgementTicks + periodTicks);
    }
}
