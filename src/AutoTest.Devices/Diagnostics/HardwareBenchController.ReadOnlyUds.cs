using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>The existing main-controller WinUSB session owns the only real CAN1 TX path.
/// No UDS/ISO-TP parser is implemented here: the specialist owns the conversation.
/// All non-authorized physical CAN sends remain DENIED.</summary>
public sealed partial class HardwareBenchController
{
    private readonly ReadOnlyUdsTransmitBoundary _physicalUdsBoundary = new();
    private readonly SemaphoreSlim _physicalUdsWrite = new(1, 1);
    private CanTransmitGate? _physicalUdsLeases;

    /// <summary>Existing ITransmitLeaseService contract. Leases are NOT permission to send.</summary>
    public ITransmitLeaseService PhysicalDutCanLeases =>
        _physicalUdsLeases ?? throw new InvalidOperationException("Connect the main controller first.");

    public static string PhysicalReadOnlyUdsConfirmation(Guid requestId,
        ReadOnlyUdsPhysicalEndpoint endpoint, byte sid)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.Validate();
        if (requestId == Guid.Empty || sid is not (0x19 or 0x22))
            throw new ArgumentException("Approved ReadOnly RequestId and SID required.");
        return $"AUTHORIZE READONLY UDS {requestId:N} SID={sid:X2} CAN1 TX={endpoint.RequestCanId:X} RX={endpoint.ResponseCanId:X} REV={endpoint.ProfileRevision}";
    }

    /// <summary>INTEGRATION passes one validated, saved ECU/Profile endpoint, UDS PDU,
    /// and lease from PhysicalDutCanLeases. The returned scope MUST be disposed at
    /// request end/cancellation. This does not acquire a second USB connection or Hub.</summary>
    public IDisposable AuthorizePhysicalReadOnlyUds(
        ReadOnlyUdsPhysicalEndpoint endpoint, ReadOnlyMemory<byte> exactReadOnlyPdu,
        LeaseToken lease, Guid requestId, TimeSpan lifetime, int maxFrames,
        string typedConfirmation)
    {
        ReadOnlyUdsTransmitBoundary.ValidateReadOnlyRequest(exactReadOnlyPdu.Span);
        string expected = PhysicalReadOnlyUdsConfirmation(requestId, endpoint, exactReadOnlyPdu.Span[0]);
        if (!string.Equals(typedConfirmation, expected, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Explicit exact read-only diagnostic transaction consent required.");
        var session = _session;
        if (session is null || session.State != UsbSessionState.Connected || _link is null)
            throw new IOException("No current physical main-controller session.");
        if (!_versionVerified)
            throw new InvalidOperationException("Verify controller GET_VERSION in the live session.");
        if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
            throw new IOException("Physical trace is lost or unhealthy; deny CAN TX.");
        var leases = _physicalUdsLeases;
        if (leases is null || !leases.Validate(BusRole.DutCan, lease))
            throw new UnauthorizedAccessException("Active DUT CAN1 lease not owned in this USB session.");
        if (session.Generation != lease.SessionGeneration)
            throw new IOException("Lease belongs to a different USB generation.");
        var scope = _physicalUdsBoundary.Begin(endpoint, exactReadOnlyPdu.Span,
            lease, requestId, session.Generation, lifetime, maxFrames);
        RecordPhysicalUdsTx(requestId, Guid.Empty, session.Generation, "AUTHORIZATION",
            "ISSUED", "", null, $"Physical CAN1 SID={exactReadOnlyPdu.Span[0]:X2}; bounded permit, not CAN TX.");
        return scope;
    }

    private void ObservePhysicalReadOnlyUdsRx(CanObservation observation)
    {
        // The receive bridge supplies the actual published observation BEFORE Hub fanout.
        _physicalUdsBoundary.ObserveIncoming(observation);
    }

    private void RecordPhysicalUdsTx(Guid requestId, Guid correlationId, long generation,
        string stage, string outcome, string rawCan, ushort? ackSequence, string detail)
    {
        _log?.Record(new PhysicalReadOnlyUdsTxFact(
            DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), generation,
            requestId, correlationId, stage, outcome,
            "CAN1_DUT", rawCan, ackSequence, "UNKNOWN", detail));
    }

    private async ValueTask<OperationResult<CanTxReceipt>> TransmitPhysicalReadOnlyUdsAsync(
        CanTxRequest tx, LeaseToken lease, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX", "No physical write attempted.");
        try { await _physicalUdsWrite.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX", "No physical write attempted."); }
        try
        {
            var session = _session;
            var link = _link;
            var leases = _physicalUdsLeases;
            if (session is null || link is null || session.State != UsbSessionState.Connected ||
                leases is null || !_versionVerified)
                return OperationResult<CanTxReceipt>.Rejected("USB_NOT_READY", "No verified connected main controller.");
            if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0 ||
                (_canBridge?.DroppedBeforeDecode ?? 0) != 0 ||
                (_canBridge?.InvalidFrames ?? 0) != 0 || (_hub?.TotalDropped ?? 0) != 0)
            {
                _physicalUdsBoundary.Revoke();
                return OperationResult<CanTxReceipt>.Rejected("TRACE_OR_RX_LOSS", "USB/CAN evidence is unhealthy.");
            }
            long generation = session.Generation;
            if (!leases.Validate(BusRole.DutCan, lease) || lease.SessionGeneration != generation)
                return OperationResult<CanTxReceipt>.Rejected("STALE_DUT_CAN_LEASE", "Lease invalid or not from this session.");
            if (!_physicalUdsBoundary.TryConsumeOutgoing(tx.Frame, lease, generation,
                    out Guid requestId, out string reason))
            {
                RecordPhysicalUdsTx(requestId, tx.CorrelationId, generation,
                    "DENIED", reason, "", null, "No physical CAN write.");
                return OperationResult<CanTxReceipt>.Rejected(reason, "Physical read-only diagnostic transaction gate denied frame.");
            }

            byte[] rawCan;
            try { rawCan = CanFrameCodec.ToUsbPayload(tx.Frame, new BusRoleMap(default, DutPort: 1, KeypadPort: 0)); }
            catch (Exception ex)
            {
                _physicalUdsBoundary.Revoke();
                return OperationResult<CanTxReceipt>.Rejected("INVALID_CAN_FRAME", ex.Message);
            }
            string rawHex = Convert.ToHexString(rawCan);
            RecordPhysicalUdsTx(requestId, tx.CorrelationId, generation,
                "REQUEST_ISSUED", "REQUESTED", rawHex, null,
                "Exact authorized CAN1 USB 0x40 before WinUSB write; no ECU response.");
            if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
            {
                _physicalUdsBoundary.Revoke();
                return OperationResult<CanTxReceipt>.Rejected("TRACE_OR_RX_LOSS",
                    "Cannot safely issue physical CAN: recording of request intent failed.");
            }
            try
            {
                link.AuthorizeOneCanTransmission(rawCan, "AUTHORIZE CAN TX " + rawHex);
                var ack = await session.RequestAsync(
                    MainControllerCodec.CanSend, rawCan, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || session.State != UsbSessionState.Connected ||
                    !ReferenceEquals(session, _session) || session.Generation != generation ||
                    ack.Command != MainControllerCodec.CanSend || ack.Payload.Length != 0)
                    throw new IOException("Late, invalid or cancelled USB ACK; CAN physical outcome UNKNOWN.");
                if (!_physicalUdsBoundary.ConfirmControllerAck(requestId, generation))
                    throw new IOException("Diagnostic permit was revoked before main controller ACK.");
                RecordPhysicalUdsTx(requestId, tx.CorrelationId, generation,
                    "MAIN_CONTROLLER_ACK", "TRANSPORT_ACCEPTED", rawHex, ack.Sequence,
                    "Controller USB ACK for CAN_SEND only; not ECU UDS response.");
                if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
                    throw new IOException("Trace dropped or failed around physical USB ACK; outcome UNKNOWN.");
                var evidence = EvidenceRef.New();
                var receipt = new CanTxReceipt(tx.CorrelationId, AckKind.DeviceAcknowledged,
                    TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()), evidence);
                return OperationResult<CanTxReceipt>.Accepted(receipt, tx.CorrelationId);
            }
            catch (Exception ex)
            {
                _physicalUdsBoundary.Revoke();
                RecordPhysicalUdsTx(requestId, tx.CorrelationId, generation,
                    "WRITE_OR_ACK_FAILED", "UNCERTAIN", rawHex, null, ex.ToString());
                return OperationResult<CanTxReceipt>.Uncertain("CAN_TX_UNCERTAIN",
                    "USB write/ACK/cancellation error; transmission and ECU state UNKNOWN.",
                    tx.CorrelationId);
            }
            finally
            {
                // A timed-out or cancelled pending grant must never survive to the next frame.
                link.RevokeOneCanTransmission();
            }
        }
        finally { _physicalUdsWrite.Release(); }
    }
}

public sealed record PhysicalReadOnlyUdsTxFact(
    DateTimeOffset Utc, long HostMonotonicTicks, long SessionGeneration,
    Guid RequestId, Guid CorrelationId, string Stage, string Outcome,
    string Bus, string RawCanPayloadHex, ushort? MainControllerAckSequence,
    string EcuResponseStatus, string Detail)
{
    public string Kind => "DEVICE_READONLY_UDS_CAN1_TX";
}
