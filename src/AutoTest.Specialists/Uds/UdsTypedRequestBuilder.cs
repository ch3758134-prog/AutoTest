using System.Collections.Immutable;

namespace AutoTest.Specialists.Uds;

/// <summary>
/// Service-aware request composition. This layer only produces UDS PDU bytes.
/// It has no device handle, authorization, CAN transmit or ECU secrets.
/// Physical transmission still requires the independent Device gate.
/// </summary>
public sealed record UdsPreparedRequest(UdsServiceDescriptor Service,
    ImmutableArray<byte> Pdu, string ParameterSummary)
{
    public UdsRiskLevel Risk => Service.Risk;
    public bool IsReadOnly => Risk == UdsRiskLevel.ReadOnly;
    public string PduHex => Convert.ToHexString(Pdu.AsSpan());
}

public static class UdsTypedRequestBuilder
{
    private const int MaxPdu = 4095;
    private static UdsPreparedRequest Wrap(string summary, byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxPdu)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Classic ISO-TP PDU must be 1..4095 bytes.");
        var descriptor = new UdsServiceCatalog().Get(bytes[0]);
        if (bytes.Length < descriptor.MinimumRequestLength)
            throw new ArgumentException("Missing mandatory UDS service parameters.");
        return new(descriptor, bytes.ToImmutableArray(), summary);
    }
    private static void CheckSubfunction(byte sub)
    {
        if (sub is 0 or > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(sub), "Specify an unsuppressed nonzero subfunction.");
    }
    private static byte High(ushort value) => (byte)(value >> 8);
    private static byte Low(ushort value) => (byte)value;

    public static UdsPreparedRequest ReadDataByIdentifier(params ushort[] dids)
    {
        ArgumentNullException.ThrowIfNull(dids);
        if (dids.Length is < 1 or > 2047)
            throw new ArgumentOutOfRangeException(nameof(dids), "At least one DID pair is required.");
        var bytes = new byte[1 + dids.Length * 2];
        bytes[0] = 0x22;
        for (int i=0; i<dids.Length; i++)
        {
            bytes[1+i*2] = High(dids[i]);
            bytes[2+i*2] = Low(dids[i]);
        }
        return Wrap("DIDs: "+string.Join(", ",dids.Select(x=>$"0x{x:X4}")),bytes);
    }
    public static UdsPreparedRequest ReadDtcByStatus(byte mask) =>
        Wrap($"ReportDTCByStatusMask 0x{mask:X2}",[0x19,0x02,mask]);
    public static UdsPreparedRequest SessionControl(byte session)
    {
        CheckSubfunction(session);
        return Wrap($"Session=0x{session:X2}",[0x10,session]);
    }
    public static UdsPreparedRequest EcuReset(byte mode)
    {
        CheckSubfunction(mode);
        return Wrap($"ResetMode=0x{mode:X2}",[0x11,mode]);
    }
    public static UdsPreparedRequest TesterPresent() => Wrap("TesterPresent 0x00",[0x3E,0]);
    public static UdsPreparedRequest SecuritySeedRequest(byte oddSubfunction)
    {
        if (oddSubfunction is 0 or > 0x7F || (oddSubfunction & 1)==0)
            throw new ArgumentOutOfRangeException(nameof(oddSubfunction),"Security seed levels must use an odd non-suppressed subfunction.");
        return Wrap($"SecurityAccess RequestSeed level=0x{oddSubfunction:X2}",[0x27,oddSubfunction]);
    }
    public static UdsPreparedRequest WriteDataByIdentifier(ushort did,ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) throw new ArgumentException("WriteDataByIdentifier requires a nonempty data record.");
        if (value.Length > MaxPdu-3) throw new ArgumentOutOfRangeException(nameof(value));
        var bytes = new byte[value.Length+3];
        bytes[0]=0x2E; bytes[1]=High(did); bytes[2]=Low(did);
        value.CopyTo(bytes.AsSpan(3));
        return Wrap($"Write DID 0x{did:X4}, {value.Length} bytes",bytes);
    }
    public static UdsPreparedRequest RoutineControl(byte control,ushort routineId,
        ReadOnlySpan<byte> optionalRecord=default)
    {
        if (control is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(control));
        if (optionalRecord.Length > MaxPdu-4) throw new ArgumentOutOfRangeException(nameof(optionalRecord));
        var bytes=new byte[4+optionalRecord.Length];
        bytes[0]=0x31;bytes[1]=control;bytes[2]=High(routineId);bytes[3]=Low(routineId);
        optionalRecord.CopyTo(bytes.AsSpan(4));
        return Wrap($"Routine 0x{routineId:X4} Control={control}",bytes);
    }
    public static UdsPreparedRequest RequestDownload(uint start,uint length,byte dataFormat=0,
        byte addressBytes=4,byte sizeBytes=4)
    {
        if (length==0 || addressBytes is < 1 or > 4 || sizeBytes is < 1 or > 4)
            throw new ArgumentException("Nonzero length and 1..4 byte address/size required.");
        if ((ulong)start+length>0x1_0000_0000UL)
            throw new ArgumentOutOfRangeException(nameof(length),"Download exceeds 32-bit address range.");
        if ((addressBytes<4 && start >= (1UL << (addressBytes*8))) ||
            (sizeBytes<4 && length >= (1UL << (sizeBytes*8))))
            throw new ArgumentException("Selected ALFID cannot represent address or size.");
        var bytes=new byte[3+addressBytes+sizeBytes];
        bytes[0]=0x34;bytes[1]=dataFormat;bytes[2]=(byte)((sizeBytes<<4)|addressBytes);
        for(int i=0;i<addressBytes;i++)bytes[3+i]=(byte)(start>>(8*(addressBytes-i-1)));
        for(int i=0;i<sizeBytes;i++)bytes[3+addressBytes+i]=(byte)(length>>(8*(sizeBytes-i-1)));
        return Wrap($"RequestDownload address=0x{start:X8}, length={length}, ALFID=0x{bytes[2]:X2}",bytes);
    }
    public static UdsPreparedRequest TransferData(byte counter,ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > MaxPdu-2) throw new ArgumentOutOfRangeException(nameof(data));
        var bytes=new byte[data.Length+2];bytes[0]=0x36;bytes[1]=counter;
        data.CopyTo(bytes.AsSpan(2));
        return Wrap($"TransferData BSC=0x{counter:X2}, data={data.Length} bytes",bytes);
    }
    public static UdsPreparedRequest TransferExit(ReadOnlySpan<byte> optionalRecord=default)
    {
        if (optionalRecord.Length>MaxPdu-1) throw new ArgumentOutOfRangeException(nameof(optionalRecord));
        var bytes=new byte[1+optionalRecord.Length];bytes[0]=0x37;
        optionalRecord.CopyTo(bytes.AsSpan(1));
        return Wrap($"TransferExit {optionalRecord.Length} option bytes",bytes);
    }
}
