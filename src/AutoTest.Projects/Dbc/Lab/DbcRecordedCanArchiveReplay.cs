using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

/// <summary>
/// Reads the existing Wave RecordedCanArchive v1 JSON *without* depending on the WAVE assembly.
/// This is a private, read-only import adapter, not a competing public CAN archive contract.
/// The archive has no proven physical clock quality; derived timestamps are not ECU hardware time.
/// </summary>
public sealed record DbcRecordedReplayRow(
    int FrameIndex, DateTimeOffset ReconstructedUtc, long OffsetMicroseconds,
    string BusRole, uint FrameId, bool Extended, string PayloadHex, Guid TraceEvidence,
    long LostFrames, string Status, string ErrorCode, string DecodedSummary);

public sealed record DbcRecordedReplayResult(
    Guid RecordId, string ArchiveSha256, Guid DbcAssetId, int DbcRevision,
    string DbcSha256, string Source, string ClockQuality, int TotalFrames,
    int DecodedFrames, int UnmappedFrames, int RejectedFrames, int SkippedKeypadFrames,
    long MaximumReportedLostFrames, ImmutableArray<DbcRecordedReplayRow> Frames);

public static class DbcRecordedCanArchiveReplay
{
    private const long MaxFileBytes = 32_000_000;

    private sealed record Archive(int SchemaVersion, Guid RecordId, Guid DbcId,
        int DbcRevision, string DbcSha256, DateTimeOffset StartUtc, Frame[] Frames);
    private sealed record Frame(long OffsetMicroseconds, JsonElement Role, uint FrameId,
        bool Extended, string PayloadHex, long LostFrames, Guid TraceEvidence);

    /// <summary>
    /// A bounded offline reader. No physical CAN, USB or Wave publisher is opened.
    /// The entire archive is validated before any decoded row is returned.
    /// </summary>
    public static async Task<DbcRecordedReplayResult> ReadAsync(
        string path, DbcWorkbenchDocument document, DbcRevisionBinding binding,
        int maxFrames = 10000, CancellationToken ct = default)
    {
        if (document is null || binding is null)
            throw new ProjectDataException("DBC_ARCHIVE_BINDING", "A pinned DBC resource is required.");
        if (maxFrames is < 2 or > 200000)
            throw new ProjectDataException("DBC_ARCHIVE_BUDGET", "Frame budget must be between 2 and 200000.");
        if (string.IsNullOrWhiteSpace(path))
            throw new ProjectDataException("DBC_ARCHIVE_PATH", "Select a recorded CAN archive first.");
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists || info.Length is <= 0 or > MaxFileBytes)
            throw new ProjectDataException("DBC_ARCHIVE_SIZE", "Missing, empty or oversized CAN archive.");
        var data = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        if (data.LongLength > MaxFileBytes)
            throw new ProjectDataException("DBC_ARCHIVE_SIZE", "CAN archive changed or exceeds the size limit.");

