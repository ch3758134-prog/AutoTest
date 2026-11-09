using System;

namespace AutoTest.Specialists.Scan.Lab;

public sealed record LabScanAdmission(bool Allowed, string Code, string NormalizedText);

public static class LabScannerAdmission
{
    // USB keyboard wedge is indistinguishable from manual keyboard input without an assigned HID
    // device. Never intercept global keyboard focus or treat any USB device as a controller.
    public static LabScanAdmission Admit(string? raw, bool dedicatedFieldFocused, bool busy)
    {
        if (busy) return new(false, "STATION_BUSY_SCAN_REJECTED", "");
        if (!dedicatedFieldFocused) return new(false, "SCANNER_FOCUS_REQUIRED", "");
        if (raw is null) return new(false, "SCAN_EMPTY", "");
        var value = raw.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(value)) return new(false, "SCAN_EMPTY", "");
        if (value.Length > 4096 || value.IndexOf('\0') >= 0)
            return new(false, "SCAN_INVALID", "");
        return new(true, "SCAN_CAPTURED", value);
    }
}
