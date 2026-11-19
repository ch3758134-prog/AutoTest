using System.Buffers.Binary;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

// No ECU emulator, no WinUSB open and no simulated success path.
public sealed class PhysicalCan1FlashFenceTests
{
    private static readonly string Hash=new string('A',64);
    private const long Generation=9;
    private static byte[] Frame(byte port,uint id,params byte[] bytes)
    {
        byte[] p=new byte[15];
        p[0]=port;p[1]=(byte)(id>0x7FFu?1:0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2,4),id);
        p[6]=(byte)bytes.Length;bytes.CopyTo(p,7);
        return p;
    }
    private static byte[] Request(byte[] payload) =>
        UsbWireCodec.EncodeRequest(1,MainControllerCodec.CanSend,payload);
    private static (PhysicalCommandFence Fence,Guid Token) Authorized(int limit=2)
    {
        var guard=new PhysicalCommandFence();
        var token=guard.AuthorizeProgrammingJob(Hash,
            PhysicalCommandFence.FlashConfirmation(Hash),0x747,0x18DA50F1,
            Generation,limit);
        return(guard,token);
    }
    [Fact]
    public void FlashScopeRequiresExactFrozenShaConsent()
    {
        var fence=new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(()=>
            fence.AuthorizeProgrammingJob(Hash,"yes",0x747,0x18DA50F1,Generation,3));
        Assert.Throws<ArgumentException>(()=>
            fence.AuthorizeProgrammingJob(Hash,PhysicalCommandFence.FlashConfirmation(Hash),
                0,0x18DA50F1,Generation,3));
        var (guard,token)=Authorized();
        Assert.NotEqual(Guid.Empty,token);
        Assert.Throws<InvalidOperationException>(()=>
            guard.AuthorizeProgrammingJob(Hash,PhysicalCommandFence.FlashConfirmation(Hash),
                0x747,0x18DA50F1,Generation,3));
    }
    [Fact]
    public void FlashScopeNeverGrantsArbitraryCanIdOrCan0OrIgn()
    {
        var (guard,token)=Authorized();
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.PrepareProgrammingCanFrame(token,Frame(0,0x747,0x02,0x10,0x02),Generation));
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.PrepareProgrammingCanFrame(token,Frame(1,0x746,0x02,0x10,0x02),Generation));
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(
                UsbWireCodec.EncodeRequest(2,MainControllerCodec.SetIgn,[1]),Generation));
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(Frame(1,0x747,0x02,0x10,0x02)),Generation));
    }
    [Fact]
    public void EachExactFrameIsConsumedOnceBeforeNativeIo()
    {
        var (guard,token)=Authorized(limit:2);
        var payload=Frame(1,0x747,0x02,0x10,0x03);
        guard.PrepareProgrammingCanFrame(token,payload,Generation);
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(Frame(1,0x747,0x02,0x11,0x01)),Generation));
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(payload),Generation+1));
        guard.AssertAllowedAndConsume(Request(payload),Generation);
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(payload),Generation));
        guard.PrepareProgrammingCanFrame(token,Frame(1,0x18DA50F1,0x02,0x10,0x02),Generation);
        guard.AssertAllowedAndConsume(Request(Frame(1,0x18DA50F1,0x02,0x10,0x02)),Generation);
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.PrepareProgrammingCanFrame(token,payload,Generation));
    }
    [Fact]
    public void ResetRevokeAndDidCannotOverrideFlashScope()
    {
        var (guard,token)=Authorized();
        Assert.Throws<InvalidOperationException>(()=>
            guard.AuthorizeReadOnlyDid(0x747,0x74F,0xF190,
                PhysicalCommandFence.ReadDidConfirmation(0x747,0x74F,0xF190),Generation));
        guard.PrepareProgrammingCanFrame(token,Frame(1,0x747,0x02,0x10,0x03),Generation);
        guard.RevokeProgrammingJob(token);
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(Frame(1,0x747,0x02,0x10,0x03)),Generation));
        var next=guard.AuthorizeProgrammingJob(Hash,
            PhysicalCommandFence.FlashConfirmation(Hash),0x747,0x18DA50F1,Generation,2);
        guard.PrepareProgrammingCanFrame(next,Frame(1,0x747,0x02,0x10,0x03),Generation);
        guard.Reset();
        Assert.Throws<UnauthorizedAccessException>(()=>
            guard.AssertAllowedAndConsume(Request(Frame(1,0x747,0x02,0x10,0x03)),Generation));
    }
}
