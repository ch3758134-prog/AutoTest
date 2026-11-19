namespace AutoTest.TestFixtures;

// Completely controllable clock source; no claim of hardware timestamps.
public sealed class FakeClock : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _utc;
    private long _ticks;
    public FakeClock(DateTimeOffset? initial = null) =>
        _utc = initial ?? new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() { lock (_sync) return _utc; }
    public override long GetTimestamp() { lock (_sync) return _ticks; }
    public override long TimestampFrequency => 1000;
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_sync) { _utc = _utc.Add(duration); _ticks += (long)duration.TotalMilliseconds; }
    }
}
