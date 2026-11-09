using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>WAVE-only diagnostic projection, not a new global logging contract.</summary>
public sealed record WaveRawFrameRow(
    int Index, DateTimeOffset TimestampUtc, long OffsetMicroseconds, string BusRole,
    string CanId, bool Extended, string DataHex, int Dlc, long ReportedLostFrames,
    string TraceEvidenceId, string DecodedSummary, string ErrorCode);

public sealed record WaveArchiveReport(
    Guid RecordId, string ArchiveSha256, string DbcSha256, int DbcRevision,
    DateTimeOffset StartUtc, int TotalFrames, long MaximumReportedLostFrames,
    int DisplayedFrames, bool DisplayTruncated, string TimeSemantics,
    string ValidationCode, IReadOnlyList<WaveRawFrameRow> Frames);

public interface IWaveArchiveDiagnosticSource
{
    WaveArchiveReport ReadArchiveReport(int maxRows, string? filter = null);
    byte[] ReadVerifiedOriginalArchive();
}

public sealed record WaveOperationLogRow(
    DateTimeOffset TimestampUtc, string Operation, string Stage, string State,
    string ErrorCode, string Detail, string Source, long DurationMilliseconds);

/// <summary>
/// Exports local WAVE diagnostic evidence without altering the global RECORD schema.
/// Never downsample export data. The archive is copied byte-for-byte with its SHA-256.
/// </summary>
public static class WaveLabExportService
{
    public static void Export(string destination, WaveArchiveReport? archiveReport,
        byte[]? originalArchive, IEnumerable<WaveOperationLogRow> operations,
        IEnumerable<WaveWorkbenchCapture> captures, IEnumerable<WaveResultRow> comparisons,
        string origin, string quality, bool includeRawEvidence = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var full = Path.GetFullPath(destination);
        if (!full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("诊断包必须使用 .zip 扩展名", nameof(destination));
        if (File.Exists(full)) throw new IOException("WAVE_EXPORT_EXISTS: 目标诊断包已存在，不允许静默覆盖");
        var folder = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        if ((archiveReport is null) != (originalArchive is null))
            throw new InvalidDataException("WAVE_EXPORT_ORIGIN_MISMATCH: 缺少已验证的原始档案");
        if (archiveReport is not null && !string.Equals(
            Convert.ToHexString(SHA256.HashData(originalArchive!)), archiveReport.ArchiveSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WAVE_EXPORT_SOURCE_CHANGED: 原始 CAN 档案哈希不匹配");
        var opRows = operations.ToArray();
        var captureRows = captures.ToArray();
        var comparisonRows = comparisons.ToArray();
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                WriteText(zip, "manifest.json", JsonSerializer.Serialize(new
                {
                    Schema = "AutoTest.R0.WaveDiagnostic/1",
                    CandidateSourceBaseSha = "d9040c461f56617cfaba3821250c6efef0c7e20d",
                    RunningHostBuildSha = "UNKNOWN_NOT_VERIFIED",
                    ExportDataPolicy = "Local user-approved diagnostics only; review the raw CAN RX payload before sharing.",
                    PhysicalObservation = "UNKNOWN - the Replay and Mock paths do not verify ECU/physical behavior",
                    IncludeRawEvidence = includeRawEvidence,
                    Redaction = includeRawEvidence ? "NONE: contains raw CAN bytes and decoded values" :
                        "REDACTED: CAN payloads, decoded data, sample values and free-form log detail are omitted",
                    ExportedAtUtc = DateTimeOffset.UtcNow,
                    WorkbenchSource = origin,
                    WorkbenchQuality = quality,
                    Archive = archiveReport is null ? null : new
                    {
                        archiveReport.RecordId, archiveReport.ArchiveSha256,
                        archiveReport.DbcSha256, archiveReport.DbcRevision,
                        archiveReport.StartUtc, archiveReport.TotalFrames,
                        archiveReport.MaximumReportedLostFrames, archiveReport.TimeSemantics,
                        archiveReport.ValidationCode
                    },
                    Captures = captureRows.Select(c => new
                    {
                        EvidenceId = c.Evidence.Evidence.Value,
                        Source = c.Kind.ToString(), c.SourceLabel,
                        Signal = c.Series.Signal.SignalName,
                        Unit = c.Series.Signal.Unit,
                        DbcSha256 = c.Series.SourceRevision.Sha256,
                        SampleCount = c.Series.Samples.Length,
                        c.Series.ObservedEvents, c.Series.RejectedEvents,
                        c.Series.Overflow, c.Series.NonMonotonic,
                        c.Series.MixedClock, c.Series.InvalidInput, c.Series.HasGap,
                        TimeSource = c.Series.Origin.Source.ToString(),
                        TimeQuality = c.Series.Origin.Quality.ToString()
                    }).ToArray(),
                    Disclaimer = "Replay/Mock results are software-only, not physical or production release evidence. " +
                        "Original device clock quality cannot be inferred from replay offsets."
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (includeRawEvidence && originalArchive is not null)
                {
                    using var stream = zip.CreateEntry("original-can-rx.json", CompressionLevel.Optimal).Open();
                    stream.Write(originalArchive);
                }
                WriteCsv(zip, "operations.csv",
                    ["Utc", "Operation", "Stage", "State", "ErrorCode", "Detail", "Source", "DurationMs"],
                    opRows.Select(x => new[] { Time(x.TimestampUtc), x.Operation, x.Stage, x.State,
                        x.ErrorCode, includeRawEvidence ? x.Detail : "[REDACTED]",
                        x.Source, Invariant(x.DurationMilliseconds) }));
                WriteCsv(zip, "comparisons.csv",
                    ["Signal", "Verdict", "Detail", "Coverage", "ComparisonEvidenceId"],
                    comparisonRows.Select(x => new[] { x.Signal, x.Verdict,
                        includeRawEvidence ? x.Detail : "[REDACTED]", x.Coverage, x.EvidenceId }));
                if (archiveReport is not null)
                    WriteCsv(zip, "raw-can.csv",
                        ["Index", "Utc", "OffsetUs", "BusRole", "CanId", "Extended", "Dlc", "Hex", "LostFramesReported", "TraceEvidenceId", "Decoded", "ErrorCode"],
                        archiveReport.Frames.Select(x => new[] { Invariant(x.Index), Time(x.TimestampUtc),
                            Invariant(x.OffsetMicroseconds), x.BusRole, x.CanId, x.Extended.ToString(),
                            Invariant(x.Dlc), includeRawEvidence ? x.DataHex : "[REDACTED]",
                            Invariant(x.ReportedLostFrames), x.TraceEvidenceId,
                            includeRawEvidence ? x.DecodedSummary : "[REDACTED]", x.ErrorCode }));
                WriteCsv(zip, "samples.csv",
                    ["CaptureEvidenceId", "InputKind", "Signal", "Unit", "Utc", "MonotonicRaw", "TimeSource", "TimeQuality", "Value", "Gap", "InputEvidenceId", "InputQuality"],
                    captureRows.SelectMany(c => c.Series.Samples.Select(x => new[]
                    {
                        c.Evidence.Evidence.Value.ToString("D"), c.Kind.ToString(),
                        c.Series.Signal.SignalName, c.Series.Signal.Unit, Time(x.Sample.Time.Utc),
                        Invariant(x.Sample.Time.MonotonicTicks), x.Sample.Time.Source.ToString(),
                        x.Sample.Time.Quality.ToString(),
                        includeRawEvidence ? Invariant(x.Sample.Value) : "[REDACTED]",
                        x.Sample.Gap.ToString(), x.SourceEvidence.Value.ToString("D"),
                        x.InputQuality.ToString()
                    })));
            }
            File.Move(temporary, full); // no overwrite; atomic in the destination directory
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteText(ZipArchive zip, string path, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(),
            new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void WriteCsv(ZipArchive zip, string path, string[] headers,
        IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(),
            new UTF8Encoding(false));
        writer.WriteLine(string.Join(',', headers.Select(Csv)));
        foreach (var fields in rows) writer.WriteLine(string.Join(',', fields.Select(Csv)));
    }

    private static string Csv(string? value)
    {
        var s = value ?? string.Empty;
        // CSV exported into office tools must not execute attacker-controlled DBC names/formulas.
        if (s.Length != 0 && (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r')) s = "'" + s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }

    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Invariant(double number) => number.ToString("R", CultureInfo.InvariantCulture);
    private static string Invariant(long number) => number.ToString(CultureInfo.InvariantCulture);
    private static string Invariant(int number) => number.ToString(CultureInfo.InvariantCulture);
}
