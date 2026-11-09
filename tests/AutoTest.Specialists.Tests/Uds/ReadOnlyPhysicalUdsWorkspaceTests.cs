using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;
using AutoTest.Specialists.Uds.Workbench;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class ReadOnlyPhysicalUdsWorkspaceTests
{
    private static UdsEndpointProfile Endpoint(uint tx = 0x7E0,uint rx=0x7E8) =>
        new(BusRole.DutCan,tx,rx,TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),"Normal");
    private static ReadOnlyPhysicalConnection Context(
        bool authorized=true,bool trace=true,long generation=7) =>
        new("FAKE_STATION",generation,Endpoint(),authorized,trace,
            DateTimeOffset.UtcNow.AddMinutes(5));
    private static UdsRequest Request(params byte[] pdu) =>
        new(ImmutableArray.CreateRange(pdu),Guid.NewGuid());

    private sealed class ScriptedClient : IUdsClient
    {
        public int Calls;
        public byte[] Response = [0x62,0xF1,0x90,0x41];
        public Outcome Outcome = Outcome.Accepted;
        public Action? OnRequest;
        public ValueTask<UdsExchangeResult> RequestAsync(UdsRequest request,
            UdsEndpointProfile profile,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            OnRequest?.Invoke();
            byte? nrc=Response.Length==3 && Response[0]==0x7F ? Response[2] : null;
            return ValueTask.FromResult(new UdsExchangeResult(
                Outcome,request.Pdu,Response.ToImmutableArray(),nrc,
                ImmutableArray<EvidenceRef>.Empty,
                TimePoint.FromHostArrival(DateTimeOffset.UtcNow,0)));
        }
    }

    private sealed class FakeSession(ReadOnlyPhysicalConnection context,ScriptedClient client,
        Func<Guid,IReadOnlyList<PhysicalUdsBusEvent>> frames) : IReadOnlyPhysicalUdsSession
    {
        public ReadOnlyPhysicalConnection Connection { get; } = context;
        public IUdsClient Client => client;
        public IReadOnlyList<PhysicalUdsBusEvent> SnapshotCan(Guid id) => frames(id);
        public IReadOnlyList<UdsTraceEntry> SnapshotUds(Guid id) =>
            [new(id,UdsTraceDirection.Response,DateTimeOffset.UtcNow,
                client.Response.ToArray(),"FAKE_UDS_RESPONSE")];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProvider : IReadOnlyPhysicalUdsSessionProvider
    {
        public ReadOnlyPhysicalConnection? Current {get;set;}=Context();
        public readonly ScriptedClient Client = new();
        public int OpenCount;
        public bool MissingRx;
        public long LostFrames;
        public bool TxAck=true;
        public async ValueTask<IReadOnlyPhysicalUdsSession> OpenAsync(
            ReadOnlyDiagnosticSessionSpec spec,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            OpenCount++;
            Assert.Equal(BusRole.DutCan,spec.Endpoint.Role);
            Assert.Equal(Current!.SessionGeneration,spec.ExpectedSessionGeneration);
            Assert.Equal("FAKE_STATION",spec.StationIdentity);
            Assert.Equal(2,spec.AllowedReadOnlySids.Count);
            Assert.True(spec.AllowedReadOnlySids.Contains(0x19));
            Assert.True(spec.AllowedReadOnlySids.Contains(0x22));
            await Task.Yield();
            return new FakeSession(Current,Client,_ =>
            {
                var now=DateTimeOffset.UtcNow;
                var frames=new List<PhysicalUdsBusEvent> {
                    new(now,"TX","CAN",BusRole.DutCan,0x7E0,false,
                        "03 22 F1 90","FAKE_DEVICE_TX",Current.SessionGeneration,LostFrames,TxAck)
                };
                if (!MissingRx)
                    frames.Add(new(now,"RX","CAN",BusRole.DutCan,0x7E8,false,
                        "04 62 F1 90 41","FAKE_DEVICE_RX",
                        Current.SessionGeneration,LostFrames));
                return frames;
            });
        }
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x27)]
    [InlineData(0x2E)]
    [InlineData(0x31)]
    [InlineData(0x34)]
    [InlineData(0x36)]
    [InlineData(0x37)]
    [InlineData(0x3E)]
    [InlineData(0x23)]
    public async Task StateChangingAndUnapprovedSidsRejectBeforeSessionOpen(byte sid)
    {
        var fake=new FakeProvider();
        var workspace=new ReadOnlyPhysicalUdsWorkspace(fake);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>workspace.RequestAsync(Request(sid,0x01,0x00),Endpoint()));
        Assert.Equal(0,fake.OpenCount);
        Assert.Equal(0,fake.Client.Calls);
    }

    [Fact]
    public async Task OnlyApprovedReadRequestUsesInjectedExistingClient()
    {
        var fake=new FakeProvider();
        var workspace=new ReadOnlyPhysicalUdsWorkspace(fake);
        var id=Request(0x22,0xF1,0x90);
        var result=await workspace.RequestAsync(id,Endpoint());
        Assert.Equal(Outcome.Accepted,result.Result.Outcome);
        Assert.Equal(id.CorrelationId,result.CorrelationId);
        Assert.True(result.RawBusEvidenceVerified);
        Assert.Equal("PHYSICAL_CAN_TRACE_BOUND",result.DiagnosticCode);
        Assert.Equal(1,fake.OpenCount);
        Assert.Equal(1,fake.Client.Calls);
        Assert.Equal(2,result.CanTrace.Count);
    }

    [Fact]
    public async Task MissingCanRxOrTxAckOrLossNeverProducesAcceptedPhysicalProof()
    {
        var fake=new FakeProvider { MissingRx=true };
        var workspace=new ReadOnlyPhysicalUdsWorkspace(fake);
        var result=await workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint());
        Assert.Equal(Outcome.Uncertain,result.Result.Outcome);
        Assert.False(result.RawBusEvidenceVerified);

        fake.MissingRx=false;fake.TxAck=false;
        result=await workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint());
        Assert.Equal(Outcome.Uncertain,result.Result.Outcome);
        fake.TxAck=true;fake.LostFrames=1;
        result=await workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint());
        Assert.Equal(Outcome.Uncertain,result.Result.Outcome);
    }

    [Fact]
    public async Task WrongEndpointAndGenerationAndNoAuthorizationRejectBeforeTx()
    {
        var fake=new FakeProvider();
        var workspace=new ReadOnlyPhysicalUdsWorkspace(fake);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint(rx:0x7E9)));
        Assert.Equal(0,fake.OpenCount);
        fake.Current=Context(authorized:false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint()));
        Assert.Equal(0,fake.Client.Calls);
        fake.Current=Context(authorized:true,generation:9);
        fake.Client.OnRequest=()=>fake.Current=Context(generation:10);
        await Assert.ThrowsAsync<IOException>(
            ()=>workspace.RequestAsync(Request(0x22,0xF1,0x90),Endpoint()));
        Assert.Equal(1,fake.Client.Calls);
    }

    [Fact]
    public async Task ReadDtcSidAllowedButMalformedReadDidRejected()
    {
        var fake=new FakeProvider();
        var workspace=new ReadOnlyPhysicalUdsWorkspace(fake);
        await Assert.ThrowsAsync<ArgumentException>(
            ()=>workspace.RequestAsync(Request(0x22,0xF1),Endpoint()));
        Assert.Equal(0,fake.OpenCount);
        var result=await workspace.RequestAsync(Request(0x19,0x02,0xFF),Endpoint());
        Assert.Equal(Outcome.Accepted,result.Result.Outcome);
        Assert.Equal(1,fake.Client.Calls);
    }

    private sealed class PendingChannel : IUdsMessageChannel
    {
        private readonly Queue<byte[]> _responses = new();
        public ValueTask SendPduAsync(ReadOnlyMemory<byte> data,CancellationToken ct)
        {
            _responses.Enqueue([0x7F,0x22,0x78]);
            _responses.Enqueue([0x62,0xF1,0x90,0x41]);
            return ValueTask.CompletedTask;
        }
        public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout,CancellationToken ct) =>
            ValueTask.FromResult(_responses.Dequeue());
    }

    [Fact]
    public async Task ExistingUdsClientConsumesPendingBeforeFinalPositive()
    {
        var client=new UdsClient(new PendingChannel());
        var id=Request(0x22,0xF1,0x90);
        var result=await client.RequestAsync(id,Endpoint(),CancellationToken.None);
        Assert.Equal(Outcome.Accepted,result.Outcome);
        Assert.Equal(2,client.Trace.Snapshot().Count(x=>
            x.Direction==UdsTraceDirection.Response && x.CorrelationId==id.CorrelationId));
    }
}
