using System;

namespace AutoTest.LabLinkGui.LabLink;

public sealed class LabLinkLogEntry
{
    public LabLinkLogEntry(LabLinkUiEvent activity)
    {
        Timestamp = activity.Timestamp;
        Kind = activity.Kind;
        Message = activity.Message;
        DeviceIdentity = activity.DeviceIdentity ?? "";
        Stage = activity.Stage ?? "";
        Win32Error = activity.Win32Error;
        Sequence = activity.Sequence ?? "";
        RawHex = activity.RawHex ?? "";
        Trace = activity.Trace ?? "";
    }
    public DateTimeOffset Timestamp { get; }
    public string TimeText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    public LabLinkActivityKind Kind { get; }
    public string KindText => Kind.ToString();
    public string Message { get; }
    public string DeviceIdentity { get; }
    public string Stage { get; }
    public int? Win32Error { get; }
    public string Win32Text => Win32Error.HasValue ? $"{Win32Error.Value} (0x{Win32Error.Value:X8})" : "";
    public string Sequence { get; }
    public string RawHex { get; }
    public string Trace { get; }
    public string Detail => $"时间: {TimeText}\r\n类型: {KindText}\r\n信息: {Message}\r\n设备: {DeviceIdentity}\r\n阶段: {Stage}\r\nWin32: {Win32Text}\r\nSEQ: {Sequence}\r\n原始数据: {RawHex}\r\nTrace:\r\n{Trace}";
}

