namespace AutoTest.Devices.Usb;
public static class MainControllerCodec
{
    // Main_Control docs/interfaces/USB_HOST_CONTROL_INTERFACE.md v0.2.
    public const byte Ping = 0xF0, GetVersion = 0xF1;
    public const byte CanSend = 0x40, CanRxIndication = 0x41;
    public const byte SetIgn = 0x12, GetIgn = 0x13;
    public const byte SetClamp = 0x18, GetClamps = 0x19;
    public const string ClampContractVersion = "EPB_TEST_CTRL v0.4-dev";
    public static byte[] EncodePing(ushort sequence) => UsbWireCodec.EncodeRequest(sequence,Ping,ReadOnlySpan<byte>.Empty);
    public static byte[] EncodeCan(ushort sequence,ReadOnlySpan<byte> payload)
    {
        if(payload.Length!=15) throw new ArgumentException("CAN USB payload must contain 15 bytes.",nameof(payload));
        return UsbWireCodec.EncodeRequest(sequence,CanSend,payload);
    }
    public static byte[] EncodeIgn(ushort sequence,bool on) => UsbWireCodec.EncodeRequest(sequence,SetIgn,new byte[]{on?(byte)1:(byte)0});
    public static byte[] EncodeClamp(ushort sequence, byte clamp, bool press)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        return UsbWireCodec.EncodeRequest(sequence, SetClamp, new byte[] { clamp, press ? (byte)1 : (byte)0 });
    }
    // GPIO readback is not a physical clamp-position or valve-voltage measurement.
}
