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
        foreach(var segment in assets.Image.Segments)
        {
            ct.ThrowIfCancellationRequested();
            int written=await handler.Transfer(segment,assets.Profile.MaxTransferDataBytes,confirmed,ct)
                .ConfigureAwait(false);
            confirmed+=written;
            await handler.VerifyMock(segment,confirmed,ct).ConfigureAwait(false);
        }
        await handler.ResetMock(confirmed,ct).ConfigureAwait(false);
        if (confirmed!=assets.Image.ByteCount)
            throw new ProgrammingRejectedException("Confirmed byte count does not match image size.");
    }
}