        Archive archive;
        try
        {
            archive = JsonSerializer.Deserialize<Archive>(data)
                ?? throw new ProjectDataException("DBC_ARCHIVE_SCHEMA", "Empty archive document.");
        }
        catch (JsonException e)
        {
            throw new ProjectDataException("DBC_ARCHIVE_JSON", "Malformed recorded CAN JSON: " + e.Message);
        }
        if (archive.SchemaVersion != 1 || archive.RecordId == Guid.Empty ||
            archive.DbcId == Guid.Empty || archive.DbcRevision < 1 ||
            archive.StartUtc == default || archive.Frames is null ||
            archive.Frames.Length is < 2 or > 200000 ||
            archive.DbcSha256 is null || archive.DbcSha256.Length != 64 ||
            !archive.DbcSha256.All(Uri.IsHexDigit))
            throw new ProjectDataException("DBC_ARCHIVE_SCHEMA", "RecordedCanArchive v1 fields are invalid.");
        if (archive.Frames.Length > maxFrames)
            throw new ProjectDataException("DBC_ARCHIVE_BUDGET",
                "Archive exceeds the explicit Replay frame budget; no silent truncation.");
        if (archive.DbcId != binding.DbcAssetId ||
            archive.DbcRevision != binding.Revision ||
            !string.Equals(archive.DbcSha256, binding.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("REFERENCE_STALE", "CAN archive and pinned DBC resource differ.");

        var rows = ImmutableArray.CreateBuilder<DbcRecordedReplayRow>(archive.Frames.Length);
        var lastOffset = -1L;
        var decodedCount = 0;
        var unmappedCount = 0;
        var rejectedCount = 0;
        var skippedCount = 0;
        var maxLoss = 0L;
        for (var i = 0; i < archive.Frames.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var frame = archive.Frames[i];
            if (frame is null || frame.OffsetMicroseconds <= lastOffset ||
                frame.OffsetMicroseconds < 0 || frame.OffsetMicroseconds > 3_600_000_000L)
                throw new ProjectDataException("DBC_ARCHIVE_ORDER", "Invalid or non-increasing frame offset at index " + i + ".");
            var role = ReadRole(frame.Role, i);
            if (frame.TraceEvidence == Guid.Empty || frame.LostFrames < 0 ||
                frame.PayloadHex is null || frame.PayloadHex.Length > 16 ||
                frame.PayloadHex.Length % 2 != 0 ||
                frame.FrameId > (frame.Extended ? 0x1FFFFFFFu : 0x7FFu))
                throw new ProjectDataException("DBC_ARCHIVE_FRAME", "Invalid CAN frame metadata at index " + i + ".");
            byte[] bytes;
            try { bytes = Convert.FromHexString(frame.PayloadHex); }
            catch (FormatException)
            {
                throw new ProjectDataException("DBC_ARCHIVE_HEX", "Invalid payload Hex at frame " + i + ".");
            }
            var observed = archive.StartUtc.ToUniversalTime().AddTicks(checked(frame.OffsetMicroseconds * 10));
            lastOffset = frame.OffsetMicroseconds;
            maxLoss = Math.Max(maxLoss, frame.LostFrames);

            string status;
            string errorCode = "";
            string summary = "";
            if (role == "KeypadCan")
            {
                status = "SKIPPED_KEYPAD";
                skippedCount++;
            }
            else if (!document.Model.Messages.Any(x => x.Id == frame.FrameId))
            {
                status = "UNMAPPED";
                errorCode = "DBC_MESSAGE_MISSING";
                unmappedCount++;
            }
            else
            {
                try
                {
                    var result = DbcReplayDecoder.Decode(document, frame.FrameId, bytes, observed,
                        frame.LostFrames, binding);
                    status = "DECODED";
                    summary = string.Join("; ", result.Signals.Select(s =>
                        s.SignalName + "=" + s.PhysicalValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        + (s.Unit.Length == 0 ? "" : " " + s.Unit)));
                    decodedCount++;
                }
                catch (ProjectDataException error)
                {
                    status = "REJECTED";
                    errorCode = error.Code;
                    summary = error.Message;
                    rejectedCount++;
                }
            }
            rows.Add(new(i, observed, frame.OffsetMicroseconds, role, frame.FrameId,
                frame.Extended, frame.PayloadHex.ToUpperInvariant(), frame.TraceEvidence,
                frame.LostFrames, status, errorCode, summary));
        }
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        return new(archive.RecordId, sha, archive.DbcId, archive.DbcRevision,
            archive.DbcSha256, "REPLAY_ARCHIVE_NOT_LIVE",
            "ArchiveStartUtc+OffsetMicroseconds; physical clock quality UNKNOWN",
            archive.Frames.Length, decodedCount, unmappedCount, rejectedCount,
            skippedCount, maxLoss, rows.ToImmutable());
    }

    private static string ReadRole(JsonElement value, int index)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            if (name is "DutCan" or "KeypadCan") return name;
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code))
        {
            if (code == 0) return "DutCan";
            if (code == 1) return "KeypadCan";
        }
        throw new ProjectDataException("DBC_ARCHIVE_ROLE", "Unsupported CAN BusRole at frame " + index + ".");
    }
}
