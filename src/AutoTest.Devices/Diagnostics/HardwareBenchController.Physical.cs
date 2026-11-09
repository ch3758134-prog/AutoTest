using AutoTest.Devices.Station;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Physical control is a separate, opt-in extension of the EXISTING WinUSB session.
/// It never creates another device connection, never auto-energizes an output and never
/// reports external electrical state from controller USB ACK or GPIO readback.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly SemaphoreSlim _physicalGate = new(1, 1);
    private readonly KeySimVoltageEvidenceTracker _keyEvidence = new();
    public string KeySimEvidenceStatus => _keyEvidence.Status;
    public event Action<HardwareControlTrace>? ControlTrace;

    private static void CheckPhysicalTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(200) || timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    private void RecordPhysical(long generation, string operation, string stage, byte command,
        byte? requestedValue, string outcome, string detail, ushort? sequence = null,
        byte? status = null, byte? readback = null, string? rawCan = null)
    {
        var fact = HardwareControlTrace.Create(generation, operation, stage, command,
            requestedValue, outcome, detail, sequence, status, readback, rawCan);
        _log?.Record(fact);
        foreach (Action<HardwareControlTrace> callback in ControlTrace?.GetInvocationList()
                     .Cast<Action<HardwareControlTrace>>() ?? Enumerable.Empty<Action<HardwareControlTrace>>())
        {
            try { callback(fact); }
            catch { /* a UI observer cannot interfere with the physical transport */ }
        }
    }

    /// <summary>Four firmware-verified SET commands. Caller must provide the exact fresh
    /// operation phrase. A successful result proves USB ACK and MCU GPIO readback only.</summary>
    public async Task<StationOutputReceipt> SetStationOutputAsync(byte command, byte value,
        string typedConfirmation, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!PhysicalCommandFence.IsStationCommand(command) || value > 1 ||
            !string.Equals(typedConfirmation, PhysicalCommandFence.StationConfirmation(command, value),
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Unsupported or unconfirmed physical Station command.");
        CheckPhysicalTimeout(timeout);
        await _physicalGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = _session;
            var link = _link;
            if (session is null || link is null || session.State != UsbSessionState.Connected)
                throw new IOException("Main controller is not connected.");
            if (!_versionVerified)
                throw new InvalidOperationException("Verify GET_VERSION in this session before control writes.");
            if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
                throw new IOException("Trace storage unavailable/lossy; physical output is blocked.");
            long generation = session.Generation;
            RecordPhysical(generation, "STATION", "REQUEST_ISSUED", command, value,
                "REQUESTED", "Before USB OUT; no ACK and no physical observation.");
            try
            {
                link.AuthorizeOneStationCommand(command, value, typedConfirmation);
                var ack = await session.RequestAsync(command, new byte[] { value }, timeout, ct)
                    .ConfigureAwait(false);
                if (session.State != UsbSessionState.Connected || session.Generation != generation ||
                    ack.Command != command || ack.Payload.Length != 0)
                    throw new IOException("Stale/malformed control ACK; output state UNKNOWN.");
                RecordPhysical(generation, "STATION", "USB_ACK", command, value,
                    "TRANSPORT_ACCEPTED", "Controller accepted SET; external voltage/fixture position UNKNOWN.",
                    ack.Sequence, ack.Status);

                byte getCommand = (byte)(command + 1);
                var readback = await session.RequestAsync(getCommand, ReadOnlyMemory<byte>.Empty, timeout, ct)
                    .ConfigureAwait(false);
                if (session.State != UsbSessionState.Connected || session.Generation != generation ||
                    readback.Command != getCommand ||
                    !FirmwareReadOnlyProtocol.ValidReadOnlyPayload(getCommand, readback.Payload))
                    throw new IOException("Readback invalid/stale; output state UNKNOWN.");
                bool matches = readback.Payload[0] == value;
                RecordPhysical(generation, "STATION", "FIRMWARE_READBACK", command, value,
                    matches ? "GPIO_MATCH" : "GPIO_MISMATCH",
                    "MCU GPIO readback only; measured voltage/position UNKNOWN.",
                    readback.Sequence, readback.Status, readback.Payload[0]);
                return new StationOutputReceipt(DateTimeOffset.UtcNow, generation, command, value,
                    ack.Sequence, ack.Status, readback.Sequence, readback.Payload[0], matches);
            }
            catch (Exception ex)
            {
                RecordPhysical(generation, "STATION", "OPERATION_ENDED", command, value,
                    "UNKNOWN", ex.ToString());
                throw;
            }
            finally { link.RevokeStationCommand(); }
        }
        finally { _physicalGate.Release(); }
    }

    /// <summary>Apply one COMPLETE 20-bit KeySim resistor-network state on CAN0,
    /// or send explicit 0x80 release when mask is zero. Not a DUT-facing real-button API.
    /// The MCU 0x40 ACK is not KEY_SIM actuation confirmation or a five-channel voltage sample.</summary>
    public async Task<KeypadCommandReceipt> SendKeyMaskAsync(uint completeMask,
        string typedConfirmation, TimeSpan timeout, CancellationToken ct = default)
    {
        if (completeMask > PhysicalKeypadProtocol.AllKeysMask ||
            !string.Equals(typedConfirmation, PhysicalKeypadProtocol.Confirmation(completeMask),
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact full KeyMask/release authorization required.");
        CheckPhysicalTimeout(timeout);
        await _physicalGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = _session;
            var link = _link;
            if (session is null || link is null || session.State != UsbSessionState.Connected)
                throw new IOException("Main controller is not connected.");
            if (!_versionVerified)
                throw new InvalidOperationException("Verify GET_VERSION before KEY_SIM writes.");
            if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
                throw new IOException("Trace storage unavailable/lossy; key transmission blocked.");
            long generation = session.Generation;
            var payload = PhysicalKeypadProtocol.EncodeUsbCanPayload(completeMask);
            string raw = Convert.ToHexString(payload);
            _keyEvidence.Pending(completeMask, generation);
            RecordPhysical(generation, "KEYSIM", "REQUEST_ISSUED",
                FirmwareReadOnlyProtocol.CanTransmit, null, "REQUESTED",
                $"CAN0 ID 0x730 complete Mask 0x{completeMask:X5}; not yet transmitted.", rawCan: raw);
            try
            {
                link.AuthorizeOneKeyFrame(payload, typedConfirmation);
                var ack = await session.RequestAsync(FirmwareReadOnlyProtocol.CanTransmit,
                    payload, timeout, ct).ConfigureAwait(false);
                if (session.State != UsbSessionState.Connected || session.Generation != generation ||
                    ack.Command != FirmwareReadOnlyProtocol.CanTransmit || ack.Payload.Length != 0)
                    throw new IOException("Stale/malformed CAN-send ACK; KeySim state UNKNOWN.");
                _keyEvidence.ControllerAccepted(completeMask, generation, ack.Sequence);
                RecordPhysical(generation, "KEYSIM", "USB_CAN_SEND_ACK",
                    FirmwareReadOnlyProtocol.CanTransmit, null, "TRANSPORT_ACCEPTED",
                    $"MCU accepted CAN send, Mask 0x{completeMask:X5}; KEY_SIM has no ACK.",
                    ack.Sequence, ack.Status, rawCan: raw);
                return new KeypadCommandReceipt(DateTimeOffset.UtcNow, generation,
                    completeMask, ack.Sequence, ack.Status, raw);
            }
            catch (Exception ex)
            {
                _keyEvidence.Invalidate("FAILED_OR_CANCELLED", generation);
                RecordPhysical(generation, "KEYSIM", "OPERATION_ENDED",
                    FirmwareReadOnlyProtocol.CanTransmit, null, "UNKNOWN", ex.ToString(),
                    rawCan: raw);
                throw;
            }
            finally { link.RevokeKeyFrame(); }
        }
        finally { _physicalGate.Release(); }
    }

    /// <summary>Stores human meter readings in the same raw trace, never fabricates ADC values.</summary>
    public KeySimManualVoltageFact RecordKeySimManualVoltage(KeySimManualVoltageInput input)
    {
        if (_session?.State != UsbSessionState.Connected || _log is null ||
            _log.FailureMessage is not null || _log.Dropped != 0)
            throw new IOException("A healthy connected trace session is required.");
        var evidence = _keyEvidence.RecordManual(_selectedInstanceId, input);
        _log.Record(evidence);
        return evidence;
    }
}
