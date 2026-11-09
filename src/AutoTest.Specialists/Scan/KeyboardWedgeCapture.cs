using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

// Receives key events ONLY from the dedicated scanner field/verified capture context.
// It never installs a system-wide keyboard hook or consumes keys in property editors.
public sealed class KeyboardWedgeCapture
{
    private readonly BarcodeInputRouter _router;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _maxGap;
    private readonly int _maxLength;
    private readonly System.Text.StringBuilder _buffer = new();
    private DateTimeOffset? _previousKey;
    private bool _discardUntilTerminator;
    private bool _ignoreNextLf;
    public KeyboardWedgeCapture(BarcodeInputRouter router, TimeProvider? clock = null,
        int maxLength = 512, TimeSpan? maxInterKeyGap = null)
    {
        _router = router; _clock = clock ?? TimeProvider.System;
        _maxLength = maxLength > 0 ? maxLength : throw new ArgumentOutOfRangeException(nameof(maxLength));
        _maxGap = maxInterKeyGap ?? TimeSpan.FromSeconds(2);
    }

    public void Clear() { _buffer.Clear(); _previousKey = null; _discardUntilTerminator = false;
        _ignoreNextLf = false; }

    public ScanIngressResult? OnKeyInput(char character, bool scannerFieldHasFocus)
    {
        if (!scannerFieldHasFocus) { Clear(); return ScanIngressResult.Reject("SCAN_FOCUS_DENIED"); }
        var now = _clock.GetUtcNow();
        if (_previousKey.HasValue && now - _previousKey > _maxGap) Clear();
        _previousKey = now;
        if (character == '\n' && _ignoreNextLf) { _ignoreNextLf = false; return null; }
        _ignoreNextLf = false;
        if (character == '\r')
        {
            var result = CompleteScan(); _ignoreNextLf = true;
            return result;
        }
        if (character == '\n') return CompleteScan();
        if (_discardUntilTerminator) return null;
        if (char.IsControl(character)) { Clear(); return ScanIngressResult.Reject("SCAN_CONTROL_CHAR"); }
        if (_buffer.Length >= _maxLength)
        {
            _buffer.Clear(); _discardUntilTerminator = true;
            return ScanIngressResult.Reject("SCAN_LENGTH");
        }
        _buffer.Append(character);
        return null;
    }

    public ScanIngressResult CompleteScan()
    {
        if (_discardUntilTerminator)
        {
            Clear(); return ScanIngressResult.Reject("SCAN_LENGTH");
        }
        var text = _buffer.ToString();
        Clear();
        var scan = new RawScan(text, "keyboard",
            TimePoint.FromHostArrival(_clock.GetUtcNow(), Stopwatch.GetTimestamp()), true);
        return _router.PublishRaw(scan, ScannerCaptureMode.KeyboardWedge);
    }
}
