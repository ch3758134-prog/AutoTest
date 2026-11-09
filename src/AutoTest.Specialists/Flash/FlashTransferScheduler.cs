using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;

namespace AutoTest.Specialists.Flash;

public sealed class FlashTransferScheduler(UdsClient uds, UdsEndpointProfile endpoint)
{
    public async ValueTask<int> WriteBlocksAsync(FirmwareSegment segment,int chunkSize,
        ProgrammingEvidence evidence,long baseConfirmed,CancellationToken ct)
    {
        if (chunkSize is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        byte counter=1; int written=0;
        while(written < segment.Data.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n=Math.Min(chunkSize,segment.Data.Length-written);
            var block=new byte[n+2]; block[0]=0x36; block[1]=counter;
            segment.Data.AsSpan().Slice(written,n).CopyTo(block.AsSpan(2));
            var correlation=Guid.NewGuid();
            evidence.RecordStage("UDS","TX",baseConfirmed+written,
                $"CorrelationId={correlation:D}; SID=0x36; Block={counter}; "+
                $"PayloadBytes={n}; RawHex=[REDACTED_FIRMWARE]; MOCK");
            var response=await uds.RequestAsync(new(block.ToImmutableArray(),correlation),endpoint,ct)
                .ConfigureAwait(false);
            bool accepted=response.Outcome==Outcome.Accepted && response.ResponsePdu.Length==2
                && response.ResponsePdu[0]==0x76 && response.ResponsePdu[1]==counter;
            evidence.RecordStage("UDS","RX",baseConfirmed+written,
                $"CorrelationId={correlation:D}; Block={counter}; Outcome={response.Outcome}; "+
                "NRC="+(response.NegativeResponseCode?.ToString("X2") ?? "NONE")+
                "; ResponseHex="+(response.ResponsePdu.IsDefaultOrEmpty ? "" :
                    Convert.ToHexString(response.ResponsePdu.ToArray()))+"; MOCK");
            if (!accepted)
                throw new ProgrammingRejectedException($"TransferData block {counter}: NRC="+
                    (response.NegativeResponseCode?.ToString("X2") ?? response.Outcome.ToString()));
            written+=n;
            evidence.RecordStage("Transfer","ACK",baseConfirmed+written,
                $"Block={counter}; CorrelationId={correlation:D}; ConfirmedBytes={baseConfirmed+written}; MOCK");
            counter++;
        }
        return written;
    }
}
