using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

// Deliberately no SerialPort opening here: model, COM interface and protocol are unverified.
// Only a caller-provided, verified byte source may be used; mocks work without hardware.
public interface IVerifiedBarcodeLineSource
{
    ValueTask<string?> ReadLineAsync(CancellationToken ct);
}

public sealed class SerialBarcodeCapture
{
    private readonly BarcodeInputRouter _router;
    private readonly TimeProvider _clock;
    private IVerifiedBarcodeLineSource? _source;
    public SerialBarcodeCapture(BarcodeInputRouter router, TimeProvider? clock = null)
    { _router = router; _clock = clock ?? TimeProvider.System; }

    public ValueTask<bool> OpenAsync(IVerifiedBarcodeLineSource source,
        string verifiedDeviceIdentity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(verifiedDeviceIdentity)) return ValueTask.FromResult(false);
        _router.AttachVerifiedSerial(new InputPolicy(false, 512, "serial"), true);
        _source = source ?? throw new ArgumentNullException(nameof(source));
        return ValueTask.FromResult(true);
    }

    public async ValueTask<ScanIngressResult> ReadAsync(CancellationToken ct)
    {
        if (_source is null) return ScanIngressResult.Reject("SERIAL_NOT_VERIFIED");
        var line = await _source.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) return ScanIngressResult.Reject("SERIAL_END_OF_INPUT");
        var scan = new RawScan(line.TrimEnd('\r', '\n'), "serial",
            TimePoint.FromHostArrival(_clock.GetUtcNow(), Stopwatch.GetTimestamp()), true);
        return _router.PublishRaw(scan, ScannerCaptureMode.VerifiedSerial);
    }
}
