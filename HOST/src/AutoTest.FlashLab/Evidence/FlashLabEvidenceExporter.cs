using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AutoTest.FlashLab.Programming;

namespace AutoTest.FlashLab.Evidence;

/// <summary>
/// Export a diagnostic archive of actual session facts. Never invent a PASS or
/// confuse "prepared programming plan" with an ECU programming result.
/// Seed/Key bytes and the fixed SafetyFile binary are never exported.
/// </summary>
public static class FlashLabEvidenceExporter
{
    public static void Export(string zipPath, string? rawJsonl,
        NativeFlashProfileDocument? profile, CanToolFlashJob? prepared,
        string? stageText, IEnumerable<string> visibleLog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        var traceIncluded = !string.IsNullOrWhiteSpace(rawJsonl) && File.Exists(rawJsonl);
        var summary = new
        {
            Product = "AutoTest FlashLab",
            ExportedUtc = DateTimeOffset.UtcNow,
            Scope = "REAL_SESSION_DIAGNOSTICS",
            ResultPolicy = "A prepared plan alone is not a successful ECU flash. Use actual CAN/UDS evidence, per-block acknowledgments, reset and APP verification.",
            Profile = profile is null ? null : new
            {
                profile.EcuModelCode,
                profile.ProgrammingMethodId,
                profile.FirmwareDisplayName,
                profile.FirmwareSha256,
                profile.EcuCanKbps,
                profile.AppRequestCanId,
                profile.AppResponseCanId,
                profile.BootRequestCanId,
                profile.BootResponseCanId,
                profile.FlashStart,
                profile.FlashEndInclusive,
                profile.BlockLength
            },
            PreparedPlan = prepared is null ? null : new
            {
                prepared.Fingerprint,
                prepared.FirmwareSha256,
                prepared.SecuritySha256,
                prepared.SecurityProviderKind,
                prepared.FlashStart,
                prepared.FlashEndInclusive,
                prepared.PlannedBytes,
                BlockCount = prepared.Blocks.Length
            },
            StageDisplayAtExport = stageText,
            RawDeviceTraceIncluded = traceIncluded,
            Important = "Only raw hardware observations are evidence of actual ECU behavior. Operator must confirm power, target identity and station safety."
        };
        Write(zip, "session.json", JsonSerializer.Serialize(summary,
            new JsonSerializerOptions { WriteIndented = true }));
        Write(zip, "ui-log.txt", string.Join(Environment.NewLine, visibleLog));
        if (traceIncluded)
        {
            var entry = zip.CreateEntry("device-raw.jsonl", CompressionLevel.Optimal);
            using var dest = entry.Open();
            using var src = new FileStream(rawJsonl!, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);
            src.CopyTo(dest);
        }
    }

    private static void Write(ZipArchive zip, string name, string body)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        stream.Write(body);
    }
}
