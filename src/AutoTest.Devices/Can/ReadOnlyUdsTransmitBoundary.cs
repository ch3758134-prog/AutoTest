using AutoTest.Contracts;

namespace AutoTest.Devices.Can;

/// <summary>DEVICE-only physical endpoint configuration; INTEGRATION owns the PROJECT/UDS
/// profile mapping. This is NOT an additional shared Contracts DTO or ISO-TP implementation.</summary>
public sealed record ReadOnlyUdsPhysicalEndpoint(
    string ProfileRevision, uint RequestCanId, uint ResponseCanId, bool ExtendedId)
{
    public void Validate()
    {
        uint max = ExtendedId ? 0x1FFFFFFFu : 0x7FFu;
        if (string.IsNullOrWhiteSpace(ProfileRevision) || RequestCanId > max ||
            ResponseCanId > max || RequestCanId == ResponseCanId ||
            (ExtendedId && (RequestCanId <= 0x7FFu || ResponseCanId <= 0x7FFu)))
            throw new ArgumentException("A revision-bound, distinct DUT CAN1 Normal endpoint is required.");
    }
}

/// <summary>
/// Closed-by-default physical CAN1 UDS guard. Binds exactly one application request PDU,
/// one lease, request identity, current USB generation, IDs, time, and frame count.
/// Checks ISO-TP envelope only: the UDS specialist still owns segmentation/reassembly.
/// </summary>
internal sealed class ReadOnlyUdsTransmitBoundary
{
    private sealed class State
    {
        public required Guid RequestId;
        public required LeaseToken Lease;
        public required ReadOnlyUdsPhysicalEndpoint Endpoint;
        public required byte[] Pdu;
        public required long Generation;
        public required DateTimeOffset ExpiresUtc;
        public required int MaxFrames;
        public int Frames;
        public bool AwaitingControllerAck;
        public bool RequestCompleteAcked;
        public int RequestOffset;
        public int RequestNextSeq = 1;
        public int RequestCfAllowance;
        public bool RequestStarted;
        public bool RequestComplete;
        public bool ResponseFlowDue;
        public bool ResponseFlowGranted;
        public int ResponseBlockSize;
        public int ResponseBlockReceived;
        public int ResponseLength;
        public int ResponseBytesReceived;
        public int ResponseNextSeq = 1;
    }
    private sealed class Scope : IDisposable
    {
        private readonly ReadOnlyUdsTransmitBoundary _owner;
        private readonly Guid _requestId;
        private int _disposed;
        public Scope(ReadOnlyUdsTransmitBoundary owner, Guid requestId)
        { _owner = owner; _requestId = requestId; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.End(_requestId);
        }
    }

    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private State? _active;
    public ReadOnlyUdsTransmitBoundary(Func<DateTimeOffset>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public static void ValidateReadOnlyRequest(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length is < 2 or > 4095)
            throw new ArgumentException("Nonempty bounded UDS request required.");
        if (pdu[0] == 0x22)
        {
            if (pdu.Length < 3 || (pdu.Length - 1) % 2 != 0)
                throw new ArgumentException("0x22 requires one or more complete DID pairs.");
        }
        else if (pdu[0] == 0x19)
        {
            if (pdu.Length < 2 || (pdu[1] & 0x80) != 0)
                throw new ArgumentException("0x19 requires a non-suppressed read subfunction.");
        }
        else throw new UnauthorizedAccessException("Only physical read-only SID 0x19/0x22 is admitted.");
    }

    public IDisposable Begin(ReadOnlyUdsPhysicalEndpoint endpoint, ReadOnlySpan<byte> exactPdu,
        LeaseToken lease, Guid requestId, long generation, TimeSpan lifetime, int maxFrames)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.Validate();
        ValidateReadOnlyRequest(exactPdu);
        if (requestId == Guid.Empty || lease.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(lease.Owner) || generation <= 0 ||
            lease.SessionGeneration != generation)
            throw new UnauthorizedAccessException("A current, owned physical DUT lease and RequestId are required.");
        if (lifetime < TimeSpan.FromMilliseconds(200) || lifetime > TimeSpan.FromMinutes(2) ||
            maxFrames is < 2 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Bounded time and frame budget required.");
        var expiry = _utcNow() + lifetime;
        if (lease.ExpiresUtc <= expiry)
            throw new UnauthorizedAccessException("CAN transmit lease must outlive the diagnostic permit.");
        lock (_sync)
        {
            if (_active is not null && _active.ExpiresUtc > _utcNow())
                throw new InvalidOperationException("One physical diagnostic transaction at a time.");
            _active = new State
            {
                RequestId = requestId, Lease = lease, Endpoint = endpoint,
                Pdu = exactPdu.ToArray(), Generation = generation,
                ExpiresUtc = expiry, MaxFrames = maxFrames
            };
        }
        return new Scope(this, requestId);
    }

