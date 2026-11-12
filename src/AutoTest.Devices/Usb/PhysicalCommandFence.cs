using System.Buffers.Binary;
namespace AutoTest.Devices.Usb;

/// <summary>Transport-level fail-closed guard. It applies even when a caller bypasses the debug UI
/// and calls UsbSessionService directly. A CAN lease is NOT physical authorization.</summary>
public sealed class PhysicalCommandFence
{
    private readonly object _sync = new();
    private byte[]? _singleCanPayload;
    private DateTimeOffset _canExpiry;
    private long _authorizedConnection;
    private HashSet<(byte Port, bool Extended, uint Id)>? _runCan;
    private bool _runIgn;
    private int _remainingWrites;
    private DateTimeOffset _runExpiry;
    private long _runGeneration;
    private string? _runFingerprint;

    /// <summary>
    /// One deliberate physical-run authorization. Limited to the exact CAN arbitration
    /// IDs chosen at preparation, a single live USB generation and a bounded duration.
    /// No implicit approval by a software configuration or a CAN lease.
    /// </summary>
    public void AuthorizePhysicalRun(string fingerprint, string typedConfirmation,
        IEnumerable<(byte Port, bool Extended, uint Id)> allowedCan,
        bool allowIgn, long generation, int maxWrites = 1000)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64 ||
            !fingerprint.All(Uri.IsHexDigit) ||
            typedConfirmation != "AUTHORIZE TEST RUN " + fingerprint)
            throw new UnauthorizedAccessException("Exact plan SHA256 consent is required.");
        if (generation <= 0 || maxWrites is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxWrites));
        var entries = allowedCan.ToHashSet();
        if (entries.Any(x => x.Port > 1 || x.Id > (x.Extended ? 0x1FFFFFFFu : 0x7FFu)))
            throw new ArgumentException("Invalid allowed CAN frame.");
        lock (_sync)
        {
            if (_runFingerprint is not null)
                throw new InvalidOperationException("Another physical run already authorized.");
            _runCan = entries;
            _runIgn = allowIgn;
            _remainingWrites = maxWrites;
            _runExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
            _runGeneration = generation;
            _runFingerprint = fingerprint;
        }
    }

    public void RevokePhysicalRun()
    {
        lock (_sync)
        {
            _runCan = null;
            _runIgn = false;
            _runGeneration = 0;
            _runExpiry = default;
            _runFingerprint = null;
            _remainingWrites = 0;
        }
    }

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

    public void AssertAllowedAndConsume(ReadOnlySpan<byte> rawRequest, long connectionGeneration)
    {
        if (!UsbWireCodec.TryDecodeRequest(rawRequest, out var request))
            throw new UnauthorizedAccessException("Rejected malformed USB request before physical write.");
        if (request.Sequence == 0)
            throw new UnauthorizedAccessException("Host requests must never use reserved SEQ=0.");
        if (IsReadOnly(request.Command, request.Payload)) return;
        lock (_sync)
        {
            if (request.Command == MainControllerCodec.CanSend && _singleCanPayload is not null &&
                _authorizedConnection == connectionGeneration && DateTimeOffset.UtcNow < _canExpiry &&
                request.Payload.AsSpan().SequenceEqual(_singleCanPayload))
            {
                _singleCanPayload = null; // one shot, consumed BEFORE I/O, including failed writes.
                return;
            }
        }
        lock (_sync)
        {
            if (_runFingerprint is not null && _runCan is not null &&
                _runGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _runExpiry && _remainingWrites > 0)
            {
                bool allowed = false;
                if (request.Command == MainControllerCodec.CanSend && request.Payload.Length == 15 &&
                    request.Payload[0] <= 1 && request.Payload[1] <= 1 &&
                    request.Payload[6] <= 8)
                {
                    uint id = BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(2, 4));
                    allowed = _runCan.Contains((request.Payload[0], request.Payload[1] == 1, id));
                }
                else if (request.Command == MainControllerCodec.SetIgn &&
                    request.Payload.Length == 1 && request.Payload[0] <= 1)
                    allowed = _runIgn;
                if (allowed) { _remainingWrites--; return; }
            }
        }
        throw new UnauthorizedAccessException($"USB command 0x{request.Command:X2} is physically blocked by default.");
    }

    public void Reset()
    {
        lock (_sync)
        {
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _runCan = null; _runIgn = false; _remainingWrites = 0;
            _runExpiry = default; _runGeneration = 0; _runFingerprint = null;
        }
    }
}
