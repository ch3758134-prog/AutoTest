using System.IO;
using System.Text.Json;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// One explicit barcode interpretation policy per offline station.
/// The port settings are separate; neither file can authorize an ECU action.
/// No per-scan writing: repeated scans must not change the configuration.
/// </summary>
public static class StationScanIdentityPolicyStore
{
    public static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "scanner-identity-policy.json");

    public static async Task<ScanIdentityPolicy?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(PathName)) return null;
        var bytes = await File.ReadAllBytesAsync(PathName, ct).ConfigureAwait(false);
        if (bytes.Length is <= 0 or > 16384)
            throw new InvalidDataException("扫码解析规则文件大小无效。");
        ScanIdentityPolicy value;
        try
        {
            value = JsonSerializer.Deserialize<ScanIdentityPolicy>(bytes)
                ?? throw new InvalidDataException("扫码解析规则为空。");
            value.Validate();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("扫码解析规则无法读取；保留原始文件。", error);
        }
        return value;
    }

    public static async Task SaveAsync(ScanIdentityPolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var target = PathName;
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary,
                JsonSerializer.SerializeToUtf8Bytes(policy,
                    new JsonSerializerOptions { WriteIndented = true }), ct)
                .ConfigureAwait(false);
            // Preserve previous good policy for manual recovery. Never restore
            // a damaged configuration automatically during a production scan.
            if (File.Exists(target))
                File.Copy(target, target + ".bak", overwrite: true);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
