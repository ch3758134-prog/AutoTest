using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoTest.Desktop.Workbench;

public enum WorkbenchSeverity { Neutral, Progress, Success, Warning, Error }

public sealed record WorkbenchMessage(DateTimeOffset When, WorkbenchSeverity Severity,
    string Heading, string Details, string? OperationId = null, string? TraceReference = null);

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool visible && visible ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility == Visibility.Visible;
}

public static class WorkbenchDisplay
{
    public static string Source(string? value) => value switch
    {
        "Physical" => "Physical · 实物", "Replay" => "Replay · 回放",
        "Mock" => "Mock · 模拟", _ => "Unknown · 尚无事实"
    };
    public static string Win32(int? value) => value is int code
        ? $"{code} (0x{code:X8})" : "未提供（由 DEVICE 返回）";
}
