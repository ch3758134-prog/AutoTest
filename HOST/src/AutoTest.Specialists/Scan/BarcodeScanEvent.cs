using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

// Scan input is not product identity. The source/focus/time evidence is kept unchanged.
public sealed record ScanIngressResult(bool Accepted, string Code, BarcodeScanEvent? Event)
{
    public static ScanIngressResult Reject(string code) => new(false, code, null);
    public static ScanIngressResult Accept(BarcodeScanEvent value) => new(true, "SCAN_ACCEPTED", value);
}

public static class ScanInputValidation
{
    public static string? ValidateSource(RawScan scan, InputPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(scan.Text)) return "SCAN_EMPTY";
        if (policy.MaxLength <= 0 || scan.Text.Length > policy.MaxLength) return "SCAN_LENGTH";
        if (scan.Text.Any(char.IsControl)) return "SCAN_CONTROL_CHAR";
        if (policy.RequireFocus && !scan.HadFocus) return "SCAN_FOCUS_DENIED";
        if (string.IsNullOrWhiteSpace(scan.Origin)) return "SCAN_SOURCE_MISSING";
        if (!string.IsNullOrWhiteSpace(policy.InputOrigin) &&
            !string.Equals(scan.Origin, policy.InputOrigin, StringComparison.Ordinal))
            return "SCAN_SOURCE_DENIED";
        return null;
    }

    public static RawScan WithTime(string text, string origin, TimePoint timestamp, bool focus) =>
        new(text, origin, timestamp, focus);
}
