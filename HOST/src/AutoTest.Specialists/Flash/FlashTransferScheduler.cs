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
            var response=await uds.RequestAsync(new(block.ToImmutableArray(),Guid.NewGuid()),endpoint,ct)
                .ConfigureAwait(false);
            if (response.Outcome != Outcome.Accepted || response.ResponsePdu.Length != 2
                || response.ResponsePdu[0] != 0x76 || response.ResponsePdu[1] != counter)
                throw new ProgrammingRejectedException($"TransferData block {counter}: NRC="+
                    (response.NegativeResponseCode?.ToString("X2") ?? response.Outcome.ToString()));
            written+=n; counter++;
            evidence.RecordStage("Transfer","ACK",baseConfirmed+written,$"Block={unchecked((byte)(counter-1))}");
        }
        return written;
    }
}
