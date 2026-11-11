namespace AutoTest.Devices.Usb;

/// <summary>Transport-level fail-closed guard. It applies even when a caller bypasses the debug UI
/// and calls UsbSessionService directly. A CAN lease is NOT physical authorization.</summary>
public sealed class PhysicalCommandFence
{
    private readonly object _sync = new();
    private byte[]? _singleCanPayload;
    private DateTimeOffset _canExpiry;
    private long _authorizedConnection;
    private bool? _authorizedIgnOn;
    private DateTimeOffset _ignExpiry;
    private long _ignConnection;

    public static bool IsReadOnly(byte command, ReadOnlySpan<byte> payload) =>
        payload.IsEmpty && (command is 0xF0 or 0xF1 or 0x11 or 0x13 or 0x15 or 0x17 or 0x1F);

    /// <summary>Requires an exact typed confirmation, one payload and a living connection.
    /// Not used by B0/B1/B2 and never called automatically on reconnect.</summary>
    public void AuthorizeSingleCanFrame(ReadOnlySpan<byte> exact15Bytes, string typedConfirmation,
        long connectionGeneration)
    {
        if (exact15Bytes.Length != 15 || exact15Bytes[0] > 1 || exact15Bytes[1] > 1 || exact15Bytes[6] > 8)
            throw new ArgumentException("Invalid physical CAN payload.", nameof(exact15Bytes));
        string expectation = "AUTHORIZE CAN TX " + Convert.ToHexString(exact15Bytes);
        if (!string.Equals(typedConfirmation, expectation, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Explicit exact-frame CAN consent was not provided.");
        if (connectionGeneration <= 0) throw new InvalidOperationException("No live connection.");
        lock (_sync)
        {
            _singleCanPayload = exact15Bytes.ToArray();
            _authorizedConnection = connectionGeneration;
            _canExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    /// <summary>
    /// One-shot, session-bound IGN command authorization. SET_IGN=0x12 carries
    /// exactly one byte (1=ON / 0=OFF). It is NEVER authorized by a CAN lease.
    /// </summary>
    public void AuthorizeSingleIgn(bool on, string exactConfirmation,
        long connectionGeneration)
    {
        if (connectionGeneration <= 0) throw new InvalidOperationException("No live connection.");
        var expected = "AUTHORIZE IGN " + (on ? "ON" : "OFF") + " GEN " + connectionGeneration;
        if (!string.Equals(exactConfirmation, expected, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("IGN requires explicit per-operation approval.");
        lock (_sync)
        {
            _authorizedIgnOn = on;
            _ignConnection = connectionGeneration;
            _ignExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    public void RevokeSingleIgn()
    {
        lock (_sync) { _authorizedIgnOn = null; _ignConnection = 0; _ignExpiry = default; }
    }

    /// <summary>Revocation of a not-yet-consumed exact CAN grant; safe on cancellation.</summary>
    public void RevokeSingleCanFrame()
    {
        lock (_sync)
        {
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
        }
    }

    public void AssertAllowedAndConsume(ReadOnlySpan<byte> rawRequest, long connectionGeneration)
    {
        if (!UsbWireCodec.TryDecodeRequest(rawRequest, out var request))
            throw new UnauthorizedAccessException("Rejected malformed USB request before physical write.");
        if (request.Sequence == 0)
            throw new UnauthorizedAccessException("Host requests must never use reserved SEQ=0.");
        if (IsReadOnly(request.Command, request.Payload)) return;
        lock (_sync)
        {
            if (request.Command == MainControllerCodec.SetIgn &&
                request.Payload.Length == 1 && request.Payload[0] <= 1 &&
                _authorizedIgnOn.HasValue &&
                _authorizedIgnOn.Value == (request.Payload[0] == 1) &&
                _ignConnection == connectionGeneration &&
                DateTimeOffset.UtcNow < _ignExpiry)
            {
                _authorizedIgnOn = null; // one shot, consumed BEFORE physical I/O
                _ignConnection = 0;
                _ignExpiry = default;
                return;
            }
            if (request.Command == MainControllerCodec.CanSend && _singleCanPayload is not null &&
                _authorizedConnection == connectionGeneration && DateTimeOffset.UtcNow < _canExpiry &&
                request.Payload.AsSpan().SequenceEqual(_singleCanPayload))
            {
                _singleCanPayload = null; // one shot, consumed BEFORE I/O, including failed writes.
                return;
            }
        }
        throw new UnauthorizedAccessException($"USB command 0x{request.Command:X2} is physically blocked by default.");
    }

    public void Reset()
    {
        lock (_sync)
        {
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _authorizedIgnOn = null; _ignConnection = 0; _ignExpiry = default;
        }
    }
}
