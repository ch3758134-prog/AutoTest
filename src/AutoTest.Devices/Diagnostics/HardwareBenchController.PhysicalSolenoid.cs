using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Actual firmware SET_SOL command (0x16: 0=raise, 1=lower).
/// Do not infer physical clamp contact, cylinder travel or air pressure from
/// USB ACK or the GET_SOL GPIO state response.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly SemaphoreSlim _physicalSolenoidWrite = new(1, 1);

    public static string PhysicalSolenoidConfirmation(byte action, long sessionGeneration)
    {
        if (action > 1) throw new ArgumentOutOfRangeException(nameof(action));
        if (sessionGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(sessionGeneration));
        return $"AUTHORIZE SOL {action} GEN {sessionGeneration}";
    }

    public async Task<OperationResult<PhysicalSolenoidReceipt>> SetPhysicalSolenoidAsync(
        byte action, string exactConfirmation, CancellationToken ct = default)
    {
        if (action > 1)
            return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                "SOL_ACTION_INVALID", "Only firmware action 0=raise or 1=lower is supported.");
        if (ct.IsCancellationRequested)
            return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                "CANCELLED_BEFORE_TX", "No fixture request attempted.");
        try { await _physicalSolenoidWrite.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                "CANCELLED_BEFORE_TX", "No fixture request attempted.");
        }

        try
        {
            var session = _session;
            var link = _link;
            var audit = _log;
            if (session is null || link is null ||
                session.State != UsbSessionState.Connected || !_versionVerified ||
                audit is null || audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                    "SOL_SESSION_NOT_READY",
                    "SET_SOL requires live verified firmware and a healthy operation trace.");
            if (PhysicalCanToolFlashActive)
                return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                    "SOL_FLASH_ACTIVE", "Physical fixture operations are blocked during ECU programming.");
            long gen = session.Generation;
            if (!string.Equals(exactConfirmation,
                PhysicalSolenoidConfirmation(action, gen), StringComparison.Ordinal))
                return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                    "SOL_OPERATOR_NOT_AUTHORIZED", "Specific solenoid action needs explicit operator consent.");

            var op = Guid.NewGuid();
            void Record(string stage, string outcome, string detail, ushort? seq = null)
                => audit.Record(new PhysicalSolenoidTxFact(DateTimeOffset.UtcNow,
                    Stopwatch.GetTimestamp(), gen, op, action, stage, outcome, seq, detail));
            Record("REQUEST", "INTENT_ONLY",
                $"SET_SOL 0x16 action={action}. Mechanical fixture state not observed.");
            if (audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<PhysicalSolenoidReceipt>.Rejected(
                    "SOL_AUDIT_UNHEALTHY", "Cannot preserve fixture command intent.");

            try
            {
                link.AuthorizeOneSolenoidTransmission(action,
                    PhysicalSolenoidConfirmation(action, link.ConnectionGeneration));
                var response = await session.RequestAsync(
                    FirmwareReadOnlyProtocol.SetSolenoid,
                    new byte[]{action}, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                    session.State != UsbSessionState.Connected ||
                    session.Generation != gen ||
                    response.Command != FirmwareReadOnlyProtocol.SetSolenoid ||
                    response.Payload.Length != 0)
                    throw new IOException("SET_SOL ACK/session invalid; fixture state UNKNOWN.");
                Record("MAIN_CONTROLLER_ACK", "TRANSPORT_ACCEPTED",
                    "Firmware accepted SET_SOL; no evidence the cylinder moved to position.",
                    response.Sequence);
                if (audit.FailureMessage is not null || audit.Dropped != 0)
                    throw new IOException("Solenoid audit became unhealthy after USB response.");
                return OperationResult<PhysicalSolenoidReceipt>.Accepted(
                    new PhysicalSolenoidReceipt(op, AckKind.DeviceAcknowledged,
                        EvidenceRef.New(), action), op);
            }
            catch (Exception ex)
            {
                Record("WRITE_OR_ACK_FAILED", "UNCERTAIN", ex.Message);
                return OperationResult<PhysicalSolenoidReceipt>.Uncertain(
                    "SOL_POSITION_UNCERTAIN",
                    "SET_SOL request or ACK uncertain; check actual fixture position before any other actuation.",
                    op);
            }
            finally { link.RevokeOneSolenoidTransmission(); }
        }
        finally { _physicalSolenoidWrite.Release(); }
    }
}
public sealed record PhysicalSolenoidReceipt(
    Guid OperationId, AckKind Ack, EvidenceRef Evidence, byte CommandedAction);

public sealed record PhysicalSolenoidTxFact(
    DateTimeOffset Utc, long HostMonotonicTicks, long SessionGeneration,
    Guid OperationId, byte CommandedAction, string Stage, string Outcome,
    ushort? MainControllerAckSequence, string Detail)
{
    public string Kind => "DEVICE_PHYSICAL_SET_SOL_0x16";
}
