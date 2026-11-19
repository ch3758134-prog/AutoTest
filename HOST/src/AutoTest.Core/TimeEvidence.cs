namespace AutoTest.Core;

public enum TimeSource { HostArrival, Replay, Simulation, DeviceSample }
public enum TimeQuality { Unknown, HostArrivalOnly, ReplayDerived, Simulated, SynchronizedHardware }

public readonly record struct EvidenceRef(Guid Value)
{
    public static EvidenceRef New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

// Monotonic ticks are opaque source-local counters, NOT UTC ticks or microseconds.
public readonly record struct TimePoint(DateTimeOffset Utc, long MonotonicTicks,
    TimeSource Source, TimeQuality Quality)
{
    public static TimePoint FromHostArrival(DateTimeOffset utc, long ticks) =>
        new(utc, ticks, TimeSource.HostArrival, TimeQuality.HostArrivalOnly);
    public static TimePoint FromReplay(DateTimeOffset utc, long ticks) =>
        new(utc, ticks, TimeSource.Replay, TimeQuality.ReplayDerived);
    public static TimePoint FromSimulation(DateTimeOffset utc, long ticks) =>
        new(utc, ticks, TimeSource.Simulation, TimeQuality.Simulated);
}
