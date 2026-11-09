using System;

namespace AutoTest.Specialists.Scan.Lab;

// Evidence from a focused Windows TextBox. This does NOT identify the physical scanner:
// keyboard wedge, manual typing and paste are indistinguishable here.
public sealed record LabScannerCaptureObservation(
    Guid AttemptId,
    DateTimeOffset CapturedUtc,
    string InputMechanism,
    bool DedicatedFieldFocused,
    bool StationBusy,
    bool EnterTerminated,
    int CharacterCount,
    int TextChangeEvents,
    double ElapsedMilliseconds,
    string DecisionCode,
    bool Accepted,
    string? RawText);

public sealed class LabScannerCaptureProbe
{
    private readonly TimeProvider _time;
    private bool _armed;
    private int _edits;
    private long _startTimestamp;
    private string? _lastText;

    public LabScannerCaptureProbe(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void FocusAcquired()
    {
        _armed = true;
        _edits = 0;
        _lastText = null;
        _startTimestamp = _time.GetTimestamp();
    }

    public void FocusLost()
    {
        _armed = false;
        _edits = 0;
        _lastText = null;
    }

    public void TextChanged(string? text, bool dedicatedFieldFocused)
    {
        if (!_armed || !dedicatedFieldFocused) return;
        if (string.Equals(text, _lastText, StringComparison.Ordinal)) return;
        _edits++;
        _lastText = text;
    }

    public LabScannerCaptureObservation CompleteEnter(string? raw, bool focused, bool busy)
    {
        var admitted = LabScannerAdmission.Admit(raw, _armed && focused, busy);
        // A bare Enter on prefilled text is not proof of a new scanner acquisition.
        var code = admitted.Allowed && _edits == 0 ? "SCAN_NO_NEW_INPUT" : admitted.Code;
        var accepted = admitted.Allowed && _edits > 0;
        var result = new LabScannerCaptureObservation(
            Guid.NewGuid(), _time.GetUtcNow(), "FOCUSED_KEYBOARD_WEDGE_OR_MANUAL_UNVERIFIED",
            _armed && focused, busy, true, raw?.TrimEnd('\r', '\n').Length ?? 0,
            _edits, _armed ? Math.Max(0, _time.GetElapsedTime(_startTimestamp).TotalMilliseconds) : 0,
            code, accepted, raw?.TrimEnd('\r', '\n'));
        // Remain armed for the next physical scan, but never reuse the previous edit count.
        if (focused) FocusAcquired(); else FocusLost();
        return result;
    }
}
