using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Usb;
using AutoTest.Devices.Can;
using AutoTest.Devices.Station;
using Xunit;
namespace AutoTest.Devices.Tests;

public sealed class UsbSessionContractTests
{
    [Fact] public void Verify_A1_T01()
    {
        Assert.Equal("A53412F000EA115A",Convert.ToHexString(MainControllerCodec.EncodePing(0x1234)));
        Assert.True(UsbWireCodec.TryDecodeResponse(Convert.FromHexString("A53412F00000908F5A"),out var resp));
        Assert.Equal((ushort)0x1234,resp.Sequence);
        Assert.True(UsbWireCodec.TryDecodeRequest(MainControllerCodec.EncodePing(0x1234),out var request));
        Assert.Equal((byte)0xF0,request.Command);
    }
    [Fact] public void Verify_A1_T02()
    {
        var expected=UsbWireCodec.EncodeResponse(0,0x41,0,new byte[]{0x11,0x22,0x33});
        var next=UsbWireCodec.EncodeResponse(3,0xF0,0,Array.Empty<byte>());
        var decoder=new UsbFrameDecoder(); var actual=new List<UsbResponse>();
        foreach(var byteValue in expected) actual.AddRange(decoder.Feed(new byte[]{byteValue}));
        actual.AddRange(decoder.Feed(next.Concat(next).ToArray()));
        Assert.Equal(new ushort[]{0,3,3},actual.Select(x=>x.Sequence));
    }
    [Fact] public void Verify_A1_T03()
    {
        var bad=UsbWireCodec.EncodeResponse(9,0xF0,0,Array.Empty<byte>());bad[6]^=0xFF;
        var good=UsbWireCodec.EncodeResponse(10,0xF0,0,Array.Empty<byte>());
        var decoder=new UsbFrameDecoder();var result=decoder.Feed(new byte[]{0xFF,0xAA}.Concat(bad).Concat(good).ToArray());
        Assert.Single(result);Assert.Equal((ushort)10,result[0].Sequence);
        Assert.True(decoder.InvalidCandidates>0);
    }
    [Fact] public void Verify_A1_T04()
    {
        var registry=new PendingRequestRegistry();var (seq,task)=registry.Register(1,0xF0);
        Assert.False(registry.TryComplete(1,new UsbResponse(0,0x41,0,Array.Empty<byte>())));
        Assert.True(registry.TryComplete(1,new UsbResponse(seq,0xF0,0,Array.Empty<byte>())));
        Assert.True(task.IsCompletedSuccessfully);
    }
    [Fact] public async Task Verify_A1_T05()
    {
        var pending=new PendingRequestRegistry();var (seq,task)=pending.Register(12,0x40);
        pending.FailGeneration(12,new TimeoutException());
        await Assert.ThrowsAsync<TimeoutException>(async()=>await task);
        Assert.False(pending.TryComplete(12,new UsbResponse(seq,0x40,0,Array.Empty<byte>())));
        var (next,newTask)=pending.Register(13,0x40);
        Assert.False(pending.TryComplete(12,new UsbResponse(next,0x40,0,Array.Empty<byte>())));
        Assert.True(pending.TryComplete(13,new UsbResponse(next,0x40,0,Array.Empty<byte>())));
        await newTask;
    }
}
public sealed class StationControlContractTests
{
    private static BusRoleMap Roles(int dut,int key)=>new(default,dut,key);
    [Fact] public void Verify_A2_T01()
    {
        var frame=KeypadCommandCodec.EncodeMask(1);
        var payload=CanFrameCodec.ToUsbPayload(frame,Roles(1,0));
        Assert.Equal((byte)0,payload[0]);
        Assert.True(CanFrameCodec.FromUsbPayload(payload,Roles(1,0),out var decoded));
        Assert.Equal(BusRole.KeypadCan,decoded!.Role);
        Assert.Throws<InvalidOperationException>(()=>CanFrameCodec.ToUsbPayload(frame,Roles(0,0)));
    }
    [Fact] public async Task Verify_A2_T02()
    {
        Assert.Equal(PhysicalState.Unknown,OutputObservation.ClassifyEvidence(AckKind.DeviceAcknowledged,false,null));
        var service=new StationControlService();
        var result=await service.SetIgnAsync(true,null!);
        Assert.Equal(Outcome.Rejected,result.Outcome);
    }
    [Fact] public async Task Verify_A2_T03()
    {
        Assert.Equal(PhysicalState.Unknown,OutputObservation.ClassifyEvidence(AckKind.SimulatorAcknowledged,false,null));
        var result=await new StationControlService().OperateFixtureAsync(false,null!);
        Assert.Equal(Outcome.Rejected,result.Outcome);
    }
    [Fact] public void Verify_A2_T04()
    {
        var key=KeypadCommandCodec.EncodeMask(1);
        Assert.Equal("0101000000000000",Convert.ToHexString(key.Data.ToArray()));
        Assert.Equal("8000000000000000",Convert.ToHexString(KeypadCommandCodec.EncodeReleaseAll().Data.ToArray()));
        Assert.Equal("01FFFF0F00000000",Convert.ToHexString(KeypadCommandCodec.EncodeMask(0xFFFFF).Data.ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(()=>KeypadCommandCodec.EncodeMask(0x100000));
        var roles=Roles(1,0);
        Assert.Equal("000030070000080101000000000000",Convert.ToHexString(CanFrameCodec.ToUsbPayload(key,roles)));
        Assert.Equal("A53700400F000030070000080101000000000000A8835A",
            Convert.ToHexString(MainControllerCodec.EncodeCan(0x37,CanFrameCodec.ToUsbPayload(key,roles))));
    }
}
public sealed class CanHubContractTests
{
    private static CanSubscriptionSpec Spec(int capacity=3)=>new(BusRole.DutCan,ImmutableArray<uint>.Empty,CanBackpressurePolicy.DropWithLossFact,capacity);
    private static CanObservation Obs(uint id=0x321) => new(CanFrame.Create(BusRole.DutCan,false,id,new byte[]{1}),TimePoint.FromSimulation(DateTimeOffset.UtcNow,0),EvidenceQuality.Derived,7,0,EvidenceRef.New());
    [Fact] public async Task Verify_A3_T01()
    {
        using var hub=new CanFrameHub();using var cancel=new CancellationTokenSource();
        var readers=Enumerable.Range(0,4).Select(_=>hub.SubscribeAsync(Spec(),cancel.Token).GetAsyncEnumerator()).ToArray();
        var moves=readers.Select(x=>x.MoveNextAsync().AsTask()).ToArray();
        Assert.Equal(4,hub.SubscriberCount);hub.Publish(Obs());
        foreach(var task in moves) Assert.True(await task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.All(readers,x=>Assert.Equal((uint)0x321,x.Current.Frame.Id));
        foreach(var reader in readers) await reader.DisposeAsync();
        Assert.Equal(0,hub.SubscriberCount);
    }
    [Fact] public async Task Verify_A3_T02()
    {
        using var hub=new CanFrameHub();using var cancel=new CancellationTokenSource();
        var reader=hub.SubscribeAsync(Spec(1),cancel.Token).GetAsyncEnumerator();
        var move=reader.MoveNextAsync().AsTask();hub.Publish(Obs());await move.WaitAsync(TimeSpan.FromSeconds(3));
        hub.Publish(Obs());hub.Publish(Obs());
        Assert.Equal(1,hub.TotalDropped);
        Assert.True(await reader.MoveNextAsync());
        hub.Publish(Obs());Assert.True(await reader.MoveNextAsync());
        Assert.True(reader.Current.LostFrames>=1);
        await reader.DisposeAsync();
    }
    [Fact] public async Task Verify_A3_T03()
    {
        var gate=new CanTransmitGate(2);
        var first=await gate.AcquireAsync(BusRole.DutCan,"UDS",TimeSpan.FromSeconds(10),default);
        var second=await gate.AcquireAsync(BusRole.DutCan,"UI",TimeSpan.FromSeconds(10),default);
        Assert.Equal(Outcome.Accepted,first.Outcome);Assert.Equal(Outcome.Rejected,second.Outcome);
        Assert.True(gate.Validate(BusRole.DutCan,first.Value!));
        gate.Reset(3);Assert.False(gate.Validate(BusRole.DutCan,first.Value!));
    }
    [Fact] public void Verify_A3_T04()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>CanFrame.Create(BusRole.DutCan,false,0x800,new byte[]{1}));
        var roles=new BusRoleMap(default,0,1);
        var ext=CanFrame.Create(BusRole.DutCan,true,0x1FFFFFFF,new byte[]{1});
        var payload=CanFrameCodec.ToUsbPayload(ext,roles);
        Assert.True(CanFrameCodec.FromUsbPayload(payload,roles,out var decoded));Assert.True(decoded!.ExtendedId);
        payload[1]=0; Assert.False(CanFrameCodec.FromUsbPayload(payload,roles,out _));
    }
    [Fact] public async Task Verify_A3_T05()
    {
        using var hub=new CanFrameHub();var reader=hub.SubscribeAsync(Spec()).GetAsyncEnumerator();
        var next=reader.MoveNextAsync().AsTask();Assert.Equal(1,hub.SubscriberCount);
        hub.Disconnect();Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(3)));
        await reader.DisposeAsync();Assert.Equal(0,hub.SubscriberCount);
    }
}
