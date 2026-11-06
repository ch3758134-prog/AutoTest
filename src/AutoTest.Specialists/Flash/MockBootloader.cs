using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AutoTest.Specialists.Uds;

namespace AutoTest.Specialists.Flash;

// Pure in-memory ECU model. No reference to ICanFrameHub, USB or any station IO.
public sealed class MockBootloader
{
    private readonly Dictionary<uint,byte> _memory = new();
    private readonly Dictionary<byte,Queue<byte>> _nrc = new();
    private readonly HashSet<byte> _pending = [];
    private readonly uint _start;
    private readonly ulong _end;
    private readonly int _maximumTransferData;
    private bool _programmingSession, _unlocked, _downloading, _downloadFinished;
    private uint _writeAddress; private int _expectedLength, _written;
    private byte _nextCounter = 1;
    private static readonly byte[] Seed = [0x43,0x21,0x91,0x07];
    public string Identity { get; }
    public bool Disconnected { get; private set; }
    public int TransferBlocks { get; private set; }
    public int DisconnectBeforeBlock { get; set; } = -1;
    public bool IgnoreRequests { get; set; }
    public MockBootloader(string identity,uint memoryStart = 0x1000,uint memoryLength = 0x10000,
        int maximumTransferData = 128)
    {
        if (string.IsNullOrWhiteSpace(identity) || memoryLength == 0 ||
            (ulong)memoryStart + memoryLength > 0x100000000UL || maximumTransferData is < 1 or > 2048)
            throw new ArgumentException("Invalid mock bootloader configuration.");
        Identity = identity; _start = memoryStart; _end = (ulong)memoryStart + memoryLength;
        _maximumTransferData = maximumTransferData;
    }
    public void SimulateNrc(byte sid,byte nrc,int count = 1)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (!_nrc.TryGetValue(sid,out var queue)) _nrc[sid] = queue = new();
        for(int i=0;i<count;i++) queue.Enqueue(nrc);
    }
    public void SimulatePendingOnce(byte sid) => _pending.Add(sid);
    public void Disconnect() => Disconnected = true;
    public void ReconnectMock()
    {
        Disconnected=false; _programmingSession=false; _unlocked=false;
        _downloading=false; _downloadFinished=false; TransferBlocks=0;
    }
    public byte[] ReadMemory(uint address,int length)
    {
        EnsureRange(address,length);
        return Enumerable.Range(0,length).Select(i=>_memory.GetValueOrDefault((uint)((ulong)address+(uint)i),(byte)0xFF)).ToArray();
    }
    private void EnsureRange(uint address,int length)
    {
        if (length <= 0 || address < _start || (ulong)address+(ulong)length > _end)
            throw new ArgumentOutOfRangeException(nameof(address),"Mock memory outside configured region.");
    }
    private static byte[] Nrc(byte sid,byte code) => [0x7F,sid,code];
    public byte[] ReceiveRequest(ReadOnlySpan<byte> pdu)
    {
        if (Disconnected) throw new IOException("Injected mock disconnect.");
        if (pdu.IsEmpty) throw new IsoTpProtocolException("Empty UDS request.");
        byte sid=pdu[0];
        if (_nrc.TryGetValue(sid,out var queue) && queue.Count>0) return Nrc(sid,queue.Dequeue());
        switch(sid)
        {
            case 0x10:
                if (pdu.Length != 2) return Nrc(sid,0x13);
                if (pdu[1] != 0x02 && pdu[1] != 0x01) return Nrc(sid,0x12);
                _programmingSession = pdu[1] == 0x02; _unlocked = false;
                return [0x50,pdu[1],0x00,0x32,0x01,0xF4];
            case 0x22:
                if (pdu.Length != 3) return Nrc(sid,0x13);
                if (pdu[1] == 0xF1 && pdu[2] == 0x90)
                    return [0x62,0xF1,0x90,..Encoding.ASCII.GetBytes(Identity)];
                return Nrc(sid,0x31);
            case 0x19: return pdu.Length >= 2 ? [0x59,pdu[1],0] : Nrc(sid,0x13);
            case 0x3E: return pdu.Length == 2 ? [0x7E,pdu[1]] : Nrc(sid,0x13);
            case 0x27:
                if (!_programmingSession) return Nrc(sid,0x7E);
                if (pdu.Length == 2 && pdu[1] == 1) return [0x67,1,..Seed];
                if (pdu.Length == 6 && pdu[1] == 2
                    && pdu.Slice(2).SequenceEqual(Seed.Select(x=>(byte)(x ^ 0xA5)).ToArray()))
                { _unlocked=true; return [0x67,2]; }
                return Nrc(sid,0x35);
            case 0x34:
                if (!_programmingSession || !_unlocked) return Nrc(sid,0x33);
                if (pdu.Length != 11 || pdu[1] != 0 || pdu[2] != 0x44) return Nrc(sid,0x13);
                uint start=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(pdu.Slice(3,4));
                uint size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(pdu.Slice(7,4));
                if (size > int.MaxValue) return Nrc(sid,0x31);
                try { EnsureRange(start,(int)size); } catch (ArgumentOutOfRangeException) {return Nrc(sid,0x31);}
                _writeAddress=start; _expectedLength=(int)size; _written=0; _nextCounter=1;
                _downloading=true; _downloadFinished=false;
                // Negative test retains overwritten bytes only in the mock model.
                for(ulong i=start;i<(ulong)start+size;i++) _memory.Remove((uint)i);
                int maxBlockLength = _maximumTransferData+2; // SID and blockSequenceCounter included
                return [0x74,0x20,(byte)(maxBlockLength>>8),(byte)maxBlockLength];
            case 0x36:
                if (!_downloading) return Nrc(sid,0x24);
                if (pdu.Length is < 3 || pdu.Length > _maximumTransferData+2) return Nrc(sid,0x13);
                if (pdu[1] != _nextCounter) return Nrc(sid,0x73);
                if (DisconnectBeforeBlock == TransferBlocks+1) {Disconnected=true;throw new IOException("Injected power loss.");}
                int n=pdu.Length-2;
                if ((long)_written+n > _expectedLength) return Nrc(sid,0x31);
                for (int i=0;i<n;i++) _memory[(uint)((ulong)_writeAddress+(uint)(_written+i))]=pdu[i+2];
                _written+=n; _nextCounter++; TransferBlocks++;
                return [0x76,pdu[1]];
            case 0x37:
                if (!_downloading || pdu.Length != 1) return Nrc(sid,0x24);
                if (_written != _expectedLength) return Nrc(sid,0x72);
                _downloading=false; _downloadFinished=true;
                return [0x77];
            case 0x31:
                // Test-only hash routine FF00, never an assumed OEM service.
                if (!_downloadFinished || pdu.Length != 36 || pdu[1] != 1 || pdu[2] != 0xFF || pdu[3] != 0x00)
                    return Nrc(sid,0x31);
                var actual=SHA256.HashData(ReadMemory(_writeAddress,_expectedLength));
                return CryptographicOperations.FixedTimeEquals(actual,pdu.Slice(4))
                    ? [0x71,0x01,0xFF,0x00] : Nrc(sid,0x72);
            case 0x11:
                if (pdu.Length != 2 || pdu[1] != 1) return Nrc(sid,0x12);
                _programmingSession=false; _unlocked=false; _downloading=false;
                return [0x51,1];
            default: return Nrc(sid,0x11);
        }
    }
    public bool TakePending(byte sid) => _pending.Remove(sid);
}

