using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Physical IGN output on the existing verified WinUSB controller.
/// SET_IGN=0x12, 1=ON / 0=OFF. The response acknowledges the command;
/// it is NOT an IGN voltage measurement or proof of ECU power state.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly SemaphoreSlim _physicalIgnWrite = new(1, 1);

    public static string PhysicalIgnConfirmation(bool on, long generation)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        return "AUTHORIZE IGN " + (on ? "ON" : "OFF") + " GEN " + generation;
    }

    public async Task<OperationResult<PhysicalIgnReceipt>> SetPhysicalIgnAsync(
        bool on, string exactConfirmation, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return OperationResult<PhysicalIgnReceipt>.Rejected(
                "CANCELLED_BEFORE_TX", "No IGN command attempted.");
        try { await _physicalIgnWrite.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return OperationResult<PhysicalIgnReceipt>.Rejected(
                "CANCELLED_BEFORE_TX", "No IGN command attempted.");
        }

        try
        {
            var session = _session;
            var link = _link;
            var audit = _log;
            if (session is null || link is null ||
                session.State != UsbSessionState.Connected || !_versionVerified ||
                audit is null || audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<PhysicalIgnReceipt>.Rejected(
                    "IGN_SESSION_NOT_READY",
                    "Physical IGN requires verified firmware, a live USB session and healthy audit.");
            if (PhysicalCanToolFlashActive)
                return OperationResult<PhysicalIgnReceipt>.Rejected(
                    "IGN_FLASH_ACTIVE", "Do not change IGN while ECU flashing is running.");
            long generation = session.Generation;
            if (!string.Equals(exactConfirmation,
                    PhysicalIgnConfirmation(on, generation), StringComparison.Ordinal))
                return OperationResult<PhysicalIgnReceipt>.Rejected(
                    "IGN_OPERATOR_NOT_AUTHORIZED", "Exact per-session IGN consent is required.");

            Guid operationId = Guid.NewGuid();
            var payload = new byte[] { on ? (byte)1 : (byte)0 };
            void Record(string stage, string outcome, string detail, ushort? seq = null)
                => audit.Record(new PhysicalIgnTxFact(DateTimeOffset.UtcNow,
                    Stopwatch.GetTimestamp(), generation, operationId, on,
                    stage, outcome, seq, detail));
            Record("REQUEST", "INTENT_ONLY",
                $"SET_IGN 0x12 payload={payload[0]:X2}; no physical output measurement.");
            if (audit.FailureMessage is not null || audit.Dropped != 0)
                return OperationResult<PhysicalIgnReceipt>.Rejected(
                    "IGN_AUDIT_UNHEALTHY", "Cannot preserve pre-write IGN intent.");

            try
            {
                // Link and session generations can differ after reconnect:
                // caller consent binds the session, link fence binds this
                // exact USB link epoch, both checked before sending.
                link.AuthorizeOneIgnTransmission(on,
                    "AUTHORIZE IGN " + (on ? "ON" : "OFF") +
                    " GEN " + link.ConnectionGeneration);

                var response = await session.RequestAsync(MainControllerCodec.SetIgn,
                    payload, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                    session.State != UsbSessionState.Connected ||
                    session.Generation != generation ||
                    response.Command != MainControllerCodec.SetIgn ||
                    response.Payload.Length != 0)
                    throw new IOException("IGN USB ACK or session mismatch; physical state UNKNOWN.");
                Record("MAIN_CONTROLLER_ACK", "TRANSPORT_ACCEPTED",
                    "SET_IGN returned valid USB ACK. Does not prove actual IGN line voltage.",
                    response.Sequence);
                if (audit.FailureMessage is not null || audit.Dropped != 0)
                    throw new IOException("IGN trace became unhealthy after device response.");
                return OperationResult<PhysicalIgnReceipt>.Accepted(
                    new PhysicalIgnReceipt(operationId, AckKind.DeviceAcknowledged,
                        EvidenceRef.New(), on), operationId);
            }
            catch (Exception ex)
            {
                Record("WRITE_OR_ACK_FAILED", "UNCERTAIN", ex.Message);
                return OperationResult<PhysicalIgnReceipt>.Uncertain(
                    "IGN_OUTPUT_UNCERTAIN",
                    "IGN request/ACK not confirmed; physical output level UNKNOWN.",
                    operationId);
            }
            finally { link.RevokeOneIgnTransmission(); }
        }
        finally { _physicalIgnWrite.Release(); }
    }
}

public sealed record PhysicalIgnReceipt(
    Guid OperationId, AckKind Ack, EvidenceRef Evidence, bool CommandedOn);

public sealed record PhysicalIgnTxFact(
    DateTimeOffset Utc, long HostMonotonicTicks, long SessionGeneration,
    Guid OperationId, bool CommandedOn,
    string Stage, string Outcome, ushort? MainControllerAckSequence,
    string Detail)
{
    public string Kind => "DEVICE_PHYSICAL_IGN_CMD_0x12";
}
