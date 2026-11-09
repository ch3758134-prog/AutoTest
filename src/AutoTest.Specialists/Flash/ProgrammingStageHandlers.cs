using System.Collections.Immutable;
using System.Security.Cryptography;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;

namespace AutoTest.Specialists.Flash;

// Test-only UDS sequence. Session/seed/hash routine semantics are explicitly
// defined by MockBootloader, not inferred as a universal ECU flashing protocol.
public sealed class ProgrammingStageHandlers(UdsClient uds,UdsEndpointProfile endpoint,
    ProgrammingEvidence evidence)
{
    private async ValueTask<byte[]> ExpectAsync(byte[] pdu,byte expectedSid,CancellationToken ct)
    {
        var correlation=Guid.NewGuid();
        bool sensitive=pdu[0] is 0x27 or 0x29 or 0x36;
        string rawRequest=sensitive ? "[REDACTED]" : Convert.ToHexString(pdu);
        evidence.RecordStage("UDS","TX",0,
            $"CorrelationId={correlation:D}; SID=0x{pdu[0]:X2}; RequestHex={rawRequest}; MOCK");
        var result=await uds.RequestAsync(new(pdu.ToImmutableArray(),correlation),endpoint,ct).ConfigureAwait(false);
        string responseHex=sensitive ? "[REDACTED]" :
            (result.ResponsePdu.IsDefaultOrEmpty ? "" : Convert.ToHexString(result.ResponsePdu.ToArray()));
        evidence.RecordStage("UDS","RX",0,
            $"CorrelationId={correlation:D}; Outcome={result.Outcome}; NRC="+
            (result.NegativeResponseCode?.ToString("X2") ?? "NONE")+
            "; ResponseHex="+responseHex+"; MOCK");
        if (result.Outcome != Outcome.Accepted || result.ResponsePdu.IsDefaultOrEmpty
            || result.ResponsePdu[0] != expectedSid)
            throw new ProgrammingRejectedException($"UDS 0x{pdu[0]:X2} failed: "+
                (result.NegativeResponseCode?.ToString("X2") ?? result.Outcome.ToString()));
        return result.ResponsePdu.ToArray();
    }
    public async ValueTask EnterSession(CancellationToken ct)
    {
        var result=await ExpectAsync([0x10,0x02],0x50,ct).ConfigureAwait(false);
        if (result.Length < 2 || result[1] != 2) throw new ProgrammingRejectedException("Programming session mismatch.");
        evidence.RecordStage("EnterSession","PASS",0);
    }
    public async ValueTask UnlockMock(CancellationToken ct)
    {
        var response=await ExpectAsync([0x27,0x01],0x67,ct).ConfigureAwait(false);
        if (response.Length != 6 || response[1] != 1) throw new ProgrammingRejectedException("Mock seed malformed.");
        var key=response.Skip(2).Select(x=>(byte)(x^0xA5)).ToArray();
        var answer=await ExpectAsync([0x27,0x02,..key],0x67,ct).ConfigureAwait(false);
        if (answer.Length != 2 || answer[1] != 2) throw new ProgrammingRejectedException("Mock unlock malformed.");
        evidence.RecordStage("UnlockMock","PASS",0,"In-memory algorithm only");
    }
    public async ValueTask<int> Transfer(FirmwareSegment segment,int preferredChunk,
        long confirmedBefore,CancellationToken ct)
    {
        var header=new byte[11]; header[0]=0x34; header[1]=0; header[2]=0x44;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(3,4),segment.Address);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(7,4),(uint)segment.Data.Length);
        var response=await ExpectAsync(header,0x74,ct).ConfigureAwait(false);
        // 0x74 lengthFormatIdentifier: high nibble = bytes specifying blockLength.
        int count=response.Length >= 2 ? response[1] >> 4 : 0;
        if (count is < 1 or > 4 || response.Length != count+2 || (response[1]&0xF)!=0)
            throw new ProgrammingRejectedException("Malformed RequestDownload blockLength response.");
        uint maxBlockLength=0;
        for(int i=0;i<count;i++) maxBlockLength=(maxBlockLength<<8)|response[2+i];
        if (maxBlockLength < 3) throw new ProgrammingRejectedException("BlockLength too small.");
        int chunk=(int)Math.Min((uint)preferredChunk,maxBlockLength-2);
        int written=await new FlashTransferScheduler(uds,endpoint).WriteBlocksAsync(segment,chunk,
            evidence,confirmedBefore,ct).ConfigureAwait(false);
        await ExpectAsync([0x37],0x77,ct).ConfigureAwait(false);
        evidence.RecordStage("TransferExit","PASS",confirmedBefore+written);
        return written;
    }
    public async ValueTask VerifyMock(FirmwareSegment segment,long confirmed,CancellationToken ct)
    {
        byte[] checksum=SHA256.HashData(segment.Data.AsSpan());
        byte[] response=await ExpectAsync([0x31,0x01,0xFF,0x00,..checksum],0x71,ct).ConfigureAwait(false);
        if (response.Length != 4 || response[0] != 0x71 || response[1] != 0x01 ||
            response[2] != 0xFF || response[3] != 0x00)
            throw new ProgrammingRejectedException("Mock verify response mismatch.");
        evidence.RecordStage("VerifyMock","PASS",confirmed,"Hash verified by in-memory mock");
    }
    public async ValueTask ResetMock(long confirmed,CancellationToken ct)
    {
        var response=await ExpectAsync([0x11,0x01],0x51,ct).ConfigureAwait(false);
        if (response.Length!=2 || response[1]!=1) throw new ProgrammingRejectedException("Mock reset response mismatch.");
        evidence.RecordStage("ResetMock","PASS",confirmed);
    }
}
