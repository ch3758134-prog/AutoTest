using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.FlashLab.Firmware;
using AutoTest.FlashLab.Configuration;

namespace AutoTest.FlashLab.Programming;

/// <summary>
/// One current native profile at a fixed project-relative location. The source
/// XML is never required. Imported firmware and security DLLs are content-addressed
/// local project resources, independent of the original external file location.
/// </summary>
public static class NativeFlashProfileStore
{
    public const string RelativeProfilePath = "assets/flash/native-profile.json";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public sealed record Snapshot(NativeFlashProfileDocument Profile,
        string ProfileSha256, string FullPath);
    public sealed record ImportedAsset(string RelativePath, string Sha256,
        long FileBytes);

    public static string FullProfilePath(string root) =>
        InsideRoot(root, RelativeProfilePath);

    public static string InsideRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Split('/', '\\').Any(part => part is "." or ".." or ""))
            throw new InvalidDataException("必须引用工程内合法的相对资源路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源路径越过工程根目录。");
        // Junctions and symlinks must not redirect trusted project assets.
        var parent = Path.GetDirectoryName(full)!;
        for (var probe = parent; probe.Length >= fullRoot.Length;
             probe = Path.GetDirectoryName(probe) ?? "")
        {
            if (Directory.Exists(probe) &&
                (File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("工程资源目录不能通过链接重定向。");
            if (string.Equals(probe, fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            if (probe.Length == 0) break;
        }
        return full;
    }

    public static async Task<Snapshot?> LoadAsync(string root,
        CancellationToken ct = default)
    {
        var file = FullProfilePath(root);
        if (!File.Exists(file)) return null;
        var data = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
        if (data.Length > 128 * 1024)
            throw new InvalidDataException("原生烧录 Profile 文件超过大小限制。");
        var document = JsonSerializer.Deserialize<NativeFlashProfileDocument>(data, Json)
            ?? throw new InvalidDataException("原生 Profile 文件为空。");
        document.ValidateDraft();
        return new(document, Convert.ToHexString(SHA256.HashData(data)), file);
    }

    /// <summary>
    /// Caller supplies last loaded SHA (or null when creating). Does not publish,
    /// authorize USB, or modify the active immutable Flash job.
    /// </summary>
    public static async Task<Snapshot> SaveDraftAsync(string root,
        NativeFlashProfileDocument draft, string? expectedSha256,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.ValidateDraft();
        var path = FullProfilePath(root);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var old = await LoadAsync(root, ct).ConfigureAwait(false);
            if (!string.Equals(old?.ProfileSha256, expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("原生烧录配置已被其他操作修改，请重新载入后编辑。");
            if (old is not null && old.Profile.ProfileId != draft.ProfileId)
                throw new InvalidDataException("不能替换已有原生 Profile 的稳定身份。");
            var next = draft with { Revision = (old?.Profile.Revision ?? 0) + 1 };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);
                return new(next, Convert.ToHexString(SHA256.HashData(bytes)), path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public static async Task<ImportedAsset> ImportFirmwareAsync(string root,
        string sourcePath, string? binBaseAddress, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(sourcePath);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > 32L * 1024 * 1024)
            throw new InvalidDataException("固件文件不存在、为空或超过 32MiB。");
        // Actual HEX/S19/BIN parser, not a filename-only check.
        var dummy = new CanToolXmlProfileReader();
        var parsed = FirmwareSelectionService.Load(path, binBaseAddress, dummy);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return await ImportContentAddressedAsync(root, path,
            "assets/flash/firmware", parsed.Sha256, ext, ct).ConfigureAwait(false);
    }

    public static async Task<ImportedAsset> ImportSecurityAsync(string root,
        string sourcePath, CancellationToken ct = default)
    {
        var inspected = CanToolSecurityProvider.Inspect(sourcePath);
        if (inspected.Kind != CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
            throw new NotSupportedException(
                "当前参考 HydEPB 工作流仅允许已核实 SHA 的原始 SafetyFile；未知 DLL 不可用于实体编程。");
        return await ImportContentAddressedAsync(root, inspected.Path,
            "assets/flash/security", inspected.Sha256, ".dll", ct).ConfigureAwait(false);
    }

    private static async Task<ImportedAsset> ImportContentAddressedAsync(string root,
        string source, string folder, string sha, string extension, CancellationToken ct)
    {
        var relative = folder + "/" + sha + extension;
        var destination = InsideRoot(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var sourceBytes = await File.ReadAllBytesAsync(source, ct).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(sourceBytes)), sha,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("导入过程中源资源发生修改，拒绝保存。");
        if (File.Exists(destination))
        {
            var live = await File.ReadAllBytesAsync(destination, ct).ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(live)), sha,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("工程内资源名称相同但内容 SHA 不符，已拒绝覆盖。");
        }
        else
        {
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, sourceBytes, ct).ConfigureAwait(false);
                File.Move(temp, destination, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return new(relative, sha, sourceBytes.LongLength);
    }
}

public static class NativeFlashJobBuilder
{
    public static CanToolFlashJob Freeze(NativeFlashProfileDocument profile,
        string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.ValidateForFreeze();
        var firmware = CheckedPinnedFile(projectRoot, profile.FirmwareRelativePath!,
            profile.FirmwareSha256!, "固件");
        var security = CheckedPinnedFile(projectRoot, profile.SecurityRelativePath!,
            profile.SecuritySha256!, "安全算法");
        // Calls the SAME proven F2 builder, but directly from strong typed
        // native parameters. No XML loading, generation or temp files.
        return CanToolFlashJob.Freeze(profile, firmware,
            profile.BinBaseAddress, security, profile.EcuModelCode);
    }

    private static string CheckedPinnedFile(string root, string relative,
        string sha256, string label)
    {
        var full = NativeFlashProfileStore.InsideRoot(root, relative);
        if (!File.Exists(full)) throw new FileNotFoundException(label + "工程资源不存在。", full);
        using var file = File.OpenRead(full);
        var actual = Convert.ToHexString(SHA256.HashData(file));
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException(label + "受控资源 SHA256 不一致，拒绝烧录。");
        return full;
    }
}