// In-process paired frame links ensure the mock ECU uses the exact same
// ISO-TP codec and conversation as the real CAN adapter. No hardware access.
internal sealed class MockIsoTpFrameLink : IIsoTpFrameLink
{
    private readonly Channel<byte[]> _incoming;
    private readonly Channel<byte[]> _outgoing;
    public System.Collections.Concurrent.ConcurrentQueue<byte[]> Sent { get; } = new();
    private MockIsoTpFrameLink(Channel<byte[]> incoming,Channel<byte[]> outgoing)
    { _incoming=incoming; _outgoing=outgoing; }
    public static (MockIsoTpFrameLink Host,MockIsoTpFrameLink Ecu) CreatePair()
    {
        var h2e=Channel.CreateUnbounded<byte[]>(); var e2h=Channel.CreateUnbounded<byte[]>();
        return (new MockIsoTpFrameLink(e2h,h2e),new MockIsoTpFrameLink(h2e,e2h));
    }
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data,CancellationToken ct)
    {
        if (data.Length is < 1 or > 8) throw new IsoTpProtocolException("Mock CAN DLC invalid.");
        byte[] bytes=data.ToArray();
        await _outgoing.Writer.WriteAsync(bytes,ct).ConfigureAwait(false);
        Sent.Enqueue(bytes);
    }
    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
        => await _incoming.Reader.ReadAsync(ct).ConfigureAwait(false);
    public void Complete(Exception? error = null)
    {
        _incoming.Writer.TryComplete(error);
        _outgoing.Writer.TryComplete(error);
    }
}

