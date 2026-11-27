using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Physical station equipment ONLY. Barcode recognition and package version
/// selection never read this file. Saved settings do not authorize START,
/// FLASH, physical clamp movement or formal production PASS.
/// </summary>
public static class StationDeviceConfigurationStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "station-device");
    public static string PathName => Path.Combine(Root, "station-device.json");

    public static async Task<StationDeviceConfiguration> LoadAsync(
        CancellationToken ct = default)
    {
        if (!File.Exists(PathName)) return StationDeviceConfiguration.New();
        var bytes = await File.ReadAllBytesAsync(PathName, ct);
        if (bytes.Length is < 1 or > 64_000)
            throw new InvalidDataException("工位设备设置大小无效。");
        var config = JsonSerializer.Deserialize<StationDeviceConfiguration>(bytes, Json)
            ?? throw new InvalidDataException("工位设备设置为空。");
        config.Validate();
        return config;
    }

    public static async Task SaveAsync(StationDeviceConfiguration config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        await Gate.WaitAsync(ct);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(config, Json);
            if (bytes.Length > 64_000)
                throw new InvalidDataException("工位设备设置大小超出限制。");
            await AtomicAsync(PathName, bytes, ct);
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// When existing settings cannot be read, preserve the original bytes
    /// before writing an empty station draft. No legacy scanner settings are
    /// interpreted or imported.
    /// </summary>
    public static async Task ResetUnreadableAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(PathName))
                throw new IOException("当前工位设置文件已不存在，请重新打开窗口。");
            try
            {
                _ = await LoadAsync(ct);
                throw new InvalidOperationException(
                    "现有设备设置仍有效，不允许静默清空。");
            }
            catch (Exception error) when (error is InvalidDataException or
                JsonException or NotSupportedException)
            {
                // Invalid data requires explicit operator confirmation
                // through the device settings reset control.
            }

            var bytes = await File.ReadAllBytesAsync(PathName, ct);
            var backup = Path.Combine(Root, "station-device-backup-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
                Guid.NewGuid().ToString("N") + ".json");
            await AtomicAsync(backup, bytes, ct);
            var verified = await File.ReadAllBytesAsync(backup, ct);
            if (!SHA256.HashData(bytes).AsSpan()
                    .SequenceEqual(SHA256.HashData(verified)))
                throw new IOException("设备设置备份未通过校验，原文件保持不变。");
            var fresh = StationDeviceConfiguration.New();
            await AtomicAsync(PathName,
                JsonSerializer.SerializeToUtf8Bytes(fresh, Json), ct);
        }
        finally { Gate.Release(); }
    }

    private static async Task AtomicAsync(string path, byte[] bytes,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}