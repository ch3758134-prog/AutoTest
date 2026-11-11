using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Physically transmits ONE configured classic-CAN frame to the DUT (CAN1).
/// This is intentionally distinct from the internal KEY_SIM CAN0 transport.
/// The current firmware adapter exposes CAN_SEND 0x40; it does not expose
/// a verified board-side periodic-table API here. Periodic orchestration is
/// therefore performed by the bench caller and is not claimed to be hard real-time.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly SemaphoreSlim _physicalManualCanWrite = new(1, 1);

    public static string PhysicalManualCanConfirmation(CanFrame frame, long generation)
    {
        if (frame.Role != BusRole.DutCan || generation <= 0) throw new ArgumentException("Invalid DUT CAN authorization");
        var payload = CanFrameCodec.ToUsbPayload(frame,
            new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
        return $"AUTHORIZE MANUAL CAN1 GEN={generation} PAYLOAD={Convert.ToHexString(payload)}";
    }

    public async Task<OperationResult<CanTxReceipt>> SendPhysicalManualCan1Async(
        CanFrame frame, string exactConfirmation, CancellationToken ct = default)
    {
        if (frame.Role != BusRole.DutCan)
            return OperationResult<CanTxReceipt>.Rejected("MANUAL_CAN_ROLE",
                "Manual DUT CAN transmissions must use CAN1, never KEY_SIM CAN0.");
        if (ct.IsCancellationRequested)
            return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX", "No frame sent.");
        try { await _physicalManualCanWrite.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return OperationResult<CanTxReceipt>.Rejected("CANCELLED_BEFORE_TX", "No frame sent.");
        }
        try
        {
            var session = _session;
            var link = _link;
            var audit = _log;
            if (session is null || link is null || audit is null ||
                session.State != UsbSessionState.Connected || !_versionVerified ||
                audit.FailureMessage is not null || audit.Dropped != 0 ||
                PhysicalCanToolFlashActive)
                return OperationResult<CanTxReceipt>.Rejected("MANUAL_CAN_DEVICE_BUSY",
                    "Actual controller session unverified, trace unhealthy, or ECU programming active.");
            var generation = session.Generation;
            byte[] payload;
            try
            {
                payload = CanFrameCodec.ToUsbPayload(frame,
                    new BusRoleMap(default, DutPort: 1, KeypadPort: 0));
                if (exactConfirmation != PhysicalManualCanConfirmation(frame, generation))
                    return OperationResult<CanTxReceipt>.Rejected("MANUAL_CAN_UNAUTHORIZED",
                        "Exact physical CAN1 frame and session approval missing.");
            }
            catch (Exception e)
            {
                return OperationResult<CanTxReceipt>.Rejected("MANUAL_CAN_INVALID", e.Message);
            }
            var id = Guid.NewGuid();
            var hex = Convert.ToHexString(payload);
            audit.RecordMessage("MANUAL_CAN1_TX_INTENT",
                $"UTC={DateTimeOffset.UtcNow:O} OP={id:D} GEN={generation} HEX={hex} " +
                "Transport ACK is not ECU recognition.");
            if (audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<CanTxReceipt>.Rejected("MANUAL_CAN_AUDIT_FAILED",
                    "Cannot maintain a reliable physical CAN evidence log.");
            try
            {
                if (PhysicalCanToolFlashActive)
                    return OperationResult<CanTxReceipt>.Rejected("FLASH_ACTIVE",
                        "Flash became active before CAN transmission.");
                link.AuthorizeOneCanTransmission(payload, "AUTHORIZE CAN TX " + hex);
                var ack = await session.RequestAsync(MainControllerCodec.CanSend,
                    payload, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                    session.State != UsbSessionState.Connected || session.Generation != generation ||
                    PhysicalCanToolFlashActive ||
                    ack.Command != MainControllerCodec.CanSend || ack.Payload.Length != 0)
                    throw new IOException("CAN1 TX response or session changed; actual transmission uncertain.");
                audit.RecordMessage("MANUAL_CAN1_MAIN_ACK",
                    $"OP={id:D} GEN={generation} CAN1 ID=0x{frame.Id:X} SEQ={ack.Sequence}");
                if (audit.FailureMessage is not null || audit.Dropped != 0)
                    throw new IOException("Physical CAN1 ACK trace lost.");
                return OperationResult<CanTxReceipt>.Accepted(
                    new CanTxReceipt(id, AckKind.DeviceAcknowledged,
                        TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()),
                        EvidenceRef.New()), id);
            }
            catch (Exception e)
            {
                audit.RecordMessage("MANUAL_CAN1_TX_UNCERTAIN",
                    $"OP={id:D} GEN={generation} HEX={hex} ERROR={e.GetType().Name}: {e.Message}");
                return OperationResult<CanTxReceipt>.Uncertain("MANUAL_CAN_TX_UNCERTAIN",
                    "CAN1 transmission or controller ACK uncertain; inspect the ECU before resending.", id);
            }
            finally { link.RevokeOneCanTransmission(); }
        }
        finally { _physicalManualCanWrite.Release(); }
    }
}
