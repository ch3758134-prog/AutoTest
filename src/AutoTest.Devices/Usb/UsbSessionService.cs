using System.Diagnostics;

namespace AutoTest.Devices.Usb;

public enum UsbSessionState { Disconnected, Connected, Faulted }

/// <summary>All times are HOST arrival times, never device synchronized times.</summary>
public sealed record UsbTraceRecord(DateTimeOffset Utc, long MonotonicTicks, long Generation,
    string Kind, string RawHex, ushort? Sequence, byte? Command, byte? Status, string? Detail);

/// <summary>Single protocol session reused by mock and production links; no parallel USB parser.</summary>
public sealed class UsbSessionService : IAsyncDisposable
{
    private readonly IUsbByteLink _link;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly PendingRequestRegistry _requests = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _stop;
    private Task? _reader;
    private long _generation;
    private UsbSessionState _state = UsbSessionState.Disconnected;
    public event Action<UsbResponse>? AsynchronousResponse;
    public event Action<UsbSessionState>? StateChanged;
    public event Action<UsbTraceRecord>? TraceRecorded;

    public UsbSessionService(IUsbByteLink link, long generationStart = 0)
    {
        if (generationStart < 0) throw new ArgumentOutOfRangeException(nameof(generationStart));
        _link = link;
        _generation = generationStart;
    }
    public long Generation => Interlocked.Read(ref _generation);
    public UsbSessionState State { get { lock (_sync) return _state; } }

