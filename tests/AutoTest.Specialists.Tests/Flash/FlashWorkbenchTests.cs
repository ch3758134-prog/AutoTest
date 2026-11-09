using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Flash.Workbench;
using Xunit;

namespace AutoTest.Specialists.Tests.Flash;

public sealed class FlashWorkbenchTests
{
    private static FlashDryRunConfiguration Config(string actual = "MOCK_ECU_A",
        double voltage = 12.4, uint start = 0x1000, ulong end = 0x11000) =>
        new("MOCK_TARGET_A","MOCK_ECU_A",actual,start,end,voltage,11,15,64);

    [Fact]
    public async Task BinImagePassesPreflightAndCompletesMockProgramming()
    {
        var workspace=new FlashDryRunWorkspace();
        var bytes=Enumerable.Range(0,240).Select(x=>(byte)x).ToArray();
        var image=workspace.LoadImage("mock.bin",bytes,0x1000);
        Assert.Equal(240,image.Bytes);
        Assert.Single(image.Segments);
        var preflight=await workspace.PreflightAsync(Config());
        Assert.True(preflight.Ready,string.Join(',',preflight.Blockers));
        var job=await workspace.StartDryRunAsync(Config());
        var evidence=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Completed,evidence.State);
        Assert.Contains(evidence.Facts,x=>x.Stage=="Job" && x.Status=="PASS");
        Assert.Contains(evidence.Facts,x=>x.Stage=="VerifyMock" && x.Status=="PASS");
    }

    [Fact]
    public async Task IdentityAndVoltageMismatchBlockBeforeAnyProgrammingJob()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",new byte[]{1,2,3,4},0x1000);
        var preflight=await workspace.PreflightAsync(Config("WRONG_ECU",9.0));
        Assert.False(preflight.Ready);
        Assert.Contains("ECU_IDENTITY_MISMATCH",preflight.Blockers);
        Assert.Contains("VOLTAGE_OUT_OF_RANGE",preflight.Blockers);
        await Assert.ThrowsAsync<ProgrammingRejectedException>(async () =>
            { await workspace.StartDryRunAsync(Config("WRONG_ECU",9.0)); });
        Assert.Null(workspace.CurrentJob);
    }

    [Fact]
    public async Task OutOfMapImageNeverStarts()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",new byte[]{1,2,3},0x3000);
        var readiness=await workspace.PreflightAsync(Config(end:0x2000));
        Assert.False(readiness.Ready);
        Assert.Contains("IMAGE_ADDRESS_NOT_ALLOWED",readiness.Blockers);
    }

    [Fact]
    public async Task InjectedNrcCausesFailedJobWithErrorFacts()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,150).Select(i=>(byte)i).ToArray(),0x1000);
        var job=await workspace.StartDryRunAsync(Config(),0x36,0x72);
        var evidence=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Failed,evidence.State);
        Assert.DoesNotContain(evidence.Facts,x=>x.Stage=="Job" && x.Status=="PASS");
    }

    [Fact]
    public async Task SimulatedDisconnectIsNotTreatedAsSuccess()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,150).Select(i=>(byte)i).ToArray(),0x1000);
        var job=await workspace.StartDryRunAsync(Config(),dropBeforeBlock:1);
        var evidence=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Uncertain,evidence.State);
        Assert.Equal("UNCONFIRMED_UDS_RESPONSE",evidence.FailureCode);
        Assert.Contains(evidence.Facts,x=>x.Stage=="Job" && x.Status=="UNCERTAIN");
        Assert.DoesNotContain(evidence.Facts,x=>x.Stage=="Job" && x.Status=="PASS");
    }

    [Fact]
    public async Task LostSecondTransferAckDoesNotDiscardFirstConfirmedBlock()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,180)
            .Select(x=>(byte)x).ToArray(),0x1000);
        var job=await workspace.StartDryRunAsync(Config(),dropBeforeBlock:2);
        var snapshot=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Uncertain,snapshot.State);
        var acks=snapshot.Facts.Where(x=>x.Stage=="Transfer" && x.Status=="ACK").ToArray();
        var first=Assert.Single(acks);
        Assert.Equal(64,first.ConfirmedBytes);
        Assert.DoesNotContain(snapshot.Facts,x=>x.Stage=="Job" && x.Status=="PASS");
    }

    [Fact]
    public void BadFileTypeIsRejected()
    {
        var workspace=new FlashDryRunWorkspace();
        Assert.Throws<FirmwareFormatException>(()=>workspace.LoadImage("mock.exe",new byte[]{1},0x1000));
    }

    [Fact]
    public async Task MockAuditHasCorrelatedUdsFactsWithoutRawSeedOrFirmwarePayload()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,120).Select(i=>(byte)i).ToArray(),0x1000);
        var job=await workspace.StartDryRunAsync(Config());
        var snapshot=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Completed,snapshot.State);
        string path=Path.Combine(Path.GetTempPath(),"r0_flash_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(path);
            var audit=await File.ReadAllTextAsync(path);
            Assert.Contains("CorrelationId=",audit);
            Assert.Contains("SID=0x36",audit);
            Assert.Contains("REDACTED_FIRMWARE",audit);
            Assert.Contains("PhysicalProgrammingVerified",audit);
            Assert.Contains("false",audit);
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
}