// Sealed transport is the only in-process link on which state-changing UDS
// is permitted by the G4 UdsClient safety policy.
public sealed class MockUdsMessageChannel : IUdsMessageChannel, IAsyncDisposable
{
    private readonly MockBootloader _bootloader;
    private readonly MockIsoTpFrameLink _hostLink;
    private readonly MockIsoTpFrameLink _ecuLink;
    private readonly IsoTpConversation _host;
    private readonly IsoTpConversation _ecu;
    private readonly CancellationTokenSource _stop=new();
    private readonly Task _worker;
    public TimeSpan ResponseLatency { get; set; } = TimeSpan.Zero;
    public byte[][] HostFrames() => _hostLink.Sent.Select(x=>x.ToArray()).ToArray();
    public byte[][] EcuFrames() => _ecuLink.Sent.Select(x=>x.ToArray()).ToArray();
    public MockUdsMessageChannel(MockBootloader bootloader)
    {
        _bootloader=bootloader ?? throw new ArgumentNullException(nameof(bootloader));
        (_hostLink,_ecuLink)=MockIsoTpFrameLink.CreatePair();
        var opts=new IsoTpOptions(TimeSpan.FromSeconds(2),ReceiveBlockSize:4);
        _host=new IsoTpConversation(_hostLink,opts);
        _ecu=new IsoTpConversation(_ecuLink,opts);
        _worker=Task.Run(RunEcuAsync);
    }
    private async Task RunEcuAsync()
    {
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                var request=await _ecu.ReceivePduAsync(TimeSpan.FromMinutes(2),_stop.Token).ConfigureAwait(false);
                if (_bootloader.IgnoreRequests) continue;
                var response=_bootloader.ReceiveRequest(request);
                if (_bootloader.TakePending(request[0]))
                {
                    if (ResponseLatency>TimeSpan.Zero) await Task.Delay(ResponseLatency,_stop.Token).ConfigureAwait(false);
                    await _ecu.SendPduAsync(new byte[]{0x7F,request[0],0x78},_stop.Token).ConfigureAwait(false);
                }
                if (ResponseLatency>TimeSpan.Zero) await Task.Delay(ResponseLatency,_stop.Token).ConfigureAwait(false);
                await _ecu.SendPduAsync(response,_stop.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(_stop.IsCancellationRequested) { }
        catch(Exception ex) { _hostLink.Complete(ex); _ecuLink.Complete(ex); }
    }
    public ValueTask SendPduAsync(ReadOnlyMemory<byte> data,CancellationToken ct) =>
        _host.SendPduAsync(data,ct);
    public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout,CancellationToken ct) =>
        _host.ReceivePduAsync(timeout,ct);
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _worker.ConfigureAwait(false); }
        catch(OperationCanceledException) { }
        _stop.Dispose();
    }
}
