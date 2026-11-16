using System.Buffers.Binary;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>Pure transport-guard negative tests: NEVER an ECU simulation or evidence of real CAN transmission.</summary>
public sealed class ReadOnlyDidPhysicalFenceTests
{
    private const uint Tx=0x747, Rx=0x74F;
    private const ushort Did=0xF190;
    private static byte[] Can(uint id, params byte[] data)
    {
        var p=new byte[15];p[0]=1;p[1]=(byte)(id>0x7FF ? 1:0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2,4),id);
        p[6]=(byte)data.Length;data.CopyTo(p,7);return p;
    }
    private static byte[] Wire(byte[] payload) =>
        UsbWireCodec.EncodeRequest(1,MainControllerCodec.CanSend,payload);
    private static PhysicalCommandFence Grant()
    {
        var f=new PhysicalCommandFence();
        f.AuthorizeReadOnlyDid(Tx,Rx,Did,PhysicalCommandFence.ReadDidConfirmation(Tx,Rx,Did),4);
        return f;
    }
    [Fact]
    public void DidRequiresExactManualConsentAndValidUsbGeneration()
    {
        var f=new PhysicalCommandFence();
        Assert.Throws<UnauthorizedAccessException>(()=>f.AuthorizeReadOnlyDid(Tx,Rx,Did,"yes",4));
        f=Grant();
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),5));
        f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4);
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4));
    }
    [Fact]
    public void AllHighRiskSidsAndOtherDidAreDeniedEvenOnApprovedId()
    {
        var f=Grant();
        foreach(byte sid in new byte[]{0x10,0x11,0x27,0x31,0x34,0x36,0x37,0x3E,0x19})
            Assert.Throws<UnauthorizedAccessException>(()=>
                f.AssertAllowedAndConsume(Wire(Can(Tx,2,sid,1)),4));
        Assert.Throws<UnauthorizedAccessException>(()=>
            f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x91)),4));
        Assert.Throws<UnauthorizedAccessException>(()=>
            f.AssertAllowedAndConsume(Wire(Can(0x700,3,0x22,0xF1,0x90)),4));
        var keypad=Can(Tx,3,0x22,0xF1,0x90);keypad[0]=0;
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(Wire(keypad),4));
    }
    [Fact]
    public void FlowControlOnlyAfterAuthenticMatchingFirstFrameAndOnePerBlock()
    {
        var f=Grant();
        var fc=Wire(Can(Tx,0x30,8,0));
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
        f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4);
        f.ObserveAuthenticatedCanRx(Can(Rx,0x10,0x20,0x62,0xF1,0x91,0,0,0),4);
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
        // A mismatched FF irrevocably invalidates this grant.
        Assert.Throws<UnauthorizedAccessException>(()=>
            f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4));

        f=Grant();f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4);
        f.ObserveAuthenticatedCanRx(Can(Rx,0x10,0x20,0x62,0xF1,0x90,0,0,0),4);
        f.AssertAllowedAndConsume(fc,4);
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
        // FF length 0x20 -> 4 CF. A second FC cannot be granted without a new FF.
        for(int i=1;i<=4;i++) f.ObserveAuthenticatedCanRx(Can(Rx,(byte)(0x20|i),0,0,0,0,0,0,0),4);
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
        f.Reset();
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
    }
    [Fact]
    public void MidTransferNextFlowControlRequiresEightRealSequentialFrames()
    {
        var f=Grant();f.AssertAllowedAndConsume(Wire(Can(Tx,3,0x22,0xF1,0x90)),4);
        f.ObserveAuthenticatedCanRx(Can(Rx,0x10,0x80,0x62,0xF1,0x90,0,0,0),4);
        var fc=Wire(Can(Tx,0x30,8,0)); f.AssertAllowedAndConsume(fc,4);
        for(int i=1;i<=7;i++) f.ObserveAuthenticatedCanRx(Can(Rx,(byte)(0x20|i),0,0,0,0,0,0,0),4);
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
        f.ObserveAuthenticatedCanRx(Can(Rx,0x28,0,0,0,0,0,0,0),4);
        f.AssertAllowedAndConsume(fc,4);
        f.RevokeReadOnlyDid();
        Assert.Throws<UnauthorizedAccessException>(()=>f.AssertAllowedAndConsume(fc,4));
    }
}