    public bool ConfirmControllerAck(Guid requestId, long generation)
    {
        lock (_sync)
        {
            var s = _active;
            if (s is null || s.RequestId != requestId || s.Generation != generation ||
                _utcNow() >= s.ExpiresUtc || !s.AwaitingControllerAck) return false;
            s.AwaitingControllerAck = false;
            if (s.RequestComplete) s.RequestCompleteAcked = true;
            return true;
        }
    }

    public void Revoke() { lock (_sync) _active = null; }

    private void End(Guid requestId)
    {
        lock (_sync) { if (_active?.RequestId == requestId) _active = null; }
    }

    private bool MatchState(LeaseToken lease, long generation, out State? state)
    {
        state = _active;
        if (state is null || state.Lease != lease || state.Generation != generation ||
            _utcNow() >= state.ExpiresUtc || _utcNow() >= lease.ExpiresUtc)
            return false;
        return true;
    }

    // Called under the controller's serialized TX gate, BEFORE any WinUSB write.
    // A rejected frame consumes no USB permission. Each successful authorization
    // consumes a bounded frame slot even if the subsequent USB write fails.
    public bool TryConsumeOutgoing(CanFrame frame, LeaseToken lease, long generation,
        out Guid requestId, out string error)
    {
        requestId = Guid.Empty; error = "UDS_TRANSACTION_NOT_AUTHORIZED";
        lock (_sync)
        {
            if (!MatchState(lease, generation, out var s) || s is null) return false;
            requestId = s.RequestId;
            if (frame.Role != BusRole.DutCan || frame.Id != s.Endpoint.RequestCanId ||
                frame.ExtendedId != s.Endpoint.ExtendedId ||
                frame.Data.IsDefault || frame.Dlc != frame.Data.Length || frame.Dlc is < 2 or > 8)
            { error = "UDS_ENDPOINT_MISMATCH"; return false; }
            if (s.AwaitingControllerAck) { error = "UDS_PREVIOUS_USB_ACK_REQUIRED"; return false; }
            if (s.Frames >= s.MaxFrames) { error = "UDS_FRAME_BUDGET_EXHAUSTED"; return false; }
            var data = frame.Data.ToArray();
            if (!s.RequestComplete)
            {
                byte[] expected;
                if (!s.RequestStarted)
                {
                    if (s.Pdu.Length <= 7)
                    {
                        expected = new byte[s.Pdu.Length + 1];
                        expected[0] = (byte)s.Pdu.Length;
                        s.Pdu.AsSpan().CopyTo(expected.AsSpan(1));
                    }
                    else
                    {
                        expected = new byte[8];
                        expected[0] = (byte)(0x10 | (s.Pdu.Length >> 8));
                        expected[1] = (byte)s.Pdu.Length;
                        s.Pdu.AsSpan(0, 6).CopyTo(expected.AsSpan(2));
                    }
                    if (!data.AsSpan().SequenceEqual(expected))
                    { error = "UDS_REQUEST_FRAME_MISMATCH"; return false; }
                    s.RequestStarted = true;
                    s.RequestOffset = s.Pdu.Length <= 7 ? s.Pdu.Length : 6;
                    s.RequestComplete = s.RequestOffset == s.Pdu.Length;
                }
                else
                {
                    if (s.RequestCfAllowance <= 0)
                    { error = "UDS_REQUEST_FLOWCONTROL_REQUIRED"; return false; }
                    int count = Math.Min(7, s.Pdu.Length - s.RequestOffset);
                    expected = new byte[count + 1];
                    expected[0] = (byte)(0x20 | s.RequestNextSeq);
                    s.Pdu.AsSpan(s.RequestOffset, count).CopyTo(expected.AsSpan(1));
                    if (!data.AsSpan().SequenceEqual(expected))
                    { error = "UDS_CONSECUTIVE_FRAME_MISMATCH"; return false; }
                    s.RequestOffset += count;
                    s.RequestNextSeq = (s.RequestNextSeq + 1) & 15;
                    if (s.RequestCfAllowance != int.MaxValue) s.RequestCfAllowance--;
                    s.RequestComplete = s.RequestOffset == s.Pdu.Length;
                }
            }
            else
            {
                // Only CTS FC for a previously observed, matching response FirstFrame;
                // no standalone FC, Wait, Overflow, arbitrary payload, or extra writes.
                if (!s.RequestCompleteAcked || !s.ResponseFlowDue ||
                    data.Length != 3 || data[0] != 0x30 ||
                    !(data[2] <= 0x7F || data[2] is >= 0xF1 and <= 0xF9))
                { error = "UDS_RESPONSE_FLOWCONTROL_NOT_DUE"; return false; }
                s.ResponseFlowDue = false;
                s.ResponseFlowGranted = true;
                s.ResponseBlockSize = data[1];
                s.ResponseBlockReceived = 0;
            }
            s.Frames++;
            s.AwaitingControllerAck = true;
            error = "";
            return true;
        }
    }

