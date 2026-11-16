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


    // Separate one-DID grant. Never piggyback on the broad physical test-run whitelist.
    private sealed class DidGrant
    {
        public required uint TxId, RxId;
        public required ushort Did;
        public required long Generation;
        public required DateTimeOffset Expires;
        public bool RequestSent;
        public bool ResponseStarted;
        public bool FlowPending;
        public int RemainingConsecutive;
        public int SinceFlow;
        public int ExpectedSequence = 1;
        public int FlowCount;
    }
    private DidGrant? _didGrant;

    public static string ReadDidConfirmation(uint txId, uint rxId, ushort did) =>
        $"AUTHORIZE DID 0x{did:X4} TX 0x{txId:X} RX 0x{rxId:X} CAN1";

    /// <summary>Single explicit 0x22 DID read on physical CAN1, one USB generation,
    /// bounded to 30 seconds. Cannot authorize 0x10, 0x27 or any programming SID.</summary>
    public void AuthorizeReadOnlyDid(uint txId, uint rxId, ushort did, string typedConfirmation, long generation)
    {
        if (generation <= 0 || txId > 0x1FFFFFFFu || rxId > 0x1FFFFFFFu ||
            txId == rxId || (txId > 0x7FFu) != (rxId > 0x7FFu))
            throw new ArgumentException("Invalid or mixed-size CAN1 UDS endpoint.");
        if (!string.Equals(typedConfirmation, ReadDidConfirmation(txId,rxId,did), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact DID/ID/CAN1 physical consent is required.");
        lock (_sync)
        {
            if (_didGrant is not null)
                throw new InvalidOperationException("A DID read already has a physical grant.");
            _didGrant = new DidGrant { TxId=txId, RxId=rxId, Did=did,
                Generation=generation, Expires=DateTimeOffset.UtcNow.AddSeconds(30) };
        }
    }
    public void RevokeReadOnlyDid() { lock (_sync) _didGrant=null; }

    /// <summary>Only UsbSessionService may feed decoded, CRC-checked 0x41 indications
    /// from the connected WinUSB reader. UI/callers cannot manufacture an FF grant.</summary>
    internal void ObserveAuthenticatedCanRx(ReadOnlySpan<byte> payload,long generation)
    {
        lock (_sync)
        {
            var d = _didGrant;
            if (d is null || !d.RequestSent || d.Generation != generation ||
                DateTimeOffset.UtcNow >= d.Expires || payload.Length != 15 ||
                payload[0] != 1 || payload[1] != (d.RxId > 0x7FFu ? 1 : 0) ||
                BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2,4)) != d.RxId)
                return;
            int dlc=payload[6];
            if (dlc is < 1 or > 8) { _didGrant=null; return; }
            byte pci=payload[7];
            if (!d.ResponseStarted)
            {
                // An ECU 0x7F 0x22 0x78 pending SF is permitted but grants no FC.
                if ((pci & 0xF0) != 0x10) return;
                if (dlc != 8) { _didGrant=null; return; }
                int size=((pci&0x0F)<<8)|payload[8];
                // FirstFrame must actually begin the positive response 0x62 + exact DID.
                if (size is < 8 or > 4095 || payload[9] != 0x62 ||
                    payload[10] != (byte)(d.Did>>8) || payload[11] != (byte)d.Did)
                { _didGrant=null; return; }
                d.ResponseStarted=true;
                d.RemainingConsecutive=(size-6+6)/7;
                d.FlowPending=true;
                return;
            }
            if ((pci & 0xF0) != 0x20 || d.FlowPending || d.RemainingConsecutive <= 0 ||
                (pci & 0x0F) != d.ExpectedSequence || dlc < 2)
            { _didGrant=null; return; }
            d.RemainingConsecutive--;
            d.ExpectedSequence=(d.ExpectedSequence+1)&0xF;
            d.SinceFlow++;
            if (d.RemainingConsecutive>0 && d.SinceFlow==8)
            { d.FlowPending=true; d.SinceFlow=0; }
        }
    }

    private bool TryConsumeReadDid(UsbRequest request,long generation)
    {
        var d=_didGrant;
        if (d is null || d.Generation!=generation || DateTimeOffset.UtcNow>=d.Expires ||
            request.Command!=MainControllerCodec.CanSend || request.Payload.Length!=15)
            return false;
        var p=request.Payload;
        if (p[0]!=1 || p[1]!=(d.TxId>0x7FFu?1:0) ||
            BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2,4))!=d.TxId)
            return false;
        // No CAN padding tolerance: the exact ISO-TP bytes and DLC must match.
        if (!d.RequestSent && p[6]==4 && p[7]==3 && p[8]==0x22 &&
            p[9]==(byte)(d.Did>>8) && p[10]==(byte)d.Did &&
            p.AsSpan(11,4).SequenceEqual(new byte[4]))
        { d.RequestSent=true; return true; }
        if (d.RequestSent && d.ResponseStarted && d.FlowPending &&
            d.RemainingConsecutive>0 && d.FlowCount<75 && p[6]==3 &&
            p[7]==0x30 && p[8]==8 && p[9]==0 &&
            p.AsSpan(10,5).SequenceEqual(new byte[5]))
        { d.FlowCount++; d.FlowPending=false; return true; }
        return false;
    }

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
            if (TryConsumeReadDid(request, connectionGeneration)) return;
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
            _didGrant = null;
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _runCan = null; _runIgn = false; _remainingWrites = 0;
            _runExpiry = default; _runGeneration = 0; _runFingerprint = null;
        }
    }
}
