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

    [Fact]
    public async Task MockAuditExposesLastAckAndVerificationOnlyWhenAllSegmentsVerified()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,130).Select(i=>(byte)i).ToArray(),0x1000);
        var id=await workspace.StartDryRunAsync(Config());
        var result=await workspace.WaitAsync(id);
        Assert.Equal(ProgrammingJobState.Completed,result.State);
        string file=Path.Combine(Path.GetTempPath(),"r0_mock_audit_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(file);
            using var doc=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var root=doc.RootElement;
            Assert.Equal(2,root.GetProperty("Schema").GetInt32());
            Assert.Equal(130,root.GetProperty("AcknowledgedTransferBytes").GetInt64());
            Assert.True(root.GetProperty("MockHashVerified").GetBoolean());
            Assert.True(root.GetProperty("FullBackendFactsIncluded").GetBoolean());
            Assert.False(root.GetProperty("PhysicalProgrammingVerified").GetBoolean());
            Assert.Equal("Completed",root.GetProperty("State").GetString());
        }
        finally { if(File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task DisconnectedMockAuditNeverClaimsVerifiedProgramming()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("mock.bin",Enumerable.Range(0,130).Select(i=>(byte)i).ToArray(),0x1000);
        var id=await workspace.StartDryRunAsync(Config(),dropBeforeBlock:2);
        var snapshot=await workspace.WaitAsync(id);
        Assert.Equal(ProgrammingJobState.Uncertain,snapshot.State);
        string file=Path.Combine(Path.GetTempPath(),"r0_mock_uncertain_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(file);
            using var doc=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var root=doc.RootElement;
            Assert.Equal("Uncertain",root.GetProperty("State").GetString());
            Assert.Equal(64,root.GetProperty("AcknowledgedTransferBytes").GetInt64());
            Assert.False(root.GetProperty("MockHashVerified").GetBoolean());
            Assert.False(root.GetProperty("PhysicalTransportUsed").GetBoolean());
        }
        finally { if(File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task ReloadingImageDoesNotRewritePriorMockJobAudit()
    {
        var workspace=new FlashDryRunWorkspace();
        var first=Enumerable.Range(0,120).Select(x=>(byte)x).ToArray();
        workspace.LoadImage("first.bin",first,0x1000);
        var id=await workspace.StartDryRunAsync(Config());
        var completed=await workspace.WaitAsync(id);
        Assert.Equal(ProgrammingJobState.Completed,completed.State);

        // A completed job may be followed by another image import, but its
        // exported evidence must still identify the image actually programmed.
        workspace.LoadImage("second.bin",new byte[]{9,8,7,6},0x2000);
        Assert.Equal("second.bin",workspace.Image?.FileName);
        string path=Path.Combine(Path.GetTempPath(),"r0_job_frozen_"+
            Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(path);
            using var json=System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(path));
            var root=json.RootElement;
            Assert.Equal(id.Value,root.GetProperty("Job").GetGuid());
            Assert.Equal("first.bin",root.GetProperty("Image")
                .GetProperty("FileName").GetString());
            Assert.Equal(120,root.GetProperty("Image")
                .GetProperty("Bytes").GetInt64());
            Assert.Equal(120,root.GetProperty("AcknowledgedTransferBytes").GetInt64());
            Assert.Equal(1,root.GetProperty("ExpectedSegmentVerifications").GetInt32());
            Assert.Equal("MOCK_ECU_A",root.GetProperty("MockConfiguration")
                .GetProperty("ExpectedIdentity").GetString());
            Assert.True(root.TryGetProperty("JobInputCapturedUtc",out _));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
    // Two separated Intel HEX address ranges, both with valid checksums.
    private const string TwoSegmentHex =
        ":0410000001020304E2\\n:0411000005060708D1\\n:00000001FF\\n";

    [Fact]
    public async Task TwoSegmentImageProducesTwoIndependentVerifiedSegmentFacts()
    {
        var workspace=new FlashDryRunWorkspace();
        var image=workspace.LoadImage("two.hex",
            System.Text.Encoding.ASCII.GetBytes(TwoSegmentHex),0);
        Assert.Equal(2,image.Segments.Count);
        Assert.Equal(8,image.Bytes);
        var id=await workspace.StartDryRunAsync(Config());
        var result=await workspace.WaitAsync(id);
        Assert.Equal(ProgrammingJobState.Completed,result.State);
        Assert.Contains(result.Facts,f=>f.Stage=="Segment_0001" && f.Status=="VERIFIED");
        Assert.Contains(result.Facts,f=>f.Stage=="Segment_0002" && f.Status=="VERIFIED");

        string path=Path.Combine(Path.GetTempPath(),"r0_two_segments_"+
            Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(path);
            using var audit=System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(path));
            var root=audit.RootElement;
            Assert.Equal(8,root.GetProperty("AcknowledgedTransferBytes").GetInt64());
            Assert.Equal(2,root.GetProperty("ExpectedSegmentVerifications").GetInt32());
            Assert.Equal(2,root.GetProperty("ActualSegmentVerifications").GetInt32());
            Assert.True(root.GetProperty("MockHashVerified").GetBoolean());
            var segments=root.GetProperty("SegmentEvidence");
            Assert.Equal(2,segments.GetArrayLength());
            Assert.Equal("0x00001000",segments[0].GetProperty("Start").GetString());
            Assert.Equal("0x00001100",segments[1].GetProperty("Start").GetString());
            Assert.All(segments.EnumerateArray(),seg=>
            {
                Assert.True(seg.GetProperty("Started").GetBoolean());
                Assert.True(seg.GetProperty("TransferAcknowledged").GetBoolean());
                Assert.True(seg.GetProperty("MockHashVerified").GetBoolean());
            });
            Assert.False(root.GetProperty("PhysicalProgrammingVerified").GetBoolean());
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SecondSegmentDisconnectPreservesFirstSegmentVerificationOnly()
    {
        var workspace=new FlashDryRunWorkspace();
        workspace.LoadImage("two.hex",
            System.Text.Encoding.ASCII.GetBytes(TwoSegmentHex),0);
        var job=await workspace.StartDryRunAsync(Config(),dropBeforeBlock:2);
        var result=await workspace.WaitAsync(job);
        Assert.Equal(ProgrammingJobState.Uncertain,result.State);
        Assert.Contains(result.Facts,f=>f.Stage=="Segment_0001" && f.Status=="VERIFIED");
        Assert.Contains(result.Facts,f=>f.Stage=="Segment_0002" && f.Status=="BEGIN");
        Assert.DoesNotContain(result.Facts,f=>f.Stage=="Segment_0002" && f.Status=="VERIFIED");

        string path=Path.Combine(Path.GetTempPath(),"r0_partial_segments_"+
            Guid.NewGuid().ToString("N")+".json");
        try
        {
            await workspace.ExportMockAuditAsync(path);
            using var audit=System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(path));
            var root=audit.RootElement;
            Assert.Equal(4,root.GetProperty("AcknowledgedTransferBytes").GetInt64());
            Assert.Equal(1,root.GetProperty("ActualSegmentVerifications").GetInt32());
            Assert.False(root.GetProperty("MockHashVerified").GetBoolean());
            var segments=root.GetProperty("SegmentEvidence");
            Assert.True(segments[0].GetProperty("MockHashVerified").GetBoolean());
            Assert.True(segments[1].GetProperty("Started").GetBoolean());
            Assert.False(segments[1].GetProperty("TransferAcknowledged").GetBoolean());
            Assert.False(segments[1].GetProperty("MockHashVerified").GetBoolean());
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

}
