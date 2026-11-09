using System.Collections.Immutable;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Uds;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class IsoTpUdsContractTests
{
    private static UdsEndpointProfile Profile => new(BusRole.DutCan,0x7E0,0x7E8,
        TimeSpan.FromMilliseconds(80),TimeSpan.FromMilliseconds(250),"Normal");
    private static UdsRequest Request(params byte[] data) => new(data.ToImmutableArray(),Guid.NewGuid());
    private sealed class TestLink : IIsoTpFrameLink
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private TestLink? _remote;
        public List<byte[]> Sent { get; }=[];
        public static (TestLink,TestLink) Connect()
        {
            var a=new TestLink(); var b=new TestLink(); a._remote=b; b._remote=a;
            return (a,b);
        }
        public void Inject(byte[] frame) => _incoming.Writer.TryWrite(frame);
        public ValueTask SendAsync(ReadOnlyMemory<byte> data,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var frame=data.ToArray(); Sent.Add(frame);
            _remote?._incoming.Writer.TryWrite(frame);
            return ValueTask.CompletedTask;
        }
        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
            => await _incoming.Reader.ReadAsync(ct);
    }
    [Fact]
    public async Task Verify_E2_T01_SF_FF_CF_FC_AndReassembly()
    {
        var sf=IsoTpFrameCodec.EncodeSingle([0x22,0xF1,0x90]);
        Assert.Equal(new byte[]{0x03,0x22,0xF1,0x90},sf);
        Assert.Equal(new byte[]{0x22,0xF1,0x90},IsoTpFrameCodec.ParseSingle(sf));
        var flow=IsoTpFrameCodec.ParseFlow(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Continue,2,0));
        Assert.Equal((byte)2,flow.BlockSize);
        var (client,ecu)=TestLink.Connect();
        var tx=new IsoTpConversation(client,new IsoTpOptions(TimeSpan.FromSeconds(1)));
        var rx=new IsoTpConversation(ecu,new IsoTpOptions(TimeSpan.FromSeconds(1),2));
        var payload=Enumerable.Range(0,70).Select(x=>(byte)x).ToArray();
        var receive=rx.ReceivePduAsync(TimeSpan.FromSeconds(1)).AsTask();
        await tx.SendPduAsync(payload);
        Assert.Equal(payload,await receive);
        Assert.Contains(client.Sent,f=>IsoTpFrameCodec.Kind(f)==IsoTpFrameKind.First);
        Assert.Contains(client.Sent,f=>IsoTpFrameCodec.Kind(f)==IsoTpFrameKind.Consecutive);
        Assert.True(ecu.Sent.Count(f=>IsoTpFrameCodec.Kind(f)==IsoTpFrameKind.FlowControl)>=2);
    }
    [Fact]
    public async Task Verify_E2_T02_BadSequenceAndFlowTimeout()
    {
        var link=new TestLink();
        link.Inject(IsoTpFrameCodec.EncodeFirst(13,new byte[13]));
        link.Inject(IsoTpFrameCodec.EncodeConsecutive(2,new byte[7]));
        var rx=new IsoTpConversation(link,new IsoTpOptions(TimeSpan.FromMilliseconds(40)));
        await Assert.ThrowsAsync<IsoTpProtocolException>(async()=>{ await rx.ReceivePduAsync(TimeSpan.FromMilliseconds(40)); });
        var tx=new IsoTpConversation(new TestLink(),new IsoTpOptions(TimeSpan.FromMilliseconds(20)));
        await Assert.ThrowsAsync<TimeoutException>(async()=>{ await tx.SendPduAsync(new byte[20]); });
        Assert.Throws<IsoTpProtocolException>(()=>IsoTpFrameCodec.ParseFlow([0x30,0,0x80]));
    }
    [Fact]
    public async Task FlowWaitOverflowAndStMinEncoding()
    {
        var link=new TestLink();
        link.Inject(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Wait,0,0));
        link.Inject(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Continue,0,0));
        await new IsoTpConversation(link,new IsoTpOptions(TimeSpan.FromMilliseconds(50)))
            .SendPduAsync(new byte[30]);
        Assert.Equal(5,link.Sent.Count); // FF + ceil((30 - 6)/7) CF
        Assert.Equal(TimeSpan.FromTicks(3000),IsoTpFrameCodec.DecodeStMin(0xF3));
        var overflow=new TestLink();
        overflow.Inject(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Overflow,0,0));
        await Assert.ThrowsAsync<IsoTpProtocolException>(async()=>{ await new IsoTpConversation(overflow)
            .SendPduAsync(new byte[20]); });
    }
    [Fact]
    public async Task Verify_E2_T03_ResponsePendingAndBusyNrc()
    {
        var ecu=new MockBootloader("UNIT-TEST");
        ecu.SimulatePendingOnce(0x22);
        await using var channel=new MockUdsMessageChannel(ecu);
        var uds=new UdsClient(channel);
        var result=await uds.RequestAsync(Request(0x22,0xF1,0x90),Profile,default);
        Assert.Equal(Outcome.Accepted,result.Outcome);
        Assert.Equal((byte)0x62,result.ResponsePdu[0]);
        Assert.Equal(2,uds.Trace.Snapshot().Count(x=>x.Direction==UdsTraceDirection.Response));
        Assert.Contains(channel.EcuFrames(),f=>IsoTpFrameCodec.Kind(f)==IsoTpFrameKind.First);
        ecu.SimulateNrc(0x22,0x21);
        var busy=await uds.RequestAsync(Request(0x22,0xF1,0x90),Profile,default);
        Assert.Equal(Outcome.Rejected,busy.Outcome); Assert.Equal((byte)0x21,busy.NegativeResponseCode);
    }
    [Fact]
    public async Task Verify_E2_T04_NoResponseAndWrongSid()
    {
        var boot=new MockBootloader("UNIT-TEST"){IgnoreRequests=true};
        await using var channel=new MockUdsMessageChannel(boot);
        var uds=new UdsClient(channel);
        Assert.Equal(Outcome.Uncertain,(await uds.RequestAsync(Request(0x22,0xF1,0x90),
            Profile with{P2=TimeSpan.FromMilliseconds(15)},default)).Outcome);
        var wrong=new UdsClient(new WrongSidChannel());
        Assert.Equal(Outcome.Uncertain,(await wrong.RequestAsync(Request(0x22,0xF1,0x90),Profile,default)).Outcome);
        Assert.Equal(Outcome.Rejected,(await wrong.RequestAsync(Request(0x10,0x02),Profile,default)).Outcome);
        Assert.Throws<NotSupportedException>(()=>UdsEndpointProfileValidator.ValidateAddressing(
            Profile with{Addressing="Extended"}));
    }
    private sealed class WrongSidChannel : IUdsMessageChannel
    {
        public ValueTask SendPduAsync(ReadOnlyMemory<byte> data,CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout,CancellationToken ct) => ValueTask.FromResult(new byte[]{0x50,1});
    }
    private sealed class BlockingChannel : IUdsMessageChannel
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask SendPduAsync(ReadOnlyMemory<byte> data,CancellationToken ct)
        { Entered.TrySetResult(true); await Release.Task.WaitAsync(ct); }
        public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout,CancellationToken ct)
            => ValueTask.FromResult(new byte[]{0x62,0xF1,0x90,0x54});
    }
    [Fact]
    public async Task Verify_E2_T06_SameClientRejectsConcurrentExchange()
    {
        var channel=new BlockingChannel(); var client=new UdsClient(channel);
        var first=client.RequestAsync(Request(0x22,0xF1,0x90),Profile,default).AsTask();
        await channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second=await client.RequestAsync(Request(0x22,0xF1,0x90),Profile,default);
        Assert.Equal(Outcome.Rejected,second.Outcome);
        channel.Release.SetResult(true);
        Assert.Equal(Outcome.Accepted,(await first).Outcome);
        Assert.Contains(client.Trace.Snapshot(),e=>e.Note=="UDS_ENDPOINT_BUSY");
    }
    [Fact]
    public async Task PhysicalChannelRejectsMutationWithoutSending()
    {
        var channel=new BlockingChannel(); var client=new UdsClient(channel);
        var response=await client.RequestAsync(Request(0x34,0,0x44),Profile,default);
        Assert.Equal(Outcome.Rejected,response.Outcome);
        Assert.False(channel.Entered.Task.IsCompleted);
    }
}
