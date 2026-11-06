namespace AutoTest.Devices.Usb;
public enum UsbSessionState { Disconnected, Connected, Faulted }
public sealed class UsbSessionService : IAsyncDisposable
{
    private readonly IUsbByteLink _link;
    private readonly SemaphoreSlim _write = new(1,1);
    private readonly PendingRequestRegistry _requests = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _stop;
    private Task? _reader;
    private long _generation;
    private UsbSessionState _state = UsbSessionState.Disconnected;
    public event Action<UsbResponse>? AsynchronousResponse;
    public event Action<UsbSessionState>? StateChanged;
    public UsbSessionService(IUsbByteLink link) => _link = link;
    public long Generation => Interlocked.Read(ref _generation);
    public UsbSessionState State { get { lock(_sync) return _state; } }
    private void ChangeState(UsbSessionState state) { lock(_sync) _state=state; StateChanged?.Invoke(state); }
    public async Task ConnectAsync(CancellationToken ct=default)
    {
        if(State!=UsbSessionState.Disconnected) throw new InvalidOperationException("Disconnect before reconnect.");
        await _link.OpenAsync(ct);
        var generation=Interlocked.Increment(ref _generation);
        var stop = new CancellationTokenSource(); _stop=stop;
        ChangeState(UsbSessionState.Connected);
        _reader = ReadLoopAsync(generation,stop.Token);
    }
    private async Task ReadLoopAsync(long generation,CancellationToken ct)
    {
        var decoder=new UsbFrameDecoder(); var data=new byte[512];
        try
        {
            while(!ct.IsCancellationRequested)
            {
                int n=await _link.ReadAsync(data,ct);
                if(n<=0) throw new IOException("USB link ended.");
                foreach(var response in decoder.Feed(data.AsSpan(0,n)))
                {
                    if(generation!=Generation) return;
                    if(response.Sequence==0) AsynchronousResponse?.Invoke(response);
                    else _requests.TryComplete(generation,response);
                }
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
        catch(Exception ex)
        {
            if(generation==Generation) { ChangeState(UsbSessionState.Faulted); _requests.FailGeneration(generation,ex); }
        }
    }
    public async Task<UsbResponse> RequestAsync(byte command, ReadOnlyMemory<byte> data,TimeSpan timeout,CancellationToken ct=default)
    {
        if(timeout<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if(State!=UsbSessionState.Connected) throw new IOException("USB link not connected.");
        var generation=Generation;
        var (sequence,completion)=_requests.Register(generation,command);
        using var timer=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await _write.WaitAsync(timer.Token);
            try
            {
                if(generation!=Generation || State!=UsbSessionState.Connected) throw new IOException("Session changed.");
                await _link.WriteAsync(UsbWireCodec.EncodeRequest(sequence,command,data.Span),timer.Token);
            }
            finally { _write.Release(); }
            var response=await completion.WaitAsync(timer.Token);
            if(generation!=Generation) throw new IOException("Response belongs to a disconnected session.");
            if(response.Status!=0) throw new UsbDeviceStatusException(response.Command,response.Status);
            return response;
        }
        catch(OperationCanceledException ex) when(!ct.IsCancellationRequested) { throw new TimeoutException("USB request timed out; side effect is unknown.",ex); }
        finally { _requests.Fail(generation,sequence,new IOException("Request ended before response.")); }
    }
    public async Task DisconnectAsync(CancellationToken ct=default)
    {
        var generation=Generation;
        Interlocked.Increment(ref _generation); // invalidate stale receive loop before close.
        var stop=_stop; _stop=null; stop?.Cancel();
        _requests.FailGeneration(generation,new IOException("USB session disconnected."));
        try { await _link.CloseAsync(ct); } finally
        {
            if(_reader is not null) { try { await _reader; } catch { /* read fault already projected */ } }
            stop?.Dispose(); _reader=null; ChangeState(UsbSessionState.Disconnected);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(); await _link.DisposeAsync(); _write.Dispose();
    }
}
public sealed class UsbDeviceStatusException : IOException
{
    public byte Command {get;}
    public byte Status {get;}
    public UsbDeviceStatusException(byte command,byte status) : base($"USB command 0x{command:X2} returned status 0x{status:X2}.")
    { Command=command;Status=status; }
}
