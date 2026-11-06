using System.Buffers.Binary;
namespace AutoTest.Devices.Usb;

public readonly record struct UsbRequest(ushort Sequence, byte Command, byte[] Payload);
public readonly record struct UsbResponse(ushort Sequence, byte Command, byte Status, byte[] Payload);
public static class UsbWireCodec
{
    public const byte Start = 0xA5, End = 0x5A;
    public const int RequestOverhead = 8, ResponseOverhead = 9;
    public static byte[] EncodeRequest(ushort sequence, byte command, ReadOnlySpan<byte> data)
    {
        if (data.Length > 255) throw new ArgumentOutOfRangeException(nameof(data));
        byte[] frame = new byte[data.Length + RequestOverhead];
        frame[0] = Start;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), sequence);
        frame[3] = command; frame[4] = (byte)data.Length;
        data.CopyTo(frame.AsSpan(5));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5 + data.Length), Crc16(frame.AsSpan(1, data.Length + 4)));
        frame[^1] = End; return frame;
    }
    public static byte[] EncodeResponse(ushort sequence, byte command, byte status, ReadOnlySpan<byte> data)
    {
        if (data.Length > 255) throw new ArgumentOutOfRangeException(nameof(data));
        byte[] frame = new byte[data.Length + ResponseOverhead]; frame[0] = Start;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), sequence);
        frame[3] = command; frame[4] = status; frame[5] = (byte)data.Length;
        data.CopyTo(frame.AsSpan(6));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6 + data.Length), Crc16(frame.AsSpan(1, data.Length + 5)));
        frame[^1] = End; return frame;
    }
    public static bool TryDecodeResponse(ReadOnlySpan<byte> raw, out UsbResponse result)
    {
        result = default;
        if(raw.Length < ResponseOverhead || raw[0] != Start || raw[^1] != End || raw.Length != ResponseOverhead + raw[5]) return false;
        int len = raw[5];
        if(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(6 + len,2)) != Crc16(raw.Slice(1,5 + len))) return false;
        result = new(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(1,2)),raw[3],raw[4],raw.Slice(6,len).ToArray()); return true;
    }
    public static bool TryDecodeRequest(ReadOnlySpan<byte> raw, out UsbRequest result)
    {
        result = default;
        if(raw.Length < RequestOverhead || raw[0] != Start || raw[^1] != End || raw.Length != RequestOverhead + raw[4]) return false;
        int len = raw[4];
        if(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(5 + len,2)) != Crc16(raw.Slice(1,4 + len))) return false;
        result = new(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(1,2)),raw[3],raw.Slice(5,len).ToArray()); return true;
    }
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach(byte b in data) { crc ^= b; for(int i=0;i<8;i++) crc = (ushort)((crc&1)==1 ? (crc>>1)^0xA001 : crc>>1); }
        return crc;
    }
}