    private void Trace(string kind, long generation, string? raw = null, ushort? sequence = null,
        byte? command = null, byte? status = null, string? detail = null)
    {
        var record = new UsbTraceRecord(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(),
            generation, kind, raw ?? "", sequence, command, status, detail);
        foreach (Action<UsbTraceRecord> observer in TraceRecorded?.GetInvocationList()
                     .Cast<Action<UsbTraceRecord>>() ?? Enumerable.Empty<Action<UsbTraceRecord>>())
        { try { observer(record); } catch { /* diagnostic observers never break USB */ } }
    }
    private void ChangeState(UsbSessionState state)
    {
        lock (_sync) _state = state;
        foreach (Action<UsbSessionState> observer in StateChanged?.GetInvocationList()
                     .Cast<Action<UsbSessionState>>() ?? Enumerable.Empty<Action<UsbSessionState>>())
        { try { observer(state); } catch { } }
    }
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (State != UsbSessionState.Disconnected)
                throw new InvalidOperationException("Disconnect before reconnect.");
            try { await _link.OpenAsync(ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Trace("CONNECT_ERROR", Generation, detail: ex.Message);
                throw;
            }
            var generation = Interlocked.Increment(ref _generation);
            var stop = new CancellationTokenSource();
            _stop = stop;
            ChangeState(UsbSessionState.Connected);
            Trace("CONNECTED", generation);
            _reader = Task.Run(() => ReadLoopAsync(generation, stop.Token));
        }
        finally { _lifecycle.Release(); }
    }
    private async Task ReadLoopAsync(long generation, CancellationToken ct)
    {
        var decoder = new UsbFrameDecoder();
        var data = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _link.ReadAsync(data, ct).ConfigureAwait(false);
                if (n <= 0) throw new IOException("USB link returned EOF (native idle timeouts must not return EOF).");
                if (n > data.Length) throw new IOException("USB link returned excessive data.");
                Trace("RX_BYTES", generation, Convert.ToHexString(data.AsSpan(0, n)));
                foreach (var response in decoder.Feed(data.AsSpan(0, n)))
                {
                    if (generation != Generation) return;
                    Trace("RX_FRAME", generation,
                        Convert.ToHexString(UsbWireCodec.EncodeResponse(response.Sequence, response.Command,
                            response.Status, response.Payload)), response.Sequence, response.Command, response.Status);
                    if (response.Sequence == 0)
                    {
                        foreach (Action<UsbResponse> subscriber in AsynchronousResponse?.GetInvocationList()
                                     .Cast<Action<UsbResponse>>() ?? Enumerable.Empty<Action<UsbResponse>>())
                        { try { subscriber(response); } catch (Exception e) { Trace("SUBSCRIBER_ERROR", generation, detail:e.Message); } }
                    }
                    else if (!_requests.TryComplete(generation, response))
                    {
                        Trace("UNMATCHED_RESPONSE", generation, sequence: response.Sequence,
                            command: response.Command, status: response.Status,
                            detail: "Unknown, late, stale or wrong-command response; ignored.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation == Generation)
            {
                Trace("READ_FAULT", generation, detail: ex.ToString());
                ChangeState(UsbSessionState.Faulted);
                _requests.FailGeneration(generation, ex);
            }
        }
        finally { Trace("RX_LOOP_ENDED", generation); }
    }

    public async Task<UsbResponse> RequestAsync(byte command, ReadOnlyMemory<byte> data,
        TimeSpan timeout, CancellationToken ct = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (State != UsbSessionState.Connected) throw new IOException("USB link not connected.");
        long generation = Generation;
        var (sequence, completion) = _requests.Register(generation, command);
        // Observe failures when an OUT write was denied before awaiting completion.
        _ = completion.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await _write.WaitAsync(timer.Token).ConfigureAwait(false);
            try
            {
                if (generation != Generation || State != UsbSessionState.Connected)
                    throw new IOException("USB session changed during write.");
                var raw = UsbWireCodec.EncodeRequest(sequence, command, data.Span);
                Trace("TX_REQUEST", generation, Convert.ToHexString(raw), sequence, command,
                    detail: "Pre-write request intent; not yet proof of USB transmission.");
                await _link.WriteAsync(raw, timer.Token).ConfigureAwait(false);
                Trace("TX_WRITTEN", generation, Convert.ToHexString(raw), sequence, command,
                    detail: "WinUSB WritePipe completed; not proof of peripheral physical action.");
            }
            finally { _write.Release(); }
            var response = await completion.WaitAsync(timer.Token).ConfigureAwait(false);
            if (generation != Generation) throw new IOException("Response belongs to a stale connection.");
            if (response.Status != 0) throw new UsbDeviceStatusException(response.Command, response.Status);
            return response;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Trace("REQUEST_TIMEOUT", generation, sequence: sequence, command: command,
                detail: $"Timeout {timeout.TotalMilliseconds:0} ms; physical state UNKNOWN for commands with side effects.");
            throw new TimeoutException("USB request timed out; physical side effect is unknown.", ex);
        }
        catch (Exception ex)
        {
            Trace("REQUEST_ERROR", generation, sequence: sequence, command: command, detail: ex.Message);
            throw;
        }
        finally { _requests.Fail(generation, sequence, new IOException("Request ended before response.")); }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long previous = Generation;
            Interlocked.Increment(ref _generation); // invalidate read loop before closing native handle
            var stop = _stop;
            _stop = null;
            stop?.Cancel();
            _requests.FailGeneration(previous, new IOException("USB session disconnected."));
            try { await _link.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
            finally
            {
                if (_reader is not null)
                { try { await _reader.ConfigureAwait(false); } catch { /* already in trace */ } }
                stop?.Dispose();
                _reader = null;
                ChangeState(UsbSessionState.Disconnected);
                Trace("DISCONNECTED", Generation);
            }
        }
        finally { _lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        await _link.DisposeAsync().ConfigureAwait(false);
        _write.Dispose(); _lifecycle.Dispose();
    }
}

public sealed class UsbDeviceStatusException : IOException
{
    public byte Command { get; }
    public byte Status { get; }
    public UsbDeviceStatusException(byte command, byte status)
        : base($"USB command 0x{command:X2} returned firmware status 0x{status:X2}.")
    { Command = command; Status = status; }
}
