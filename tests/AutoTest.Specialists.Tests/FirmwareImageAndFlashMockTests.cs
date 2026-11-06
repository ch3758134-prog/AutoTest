using System.Collections.Immutable;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Flash;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class ProgrammingMockContractTests
{
    private static readonly StationId Station=StationId.Create("MOCK-STATION");
    private static readonly UdsEndpointProfile Endpoint=new(BusRole.DutCan,0x7E0,0x7E8,
        TimeSpan.FromMilliseconds(200),TimeSpan.FromMilliseconds(500),"Normal");
    private static string HexRecord(byte type,ushort address,params byte[] data)
    {
        var parts=new byte[]{(byte)data.Length,(byte)(address>>8),(byte)address,type}.Concat(data).ToArray();
        byte sum=(byte)unchecked(0-parts.Sum(x=>x));
        return ":"+Convert.ToHexString(parts)+sum.ToString("X2");
    }
    private static string S1(ushort address,params byte[] data)
    {
        byte count=(byte)(data.Length+3);
        var all=new byte[]{count,(byte)(address>>8),(byte)address}.Concat(data).ToArray();
        byte check=(byte)~(byte)all.Sum(x=>x);
        return "S1"+Convert.ToHexString(all)+check.ToString("X2");
    }
    [Fact]
    public void Verify_E4_T01_FormatsAndChecksums()
    {
        var hex=HexRecord(4,0,0,1)+"\n"+HexRecord(0,0x1000,1,2,3,4)+"\n"+HexRecord(1,0);
        var image=FirmwareImageParser.ParseHex(hex);
        Assert.Equal(0x11000u,image.Segments[0].Address); Assert.Equal(4L,image.ByteCount);
        var s19=S1(0x1000,1,2,3,4)+"\nS9030000FC";
        var parsed=FirmwareImageParser.ParseS19(s19);
        Assert.Equal(0x1000u,parsed.Segments[0].Address);
        Assert.Equal(new byte[]{1,2,3,4},parsed.Segments[0].Data.ToArray());
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(hex.Replace("F9","F8")));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseS19(s19.Replace("DE","DF")));
    }
    [Fact]
    public void Verify_E4_T02_OverlapEmptyAndOverflow()
    {
        var duplicate=HexRecord(0,0x1000,1,2)+"\n"+HexRecord(0,0x1001,3,4)+"\n"+HexRecord(1,0);
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(duplicate));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseHex(HexRecord(1,0)));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseBin([1,2,3,4],uint.MaxValue-1));
        Assert.Throws<FirmwareFormatException>(()=>FirmwareImageParser.ParseS19("S9030000FC"));
    }
    private sealed class FixedProvider(ProgrammingAssets assets) : IProgrammingContextProvider
    {
        public ProgrammingAssets Assets {get;set;}=assets;
        public ValueTask<ProgrammingAssets> ResolveAsync(ProgrammingRequest request,CancellationToken ct)
            => ValueTask.FromResult(Assets);
    }
    private static (FixedProvider Provider,ProgrammingRequest Request,ProgrammingProfile Profile,
        FirmwareImage Image,MockBootloader Bootloader,ProgrammingJobCoordinator Coordinator) Setup(int bytes=111)
    {
        var image=FirmwareImageParser.ParseBin(Enumerable.Range(0,bytes).Select(i=>(byte)(i*7)).ToArray(),0x1000);
        var rev=RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes("PROFILE_MOCK_V1"));
        var profile=new ProgrammingProfile("MODEL-A","MODEL-A",Endpoint,
            [new FlashAddressRange(0x1000,0x11000)],23,11.5,14.8,rev);
        var assets=new ProgrammingAssets(profile,image,new ProgrammingEnvironment(Station,"MODEL-A",12.5,true,true));
        var provider=new FixedProvider(assets);
        var request=new ProgrammingRequest(Station,"MODEL-A",new AssetRef(AssetId.New(),
            AssetRevision.Create(1),image.SourceFingerprint),rev);
        var bootloader=new MockBootloader("MODEL-A",0x1000,0x10000,17);
        var coordinator=new ProgrammingJobCoordinator(provider,new MockProgrammingWorkflow(bootloader));
        return (provider,request,profile,image,bootloader,coordinator);
    }
    private static SafetyAuthorization MockPermission() => new(
        new Authorization(AuthorizationScope.MockOnly,Station,1,
            DateTimeOffset.UtcNow.AddMinutes(1),"V0_MOCK_ISSUER"),"MOCK_FLASH","TEST_ONLY");
    [Fact]
    public async Task Verify_E4_T03_ModelVoltageAndNonMockRejected()
    {
        var s=Setup();
        s.Provider.Assets=s.Provider.Assets with{ Environment=s.Provider.Assets.Environment with{DeviceIdentity="OTHER"}};
        var invalid=await s.Coordinator.PreflightAsync(s.Request,default);
        Assert.False(invalid.Ready); Assert.Contains("ECU_IDENTITY_MISMATCH",invalid.Blockers);
        s.Provider.Assets=s.Provider.Assets with{ Environment=s.Provider.Assets.Environment with{DeviceIdentity="MODEL-A",Voltage=8}};
        Assert.Contains("VOLTAGE_OUT_OF_RANGE",(await s.Coordinator.PreflightAsync(s.Request,default)).Blockers);
        s.Provider.Assets=s.Provider.Assets with{ Environment=s.Provider.Assets.Environment with{Voltage=12.5,IsMock=false}};
        Assert.Contains("PHYSICAL_FLASH_NOT_AUTHORIZED",(await s.Coordinator.PreflightAsync(s.Request,default)).Blockers);
    }
    [Fact]
    public async Task Verify_E4_T04_MockProgrammingFullPathAndHash()
    {
        var s=Setup();
        var plan=await s.Coordinator.BuildPlanAsync(s.Request,default);
        var id=await s.Coordinator.ExecuteAsync(plan,MockPermission(),default);
        var result=await s.Coordinator.WaitForCompletionAsync(id,default);
        Assert.Equal(ProgrammingJobState.Completed,result.State);
        Assert.Contains(result.Facts,f=>f.Stage=="VerifyMock" && f.Status=="PASS");
        Assert.Equal(s.Image.Segments[0].Data.ToArray(),s.Bootloader.ReadMemory(0x1000,(int)s.Image.ByteCount));
        Assert.Equal(7,s.Bootloader.TransferBlocks); // ceil(111 / min(23,17))
    }
    [Fact]
    public async Task Verify_E4_T05_NrcDisconnectFailureAndRestart()
    {
        var s=Setup();
        var plan=await s.Coordinator.BuildPlanAsync(s.Request,default);
        s.Bootloader.SimulateNrc(0x36,0x72);
        var first=await s.Coordinator.ExecuteAsync(plan,MockPermission(),default);
        var fail=await s.Coordinator.WaitForCompletionAsync(first,default);
        Assert.Equal(ProgrammingJobState.Failed,fail.State);
        Assert.DoesNotContain(fail.Facts,f=>f.Stage=="Job" && f.Status=="PASS");
        s.Bootloader.ReconnectMock(); s.Bootloader.DisconnectBeforeBlock=2;
        var second=await s.Coordinator.ExecuteAsync(plan,MockPermission(),default);
        var lost=await s.Coordinator.WaitForCompletionAsync(second,default);
        Assert.Equal(ProgrammingJobState.Failed,lost.State);
        s.Bootloader.DisconnectBeforeBlock=-1; s.Bootloader.ReconnectMock();
        var third=await s.Coordinator.ExecuteAsync(plan,MockPermission(),default);
        Assert.Equal(ProgrammingJobState.Completed,(await s.Coordinator.WaitForCompletionAsync(third,default)).State);
    }
    [Fact]
    public async Task CancelledJobNeverReportsPass()
    {
        var s=Setup();
        var workflow=new MockProgrammingWorkflow(s.Bootloader)
        { ResponseLatency=TimeSpan.FromMilliseconds(100) };
        var coordinator=new ProgrammingJobCoordinator(s.Provider,workflow);
        var plan=await coordinator.BuildPlanAsync(s.Request,default);
        var id=await coordinator.ExecuteAsync(plan,MockPermission(),default);
        Assert.True(coordinator.RequestCancel(id));
        var snapshot=await coordinator.WaitForCompletionAsync(id,default);
        Assert.Equal(ProgrammingJobState.Cancelled,snapshot.State);
        Assert.DoesNotContain(snapshot.Facts,f=>f.Stage=="Job" && f.Status=="PASS");
    }
    [Fact]
    public async Task Verify_E4_T06_ImageMismatchAndCorruptedPlanCannotStart()
    {
        var s=Setup();
        var modified=s.Request with{ Image=s.Request.Image with{
            Fingerprint=RevisionFingerprint.FromBytes([0xAB])}};
        Assert.Contains("IMAGE_HASH_MISMATCH",(await s.Coordinator.PreflightAsync(modified,default)).Blockers);
        var plan=await s.Coordinator.BuildPlanAsync(s.Request,default);
        await Assert.ThrowsAsync<ProgrammingRejectedException>(async()=>{ await s.Coordinator.ExecuteAsync(
            plan with{LockedStages=["UnsafeCustomStage"]},MockPermission(),default); });
        Assert.Equal(0,s.Bootloader.TransferBlocks);
    }
    [Fact]
    public async Task Verify_E4_T07_RealAuthorizationStrictlyRejected()
    {
        var s=Setup(); var plan=await s.Coordinator.BuildPlanAsync(s.Request,default);
        var physical=new SafetyAuthorization(new Authorization(AuthorizationScope.Programming,Station,1,
            DateTimeOffset.UtcNow.AddMinutes(1),"V0_MOCK_ISSUER"),"REAL_FLASH","NONE");
        await Assert.ThrowsAsync<ProgrammingRejectedException>(async()=>{ await s.Coordinator.ExecuteAsync(plan,physical,default); });
        Assert.Equal(0,s.Bootloader.TransferBlocks);
    }
}
