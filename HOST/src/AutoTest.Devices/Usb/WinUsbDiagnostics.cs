using System.ComponentModel;
using System.Diagnostics;

namespace AutoTest.Devices.Usb;

/// <summary>Transport-layer operation, suitable for JSONL and a later unified logger.
/// Raw USB TX/RX bytes/SEQ stay in the existing UsbTraceRecord stream.</summary>
public sealed record WinUsbOperationEvent(
    DateTimeOffset Utc, long MonotonicTicks, string Operation, string Stage, string Outcome,
    string DeviceInstanceId, string? DevicePath, long ConnectionGeneration,
    int? NativeErrorCode, string? NativeErrorHex, string? Detail)
{
    public string Kind => "USB_NATIVE_OPERATION";
}

/// <summary>Preserves the exact failing WinUSB stage and GetLastError code.
/// Never interprets a particular Win32 code as proof of one root cause.</summary>
public sealed class WinUsbStageException : IOException
{
    public string Stage { get; }
    public int? NativeErrorCode { get; }
    public string? NativeErrorHex => NativeErrorCode is int code ? $"0x{unchecked((uint)code):X8}" : null;

    public WinUsbStageException(string stage, int? nativeErrorCode, string? explanation = null)
        : base(Format(stage, nativeErrorCode, explanation),
            nativeErrorCode is int code ? new Win32Exception(code) : null)
    {
        Stage = stage;
        NativeErrorCode = nativeErrorCode;
    }

    private static string Format(string stage, int? code, string? detail)
    {
        string native = code is int n
            ? $"Win32={n} (0x{unchecked((uint)n):X8}; {new Win32Exception(n).Message})"
            : "Win32=NOT_AVAILABLE";
        return $"WinUSB stage={stage}; {native}; {detail ?? "Connection state or descriptor requires inspection."}";
    }
}
