namespace AutoTest.Devices.Usb;
public static class MainControllerCodec
{
    public const byte Ping = 0xF0, CanSend = 0x40, CanRxIndication = 0x41, SetIgn = 0x12;
    public static byte[] EncodePing(ushort sequence) => UsbWireCodec.EncodeRequest(sequence,Ping,ReadOnlySpan<byte>.Empty);
    public static byte[] EncodeCan(ushort sequence,ReadOnlySpan<byte> payload)
    {
        if(payload.Length!=15) throw new ArgumentException("CAN USB payload must contain 15 bytes.",nameof(payload));
        return UsbWireCodec.EncodeRequest(sequence,CanSend,payload);
    }
    public static byte[] EncodeIgn(ushort sequence,bool on) => UsbWireCodec.EncodeRequest(sequence,SetIgn,new byte[]{on?(byte)1:(byte)0});
    // Fixture command needs confirmed firmware command mapping and a separately authorized physical route.
}
