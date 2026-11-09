using System.Diagnostics;

namespace AutoTest.Devices.Diagnostics;

/// <summary>USB ACK/readback are controller-side facts. Physical voltage, fixture position,
/// KEY_SIM actuation and ECU recognition remain UNKNOWN without independent evidence.</summary>
public sealed record HardwareControlTrace(
    DateTimeOffset Utc, long HostTicks, long SessionGeneration,
    string Operation, string Stage, byte UsbCommand, byte? RequestedValue,
    ushort? UsbSequence, byte? UsbStatus, byte? GpioReadback,
    string? UsbCanPayloadHex, string Outcome, string Detail)
{
    public string Kind => "DEVICE_PHYSICAL_CONTROL";
    public string PhysicalObserved => "UNKNOWN";
    public string EcuResponded => "UNKNOWN";
    public string BusinessVerified => "NOT_EVALUATED";

    public static HardwareControlTrace Create(long generation, string operation, string stage,
        byte command, byte? requestedValue, string outcome, string detail,
        ushort? sequence = null, byte? status = null, byte? readback = null,
        string? usbCanPayload = null) => new(
            DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), generation, operation, stage,
            command, requestedValue, sequence, status, readback, usbCanPayload, outcome, detail);
}
