namespace AutoTest.Devices.Diagnostics;

/// <summary>Main controller USB ACK + firmware GPIO readback, not external electrical confirmation.</summary>
public sealed record StationOutputReceipt(
    DateTimeOffset Utc, long SessionGeneration, byte Command, byte RequestedValue,
    ushort AckSequence, byte AckStatus, ushort ReadbackSequence, byte GpioReadback, bool Matches)
{
    public string PhysicalObserved => "UNKNOWN";
}
