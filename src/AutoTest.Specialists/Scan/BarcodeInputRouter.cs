using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

public enum ScannerCaptureMode { Disabled, KeyboardWedge, ManualSimulation, VerifiedSerial }

// Single-consumer scan queue is bounded. The UI must explicitly attach the scanner context.
public sealed class BarcodeInputRouter : IBarcodeInput
{
    private readonly Channel<BarcodeScanEvent> _events = Channel.CreateBounded<BarcodeScanEvent>(
        new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true, SingleWriter = false });
    private readonly object _sync = new();
    private InputPolicy _policy = new(true, 512, "keyboard");
    private ScannerCaptureMode _mode = ScannerCaptureMode.Disabled;
    private Func<bool>? _admissionGate;

    public ScannerCaptureMode Mode { get { lock (_sync) return _mode; } }

    public void AttachMode(ScannerCaptureMode mode, InputPolicy policy)
    {
        if (policy.MaxLength is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(policy));
        if (mode == ScannerCaptureMode.VerifiedSerial)
            throw new InvalidOperationException("SERIAL_MODE_REQUIRES_VERIFIED_DEVICE_ADAPTER");
        if (mode == ScannerCaptureMode.KeyboardWedge && !policy.RequireFocus)
            throw new InvalidOperationException("KEYBOARD_CAPTURE_REQUIRES_FOCUS");
        lock (_sync) { _mode = mode; _policy = policy; }
    }

    public void AttachVerifiedSerial(InputPolicy policy, bool verifiedByOperator)
    {
        if (!verifiedByOperator) throw new InvalidOperationException("SERIAL_DEVICE_NOT_VERIFIED");
        if (policy.MaxLength is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(policy));
        lock (_sync) { _mode = ScannerCaptureMode.VerifiedSerial; _policy = policy; }
    }

    // Production must bind this to its live station-admission policy before enabling scan.
    // A busy run then rejects at capture, rather than leaving stale queued scans to run later.
    public void SetAdmissionGate(Func<bool> allowInput)
    {
        lock (_sync) _admissionGate = allowInput ?? throw new ArgumentNullException(nameof(allowInput));
    }

    public ScanIngressResult PublishRaw(RawScan input, ScannerCaptureMode from)
    {
        InputPolicy policy;
        Func<bool>? admission;
        lock (_sync)
        {
            if (_mode == ScannerCaptureMode.Disabled || _mode != from)
                return ScanIngressResult.Reject("SCAN_MODE_DENIED");
            policy = _policy;
            admission = _admissionGate;
        }
        if (admission is not null && !admission())
            return ScanIngressResult.Reject("SCAN_STATION_BUSY");
        var validation = ScanInputValidation.ValidateSource(input, policy);
        if (validation is not null) return ScanIngressResult.Reject(validation);
        var accepted = new BarcodeScanEvent(input, EvidenceRef.New());
        if (!_events.Writer.TryWrite(accepted)) return ScanIngressResult.Reject("SCAN_QUEUE_FULL");
        return ScanIngressResult.Accept(accepted);
    }

    public async IAsyncEnumerable<BarcodeScanEvent> ObserveAsync(InputPolicy policy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Observation cannot weaken ingestion policy or reinterpret the stored raw event.
        await foreach (var item in _events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            if (ScanInputValidation.ValidateSource(item.Scan, policy) is null) yield return item;
    }
}
