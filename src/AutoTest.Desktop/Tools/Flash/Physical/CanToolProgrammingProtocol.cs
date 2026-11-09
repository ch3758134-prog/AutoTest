using System.Buffers.Binary;

namespace AutoTest.Desktop.Tools.Flash.Physical;

/// <summary>
/// Request PDU construction from the supplied CanTool FlashSequence.cs.
/// The actual send path must use the real CAN1 ISO-TP session, never the MockBootloader.
/// This class is side-effect free and does NOT grant any physical transmit permission.
/// </summary>
public static class CanToolProgrammingProtocol
{
    // CanTool erase: SID 31 / startRoutine 01 / RID FF00 / address4 / length4.
    public static byte[] Erase(uint address, uint length)
    {
        if (length == 0 || (ulong)address + length > uint.MaxValue + 1UL)
            throw new ArgumentOutOfRangeException(nameof(length));
        byte[] pdu=new byte[12] { 0x31,0x01,0xFF,0x00,0,0,0,0,0,0,0,0 };
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(4,4),address);
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(8,4),length);
        return pdu;
    }

    // CanTool download: SID 34 / DFI 00 / ALFI 44 / address4 / size4.
    public static byte[] Download(uint address,uint length)
    {
        if (length==0 || (ulong)address+length>uint.MaxValue+1UL)
            throw new ArgumentOutOfRangeException(nameof(length));
        byte[] pdu=new byte[11] {0x34,0x00,0x44,0,0,0,0,0,0,0,0};
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(3,4),address);
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(7,4),length);
        return pdu;
    }

    public static uint ParseDownloadMaximum(ReadOnlySpan<byte> udsResponse)
    {
        if (udsResponse.Length<3 || udsResponse[0]!=0x74)
            throw new InvalidDataException("Missing positive 0x74 RequestDownload response.");
        // ISO 14229: high nibble encodes byte count of maxNumberOfBlockLength.
        int count=udsResponse[1]>>4;
        if (count<1 || count>4 || udsResponse.Length!=count+2 || (udsResponse[1]&0x0F)!=0)
            throw new InvalidDataException("Invalid RequestDownload lengthFormatIdentifier.");
        uint max=0;
        for (int i=0;i<count;i++) max=(max<<8)|udsResponse[i+2];
        if (max<3) throw new InvalidDataException("ECU maximum TransferData block too small.");
        return max;
    }

    public static byte[] Transfer(byte counter,ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty) throw new ArgumentException("Empty TransferData chunk.");
        byte[] pdu=new byte[2+chunk.Length];
        pdu[0]=0x36;pdu[1]=counter;
        chunk.CopyTo(pdu.AsSpan(2));
        return pdu;
    }
    public static byte[] TransferExit() => [0x37];

    public static ushort ComplementOfSum(ReadOnlySpan<byte> data)
    {
        uint checksum=0;
        foreach(byte b in data) checksum+=b;
        return unchecked((ushort)~checksum);
    }
    public static byte[] Verify(uint address,uint length)
    {
        if (length==0 || (ulong)address+length>uint.MaxValue+1UL)
            throw new ArgumentOutOfRangeException(nameof(length));
        byte[] pdu=new byte[12] {0x31,0x01,0xFF,0x01,0,0,0,0,0,0,0,0};
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(4,4),address);
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(8,4),length);
        return pdu;
    }
    public static void CheckVerify(ReadOnlySpan<byte> response,ushort expected)
    {
        if (response.Length<4 || response[0]!=0x71 ||
            response[1]!=0x01 || response[2]!=0xFF || response[3]!=0x01)
            throw new InvalidDataException("Invalid verification routine response.");
        // CanTool's current implementation checks the trailing 16-bit checksum
        // only when returned. Fail closed: real programming MUST confirm it.
        if (response.Length<6)
            throw new InvalidDataException("ECU response lacks checksum; cannot claim verified.");
        ushort actual=BinaryPrimitives.ReadUInt16BigEndian(response[^2..]);
        if (actual!=expected)
            throw new InvalidDataException($"Checksum mismatch ECU={actual:X4}, HOST={expected:X4}.");
    }

    public static IEnumerable<byte[]> TransferBlocks(ReadOnlyMemory<byte> image,
        uint ecuMaxBlockLength,uint configuredBlockLength)
    {
        int size=checked((int)Math.Min(ecuMaxBlockLength-2,configuredBlockLength));
        if (size<=0) throw new ArgumentOutOfRangeException(nameof(ecuMaxBlockLength));
        byte counter=1;
        for(int offset=0;offset<image.Length;offset+=size)
        {
            int n=Math.Min(size,image.Length-offset);
            yield return Transfer(counter,image.Span.Slice(offset,n));
            unchecked { counter++; }
        }
    }
}
