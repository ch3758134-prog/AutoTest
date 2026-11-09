using System.IO;
using System.Text.Json;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.Desktop.Tools.Uds;

// R0 private UI evidence, not a replacement for the shared UdsTrace or CAN log contract.
// Frame timestamps are UI observation times; Mock frame groups are NOT a bus-order trace.
public sealed record UdsDiagnosticEvent(
    DateTimeOffset TimestampUtc, string Source, string Layer,
    string Direction, string CorrelationId, string RawHex,
    string DecodedSummary, string ErrorCode, bool Sensitive = false)
{
    // For Physical only, these originate from DEVICE's bound session evidence.
    // This timestamp is HostArrival/trace UTC, never an ECU sampling timestamp.
    public DateTimeOffset? HostFrameUtc { get; init; }
    public uint? CanId { get; init; }
    public string? TraceRef { get; init; }
    public long? SessionGeneration { get; init; }
    public long LostFrames { get; init; }
    public bool PhysicalEvidenceVerified { get; init; }
}

public static class UdsWorkbenchEvidence
{
    // SecurityAccess keys, authentication and programming/write payloads must
    // never be copied into an automatically generated diagnostic export.
    public static bool RequiresRedaction(ReadOnlySpan<byte> request) =>
        !request.IsEmpty && request[0] is 0x27 or 0x29 or 0x2E or 0x2F
            or 0x34 or 0x35 or 0x36 or 0x37;

    public static string SafeHex(bool sensitive, string hex) =>
        sensitive ? "[REDACTED: SENSITIVE UDS SERVICE]" : hex;

    public static string DescribeFrame(string hex)
    {
        try
        {
            var frame = UdsHex.Parse(hex);
            if (frame.Length is < 1 or > 8) return "DLC_INVALID";
            return (frame[0] >> 4) switch
            {
                0x0 when frame.Length >= 4 && frame[1] == 0x7F =>
                    "SF NEGATIVE_RESPONSE SID=0x" + frame[2].ToString("X2") +
                    " NRC=0x" + frame[3].ToString("X2") +
                    (frame[3] == 0x78 ? " RESPONSE_PENDING; P2* waiting applies" : ""),
                0x0 when frame.Length >= 2 && frame[1] >= 0x40 =>
                    "SF POSITIVE_RESPONSE SID=0x" + frame[1].ToString("X2") +
                    " length=" + (frame[0] & 0x0F),
                0x0 => "SF length=" + (frame[0] & 0x0F),
                0x1 when frame.Length >= 2 => "FF total=" +
                    (((frame[0] & 0x0F) << 8) | frame[1]),
                0x2 => "CF sequence=" + (frame[0] & 0x0F),
                0x3 when frame.Length >= 3 => "FC status=" +
                    (frame[0] & 0x0F) + " BS=" + frame[1] +
                    " STmin=0x" + frame[2].ToString("X2"),
                _ => "UNRECOGNIZED_PCI"
            };
        }
        catch (FormatException) { return "FRAME_HEX_INVALID"; }
    }

    public static async Task ExportAsync(string path, string source, string profile,
        string traceQuality, IReadOnlyList<UdsDiagnosticEvent> events,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (events.Count == 0) throw new InvalidOperationException("No diagnostic evidence to export.");
        var payload = new
        {
            Schema = "AutoTest.R0.UDS.DiagnosticExport/1",
            Source = events.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ? "Mixed Sources" : source,
            Profile = profile,
            PhysicalTxVerified = events.Any(e=>e.Source=="Physical" &&
                e.Layer=="CAN" && e.Direction=="TX" && e.PhysicalEvidenceVerified),
            PhysicalEcuResponseEvidenceAvailable = events.Any(e=>e.Source=="Physical" &&
                e.Layer=="CAN" && e.Direction=="RX" && e.PhysicalEvidenceVerified),
            TraceQuality = traceQuality,
            BuildCommit = "NOT_EMBEDDED_BY_BUILD",
            ResourceRevision = "NOT_REGISTERED",
            SessionGeneration = events.Where(e=>e.Source=="Physical" &&
                e.SessionGeneration.HasValue).Select(e=>e.SessionGeneration!.Value)
                .Distinct().Count() == 1
                    ? events.First(e=>e.Source=="Physical" &&
                        e.SessionGeneration.HasValue).SessionGeneration?.ToString()
                    : "UNVERIFIED_OR_MIXED",
            TimestampSemantic = "MOCK/REPLAY UI observation; PHYSICAL CAN HostArrivalUtc from DEVICE; NOT ECU clock",
            RawFrameOrdering = "MOCK HOST/ECU FRAME GROUPS ARE NOT CHRONOLOGICAL",
            ExportedUtc = DateTimeOffset.UtcNow,
            Events = events.Select(e => e.Sensitive ?
                e with { RawHex = "[REDACTED: SENSITIVE UDS SERVICE]" } : e).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload,
            new JsonSerializerOptions { WriteIndented = true });
        var absolute = Path.GetFullPath(path);
        var temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, absolute, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
