using System.IO;
using System.Text.Json;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// A parent Inspection is one ECU attempt, not one TEST case occurrence.
/// These records are station-owned and must not mutate an installed ECU
/// project's resource directory. Only a committed terminal parent outcome
/// is counted as PASS or FAIL.
/// </summary>
public static class ProductionInspectionArchive
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "records", "inspections");

    public static string FolderFor(DateTimeOffset startedUtc, string? root = null) =>
        Path.Combine(root ?? Root, startedUtc.ToLocalTime().ToString("yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture));

    public sealed record DaySummary(int Passed, int Failed, int Incomplete,
        int Active, int Corrupt)
    {
        public int Finished => Passed + Failed + Incomplete;
        public string Display => "今日检测 " + Finished + " 台 · PASS " +
            Passed + " · FAIL " + Failed +
            (Incomplete == 0 ? "" : " · 未完成 " + Incomplete) +
            (Active == 0 ? "" : " · 检测中 " + Active) +
            (Corrupt == 0 ? "" : " · 记录异常 " + Corrupt);
    }

    public static DaySummary ReadDay(DateTime? localDate = null,
        string? archiveRoot = null)
    {
        var today = (localDate ?? DateTime.Today).Date;
        var folder = Path.Combine(archiveRoot ?? Root,
            today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        if (!Directory.Exists(folder)) return new(0, 0, 0, 0, 0);
        var passed = 0;
        var failed = 0;
        var incomplete = 0;
        var active = 0;
        var corrupt = 0;
        var seen = new HashSet<Guid>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Length is < 2 or > 8_000_000)
                {
                    corrupt++;
                    continue;
                }
                using var input = File.OpenRead(path);
                using var document = JsonDocument.Parse(input,
                    new JsonDocumentOptions { MaxDepth = 24 });
                var data = document.RootElement;
                if (data.GetProperty("Mode").GetString() != "PRODUCTION")
                    continue;
                var run = data.GetProperty("VerificationId").GetGuid();
                if (run == Guid.Empty || !seen.Add(run))
                {
                    corrupt++;
                    continue;
                }
                if (data.GetProperty("StartedUtc").GetDateTimeOffset()
                        .ToLocalTime().Date != today)
                {
                    corrupt++;
                    continue;
                }
                if (data.GetProperty("EndedUtc").ValueKind == JsonValueKind.Null)
                {
                    active++;
                    continue;
                }
                switch (data.GetProperty("Outcome").GetString())
                {
                    case "生产检测 PASS": passed++; break;
                    case "生产检测 FAIL": failed++; break;
                    default: incomplete++; break;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or
                                       FormatException or KeyNotFoundException or
                                       InvalidOperationException or UnauthorizedAccessException)
            {
                corrupt++;
            }
        }
        return new(passed, failed, incomplete, active, corrupt);
    }
}
