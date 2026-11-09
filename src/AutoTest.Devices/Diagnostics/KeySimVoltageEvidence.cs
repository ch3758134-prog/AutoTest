namespace AutoTest.Devices.Diagnostics;

public sealed record KeySimChannelManualReading(int Channel, decimal? MeasuredVolts);
public sealed record KeySimManualVoltageInput(
    string EcuProfile, string ProfileRevision, string Instrument,
    string Wiring, string Supply, string Load, string? Operator,
    IReadOnlyList<KeySimChannelManualReading> Channels, string? Notes);
public sealed record KeySimManualVoltageFact(
    string Kind, Guid EvidenceId, DateTimeOffset RecordedUtc, string Source,
    string? MainControllerDeviceId, long SessionGeneration, uint? LastRequestedCompleteMask,
    ushort? MainControllerUsbCanSendSequence, string TransportEvidence,
    string KeyBoardAcknowledgement, string EcuProfile, string ProfileRevision,
    string Instrument, string Wiring, string Supply, string Load, string? Operator,
    IReadOnlyList<KeySimChannelManualReading> Channels, string? Notes,
    string PhysicalObserved, string EcuResponded, string BusinessVerified);

/// <summary>Five board voltage outputs are *human* meter observations, never simulated ADC
/// values. Full 20-bit mask commands do not compose by OR across business controls.</summary>
public sealed class KeySimVoltageEvidenceTracker
{
    private readonly object _sync = new();
    private uint? _mask;
    private ushort? _sequence;
    private long _generation;
    private string _transport = "UNKNOWN";
    public string Status { get { lock (_sync) return $"{_transport}; GEN={_generation}; MASK={(_mask is uint m ? $"0x{m:X5}" : "UNKNOWN")}; USB_SEQ={(_sequence?.ToString() ?? "UNKNOWN")}; BOARD_ACK=NOT_AVAILABLE"; } }
    public void Pending(uint completeMask, long generation)
    {
        if (completeMask > 0xFFFFF || generation <= 0) throw new ArgumentOutOfRangeException(nameof(completeMask));
        lock (_sync) { _mask = completeMask; _generation = generation; _sequence = null; _transport = "REQUESTED_UNCONFIRMED"; }
    }
    public void ControllerAccepted(uint completeMask, long generation, ushort usbSequence)
    {
        if (usbSequence == 0) throw new ArgumentOutOfRangeException(nameof(usbSequence));
        lock (_sync)
        {
            if (_generation != generation || _mask != completeMask || _transport != "REQUESTED_UNCONFIRMED")
                throw new InvalidOperationException("Stale or mismatched CAN-send response.");
            _sequence = usbSequence;
            _transport = "MAIN_CONTROLLER_CAN_SEND_ACCEPTED";
        }
    }
    public void Invalidate(string reason, long newGeneration)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason required", nameof(reason));
        lock (_sync)
        {
            _mask = null; _sequence = null; _generation = newGeneration;
            _transport = "UNKNOWN_" + reason.Trim().Replace(' ', '_').ToUpperInvariant();
        }
    }
    public KeySimManualVoltageFact RecordManual(string? deviceId, KeySimManualVoltageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        static string Need(string? s, string name) =>
            string.IsNullOrWhiteSpace(s) ? throw new ArgumentException($"Required: {name}", name) : s.Trim();
        var profile = Need(input.EcuProfile, nameof(input.EcuProfile));
        var revision = Need(input.ProfileRevision, nameof(input.ProfileRevision));
        var instrument = Need(input.Instrument, nameof(input.Instrument));
        var wiring = Need(input.Wiring, nameof(input.Wiring));
        var supply = Need(input.Supply, nameof(input.Supply));
        var load = Need(input.Load, nameof(input.Load));
        if (input.Channels is null || input.Channels.Count != 5 ||
            !input.Channels.Select(c => c.Channel).Order().SequenceEqual(new[] { 1, 2, 3, 4, 5 }))
            throw new ArgumentException("V1..V5 required; null means UNMEASURED.", nameof(input));
        if (!input.Channels.Any(c => c.MeasuredVolts.HasValue))
            throw new ArgumentException("At least one measured voltage required.", nameof(input));
        lock (_sync)
            return new KeySimManualVoltageFact(
                "KEYSIM_MANUAL_VOLTAGE_OBSERVATION", Guid.NewGuid(), DateTimeOffset.UtcNow,
                "OPERATOR_MANUAL_METER", deviceId, _generation, _mask, _sequence, _transport,
                "NOT_AVAILABLE", profile, revision, instrument, wiring, supply, load,
                input.Operator?.Trim(), input.Channels.OrderBy(c => c.Channel).ToArray(), input.Notes,
                "OPERATOR_RECORDED_PARTIAL_OR_FULL_VOLTAGES", "UNKNOWN", "NOT_EVALUATED");
    }
}
