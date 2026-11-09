using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

/// <summary>
/// Exports every imported Replay row, independently of the bounded UI operation journal.
/// No CAN/USB session is opened; output is always local and never overwrites prior evidence.
/// </summary>
public sealed record DbcRecordedReplayEvidenceReceipt(
    string Path, string Sha256, int FrameCount, bool RawHexIncluded);

public static class DbcRecordedReplayEvidenceWriter
{
    public static async Task<DbcRecordedReplayEvidenceReceipt> ExportAsync(
        string file, DbcRecordedReplayResult replay, bool includeRawHex = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (string.IsNullOrWhiteSpace(file))
            throw new ProjectDataException("DBC_ARCHIVE_EXPORT_PATH", "Choose a local .jsonl evidence path.");
        var path = Path.GetFullPath(file);
        if (!string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("DBC_ARCHIVE_EXPORT_PATH", "Recorded Replay evidence requires .jsonl extension.");
        if (File.Exists(path))
            throw new ProjectDataException("DBC_ARCHIVE_EXPORT_EXISTS", "Existing evidence cannot be overwritten.");
        if (replay.Frames.IsDefault || replay.Frames.Length != replay.TotalFrames)
            throw new ProjectDataException("DBC_ARCHIVE_EXPORT_INCOMPLETE", "Replay rows are absent or incomplete.");

        var parent = Path.GetDirectoryName(path)
            ?? throw new ProjectDataException("DBC_ARCHIVE_EXPORT_PATH", "Invalid evidence directory.");
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, "." + Path.GetFileName(path) +
            "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 81920, leaveOpen: true))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        Type = "AUTOTEST_PROJECT_DBC_RECORDED_REPLAY", SchemaVersion = 1,
                        ExportedUtc = DateTimeOffset.UtcNow,
                        replay.RecordId, replay.ArchiveSha256, replay.DbcAssetId,
                        replay.DbcRevision, replay.DbcSha256, replay.Source,
                        replay.ClockQuality, replay.TotalFrames, replay.DecodedFrames,
                        replay.UnmappedFrames, replay.RejectedFrames,
                        replay.SkippedKeypadFrames, replay.MaximumReportedLostFrames,
                        RawHexIncluded = includeRawHex,
                        Confidentiality = "LOCAL_ONLY_REVIEW_BEFORE_SHARING",
                        Note = "Replay archive timestamps are reconstructed, NOT verified ECU hardware timestamps."
                    }));
                    foreach (var frame in replay.Frames)
                    {
                        ct.ThrowIfCancellationRequested();
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            Type = "CAN_RX_REPLAY_ROW", frame.FrameIndex,
                            frame.ReconstructedUtc, frame.OffsetMicroseconds,
                            frame.BusRole, frame.FrameId, frame.Extended,
                            PayloadHex = includeRawHex ? frame.PayloadHex : "[OMITTED]",
                            frame.TraceEvidence, frame.LostFrames, frame.Status,
                            frame.ErrorCode, frame.DecodedSummary
                        }));
                    }
                    await writer.FlushAsync(ct);
                }
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            // Never replace an existing evidence file: a duplicate name is a validation failure.
            File.Move(temporary, path);
            await using var read = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, FileOptions.Asynchronous);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct)).ToLowerInvariant();
            return new(path, hash, replay.TotalFrames, includeRawHex);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
