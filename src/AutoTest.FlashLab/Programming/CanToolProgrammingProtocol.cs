using System.Buffers.Binary;
using System.IO;

namespace AutoTest.FlashLab.Programming;

/// <summary>Exact PDU layout traced to CanTool FlashSequence, with the known original
/// 0x74 and missing-checksum bugs corrected. Pure bytes; NO physical sending.</summary>
public static class CanToolProgrammingProtocol
{
    public static byte[] Erase(uint address,uint length) => AddressRoutine(0x00,address,length);
    public static byte[] Verify(uint address,uint length) => AddressRoutine(0x01,address,length);
    private static byte[] AddressRoutine(byte routine,uint address,uint length)
    {
        CheckRange(address,length);
        byte[] pdu=[0x31,0x01,0xFF,routine,0,0,0,0,0,0,0,0];
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(4,4),address);
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(8,4),length);
        return pdu;
    }
    public static byte[] Download(uint address,uint length)
    {
        CheckRange(address,length);
        byte[] pdu=[0x34,0x00,0x44,0,0,0,0,0,0,0,0];
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(3,4),address);
        BinaryPrimitives.WriteUInt32BigEndian(pdu.AsSpan(7,4),length);
        return pdu;
    }
    private static void CheckRange(uint address,uint length)
    {
        if(length==0 || (ulong)address+length>0x100000000UL)
            throw new ArgumentOutOfRangeException(nameof(length));
    }
    public static uint ParseDownloadMaximum(ReadOnlySpan<byte> response)
    {
        if(response.Length<3 || response[0]!=0x74) throw new InvalidDataException("Missing UDS 74.");

        // On-wire confirmed HydEPB/CYT2B63 BootLoader response, captured at
        // 2026-11-18: 06 74 20 00 00 02 02 AA.
        // Unlike a standard 74 20 + 2-byte max, this OEM encodes 0x0202
        // as FOUR following bytes despite the 0x20 header. The real ECU then
        // receives a 0x0202-byte 36 PDU (2 headers + 0x200 data) successfully.
        // Keep the exception exact; do not accept arbitrary malformed replies.
        if(response.SequenceEqual(new byte[]{0x74,0x20,0x00,0x00,0x02,0x02}))
            return 0x202u;

        // All other profiles: strict standard lengthFormatIdentifier.
        int n=response[1]>>4;
        if(n is < 1 or > 4 || (response[1]&0x0F)!=0 || response.Length!=n+2)
            throw new InvalidDataException("Invalid 74 lengthFormatIdentifier.");
        uint max=0;
        for(int i=0;i<n;i++) max=(max<<8)|response[2+i];
        if(max<3) throw new InvalidDataException("ECU 74 maximum too small.");
        return max; // full 36 PDU length including SID and sequence byte
    }
    public static byte[] Transfer(byte sequence,ReadOnlySpan<byte> bytes)
    {
        if(bytes.IsEmpty || bytes.Length>4093)throw new ArgumentOutOfRangeException(nameof(bytes));
        byte[] pdu=new byte[2+bytes.Length];
        pdu[0]=0x36;pdu[1]=sequence;bytes.CopyTo(pdu.AsSpan(2));return pdu;
    }
    public static ushort ComplementOfSum(ReadOnlySpan<byte> bytes)
    {
        uint sum=0;
        foreach(byte b in bytes) sum+=b;
        return unchecked((ushort)~sum);
    }
    public static void CheckVerify(ReadOnlySpan<byte> response,ushort expected)
    {
        if(response.Length<6 || response[0]!=0x71 || response[1]!=0x01 ||
            response[2]!=0xFF || response[3]!=0x01)
            throw new InvalidDataException("ECU FF01 response has no complete checksum.");
        ushort actual=BinaryPrimitives.ReadUInt16BigEndian(response[^2..]);
        if(actual!=expected)
            throw new InvalidDataException($"FF01 checksum mismatch ECU=0x{actual:X4}, HOST=0x{expected:X4}");
    }
    public static byte[] UdsReset() => [0x11,0x01];
    public static byte[] RequestDownloadExit() => [0x37];
}
