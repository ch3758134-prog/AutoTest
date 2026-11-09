namespace AutoTest.Devices.Diagnostics;

/// <summary>USB 0x40 MCU accepted CAN0 send; KEY_SIM has no independent action ACK.</summary>
public sealed record KeypadCommandReceipt(
    DateTimeOffset Utc, long SessionGeneration, uint CompleteKeyMask,
    ushort UsbSequence, byte UsbStatus, string UsbCanPayloadHex)
{
    public string KeyBoardAcknowledgement => "NOT_AVAILABLE";
    public string PhysicalObserved => "UNKNOWN";
    public string EcuResponded => "UNKNOWN";
}
