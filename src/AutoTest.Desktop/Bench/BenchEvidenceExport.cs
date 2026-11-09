using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTest.Desktop.Bench;

// UI-local evidence projection. The authoritative DEVICE/RECORD trace remains in its own service.
// An ACK is never a physical observation or an ECU verdict.
public enum BenchEvidencePhase
{
    Requested,
    TransportAccepted,
    Rejected,
    ManualMeasurement,
    Connection,
    Error
}

public sealed record BenchEvidenceEntry(
    DateTimeOffset TimestampUtc,
    BenchEvidencePhase Phase,
    BenchEvidenceSource Source,
    string ProjectRevision,
    string? ControllerId,
    string? StateId,
    string? DeviceIdentity,
    long? SessionGeneration,
    string? OperationId,
    string? CorrelationId,
    string? RequestHex,
    string? ResponseHex,
    string? TraceReference,
    string? FailureStage,
    string? ErrorCode,
    string Summary)
{
    public string TimestampDisplay => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string PhaseDisplay => Phase switch
    {
        BenchEvidencePhase.Requested => "请求",
        BenchEvidencePhase.TransportAccepted => "传输接受（非物理观测）",
        BenchEvidencePhase.Rejected => "拒绝",
        BenchEvidencePhase.ManualMeasurement => "人工测量（非 ECU 反馈）",
        BenchEvidencePhase.Connection => "连接状态",
        _ => "错误"
    };
}

public static class BenchEvidenceExport
{
    public const string Schema = "autotest.r0.bench-evidence.v1";

    public static string ToJson(IEnumerable<BenchEvidenceEntry> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            disclaimer = "UI-local evidence projection. PhysicalObserved and EcuFeedback are UNKNOWN unless a separate authoritative source supplies them. Raw DEVICE/RECORD traces are not embedded.",
            exportedAtUtc = DateTimeOffset.UtcNow,
            entries = events.ToArray()
        }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
    }

    public static string ToTsv(IEnumerable<BenchEvidenceEntry> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var b = new StringBuilder();
        b.AppendLine("TimestampUtc\tPhase\tSource\tProjectRevision\tControllerId\tStateId\tDeviceIdentity\tSessionGeneration\tOperationId\tCorrelationId\tRequestHex\tResponseHex\tTraceReference\tFailureStage\tErrorCode\tSummary");
        foreach (var e in events)
        {
            var fields = new[]
            {
                e.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                e.Phase.ToString(), e.Source.ToString(), e.ProjectRevision, e.ControllerId,
                e.StateId, e.DeviceIdentity, e.SessionGeneration?.ToString(CultureInfo.InvariantCulture),
                e.OperationId, e.CorrelationId, e.RequestHex, e.ResponseHex, e.TraceReference,
                e.FailureStage, e.ErrorCode, e.Summary
            };
            b.AppendLine(string.Join("\t", fields.Select(Escape)));
        }
        return b.ToString();
    }

    private static string Escape(string? value) => (value ?? "")
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
