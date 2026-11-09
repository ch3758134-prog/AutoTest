using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;

namespace AutoTest.Specialists.Flash;

public sealed class MockProgrammingWorkflow(MockBootloader bootloader) : IProgrammingWorkflow
{
    public TimeSpan ResponseLatency { get; set; } = TimeSpan.Zero;
    public static readonly ImmutableArray<string> StageNames =
        ["EnterSession","UnlockMock","RequestDownload","Transfer","TransferExit","VerifyMock","ResetMock"];
    public ProgrammingPlan BuildPlan(ProgrammingRequest request)
    {
        string content=string.Join('|',request.Station.Value,request.Target,
            request.Image.Id.Value,request.Image.Revision.Value,request.Image.Fingerprint.Sha256,
            request.ProfileRevision.Sha256,string.Join(',',StageNames));
        return new(request,StageNames,RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes(content)));
    }
    public async ValueTask ExecuteAsync(ProgrammingPlan plan,ProgrammingAssets assets,
        ProgrammingEvidence evidence,CancellationToken ct)
    {
        assets.Profile.Validate(); assets.Profile.ValidateImage(assets.Image);
        if (!assets.Environment.IsMock || !assets.Profile.MockOnly)
            throw new ProgrammingRejectedException("Physical programming is not authorized.");
        await using var channel=new MockUdsMessageChannel(bootloader) { ResponseLatency=ResponseLatency };
        var uds=new UdsClient(channel);
        var handler=new ProgrammingStageHandlers(uds,assets.Profile.Endpoint,evidence);
        long confirmed=0;
        await handler.EnterSession(ct).ConfigureAwait(false);
        await handler.UnlockMock(ct).ConfigureAwait(false);
        int segmentIndex=0;
        foreach(var segment in assets.Image.Segments)
        {
            ct.ThrowIfCancellationRequested();
            segmentIndex++;
            string stage=$"Segment_{segmentIndex:D4}";
            evidence.RecordStage(stage,"BEGIN",confirmed,
                $"Address=0x{segment.Address:X8}; EndExclusive=0x{segment.EndExclusive:X8}; "+
                $"Length={segment.Data.Length}; MOCK");
            int written=await handler.Transfer(segment,assets.Profile.MaxTransferDataBytes,confirmed,ct)
                .ConfigureAwait(false);
            confirmed+=written;
            evidence.RecordStage(stage,"TRANSFER_ACKED",confirmed,
                $"Length={written}; TransferData ACK only, not hash verified; MOCK");
            await handler.VerifyMock(segment,confirmed,ct).ConfigureAwait(false);
            evidence.RecordStage(stage,"VERIFIED",confirmed,
                $"Hash verified by in-memory Mock; Length={written}; MOCK");
        }
        await handler.ResetMock(confirmed,ct).ConfigureAwait(false);
        if (confirmed!=assets.Image.ByteCount)
            throw new ProgrammingRejectedException("Confirmed byte count does not match image size.");
    }
}
