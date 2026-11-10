using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// One genuinely physical KEY_SIM 0x730 CAN0 write on the existing USB session.
/// USB CAN_SEND ACK is the sole guaranteed acknowledgement; no KEY_SIM or
/// DUT voltage/effect feedback is invented or required by this command.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly SemaphoreSlim _physicalKeySimWrite = new(1, 1);

    public static string PhysicalKeySimConfirmation(uint mask, long generation)
    {
        if (mask > KeySimCanCodec.MaxMask || generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(mask));
        return $"AUTHORIZE KEYSIM CAN0 MASK={mask:X5} GEN={generation}";
    }

    public async Task<OperationResult<CanTxReceipt>> SetPhysicalKeySimMaskAsync(
        uint mask, string exactConfirmation, CancellationToken ct = default)
    {
        if (mask > KeySimCanCodec.MaxMask)
            return OperationResult<CanTxReceipt>.Rejected(
                "KEYSIM_MASK_INVALID", "Only twenty resistor-switch bits are supported.");
        if (ct.IsCancellationRequested)
            return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX",
                "No physical request attempted.");
        try { await _physicalKeySimWrite.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX",
                "No physical request attempted.");
        }
        try
        {
            var session = _session;
            var link = _link;
            var audit = _log;
            if (session is null || link is null ||
                session.State != UsbSessionState.Connected || !_versionVerified ||
                audit is null || audit.FailureMessage is not null ||
                audit.Dropped != 0)
                return OperationResult<CanTxReceipt>.Rejected(
                    "KEYSIM_SESSION_NOT_READY",
                    "A verified physical controller session and healthy trace are required.");
            long generation = session.Generation;
            if (!string.Equals(exactConfirmation, PhysicalKeySimConfirmation(mask, generation),
                StringComparison.Ordinal))
                return OperationResult<CanTxReceipt>.Rejected(
                    "KEYSIM_OPERATOR_NOT_AUTHORIZED",
                    "Current session and exact resistor-switch mask need explicit consent.");
            // The active board receives 0x730 via physical CAN0. No second
            // connection, no guessed DAC voltages, no alternative 0x100 path.
            var frame = KeySimCanCodec.SetMask(mask);
            byte[] payload = CanFrameCodec.ToUsbPayload(frame,
                new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
            string hex = Convert.ToHexString(payload);
            var operationId = Guid.NewGuid();
            void Record(string stage, string outcome, string detail, ushort? seq = null) =>
                audit.Record(new PhysicalKeySimTxFact(DateTimeOffset.UtcNow,
                    Stopwatch.GetTimestamp(), generation, operationId, mask,
                    stage, outcome, hex, seq, detail));
            Record("REQUEST", "INTENT_ONLY",
                "Real CAN0 KEY_SIM command is about to be written. No board feedback asserted.");
            if (audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<CanTxReceipt>.Rejected(
                    "KEYSIM_AUDIT_NOT_DURABLE", "Cannot record actual physical CAN TX intent.");
            try
            {
                link.AuthorizeOneCanTransmission(payload, "AUTHORIZE CAN TX " + hex);
                var ack = await session.RequestAsync(MainControllerCodec.CanSend, payload,
                    TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested ||
                    !ReferenceEquals(_session, session) ||
                    session.State != UsbSessionState.Connected ||
                    session.Generation != generation ||
                    ack.Command != MainControllerCodec.CanSend ||
                    ack.Payload.Length != 0)
                    throw new IOException("KEY_SIM CAN0 USB ACK or session is invalid; outcome unknown.");
                Record("MAIN_CONTROLLER_ACK", "TRANSPORT_ACCEPTED",
                    "Controller accepted CAN_SEND, not evidence KEY_SIM switched or DUT recognized input.",
                    ack.Sequence);
                if (audit.FailureMessage is not null || audit.Dropped != 0)
                    throw new IOException("KeySim output trace lost after USB response.");
                var evidence = EvidenceRef.New();
                return OperationResult<CanTxReceipt>.Accepted(new CanTxReceipt(operationId,
                    AckKind.DeviceAcknowledged,
                    TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()),
                    evidence), operationId);
            }
            catch (Exception ex)
            {
                Record("WRITE_OR_ACK_FAILED", "UNCERTAIN", ex.Message);
                return OperationResult<CanTxReceipt>.Uncertain(
                    "KEYSIM_CAN0_UNCERTAIN",
                    "USB transmission or ACK could not be established; actual switch state UNKNOWN.",
                    operationId);
            }
            finally { link.RevokeOneCanTransmission(); }
        }
        finally { _physicalKeySimWrite.Release(); }
    }
}

public sealed record PhysicalKeySimTxFact(
    DateTimeOffset Utc, long HostMonotonicTicks, long SessionGeneration,
    Guid OperationId, uint Requested20BitMask,
    string Stage, string Outcome, string RawCanUsbPayloadHex,
    ushort? MainControllerAckSequence, string Detail)
{
    public string Kind => "DEVICE_PHYSICAL_KEYSIM_CAN0_TX";
}