    /// <summary>Actual CAN1 RX observation only. Never synthesizes ECU response.
    /// The receive bridge invokes this before publishing to UDS clients.</summary>
    public void ObserveIncoming(CanObservation observation)
    {
        lock (_sync)
        {
            var s = _active;
            if (s is null || observation.SessionGeneration != s.Generation ||
                _utcNow() >= s.ExpiresUtc) return;
            if (observation.LostFrames != 0) { _active = null; return; }
            var f = observation.Frame;
            if (f.Role != BusRole.DutCan || f.Id != s.Endpoint.ResponseCanId ||
                f.ExtendedId != s.Endpoint.ExtendedId || f.Data.IsDefault ||
                f.Dlc != f.Data.Length || f.Dlc == 0) return;
            var d = f.Data.ToArray();
            if (!s.RequestComplete)
            {
                // ECU FC is required before every authorized group of request CFs.
                if (s.RequestStarted && s.Pdu.Length > 7 && d.Length >= 3 &&
                    (d[0] & 0xF0) == 0x30)
                {
                    if (d[0] == 0x30)
                        s.RequestCfAllowance = d[1] == 0 ? int.MaxValue : d[1];
                    else if (d[0] == 0x32) _active = null;
                }
                return;
            }
            int pci = d[0] >> 4;
            if (pci == 1 && d.Length == 8)
            {
                int len = ((d[0] & 15) << 8) | d[1];
                // Only a real response for the approved SID can enable an FC.
                if (len < 8 || (d[2] != (byte)(s.Pdu[0] + 0x40) &&
                    !(d[2] == 0x7F && d[3] == s.Pdu[0]))) return;
                // A positive 0x22 response must start with the requested DID.
                // A positive 0x19 response must echo the approved subfunction.
                if (d[2] == 0x62 &&
                    (d[3] != s.Pdu[1] || d[4] != s.Pdu[2])) return;
                if (d[2] == 0x59 && d[3] != s.Pdu[1]) return;
                s.ResponseLength = len; s.ResponseBytesReceived = 6;
                s.ResponseNextSeq = 1; s.ResponseFlowDue = true;
                s.ResponseFlowGranted = false; s.ResponseBlockReceived = 0;
                return;
            }
            if (pci != 2 || !s.ResponseFlowGranted || d.Length < 2 ||
                (d[0] & 15) != s.ResponseNextSeq || s.ResponseBytesReceived >= s.ResponseLength)
                return;
            s.ResponseNextSeq = (s.ResponseNextSeq + 1) & 15;
            s.ResponseBytesReceived += Math.Min(7, d.Length - 1);
            s.ResponseBlockReceived++;
            if (s.ResponseBytesReceived >= s.ResponseLength)
            {
                s.ResponseFlowGranted = false; s.ResponseFlowDue = false;
            }
            else if (s.ResponseBlockSize != 0 &&
                     s.ResponseBlockReceived >= s.ResponseBlockSize)
            {
                s.ResponseFlowGranted = false; s.ResponseFlowDue = true;
            }
        }
    }
}
