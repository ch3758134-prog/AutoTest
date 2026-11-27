using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.FlashLab.Firmware;
using AutoTest.FlashLab.Configuration;

namespace AutoTest.FlashLab.Programming;

/// <summary>
/// One current native FLASH parameter set in the ECU engineering project.
/// The source XML is not required; the user selects HEX, and AutoTest supplies
/// the fixed verified SafetyFile from its embedded installation resource.
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
        // Reject both directory junctions and symlinked target files. Otherwise
        // a manifest-local path could resolve to a mutable file outside the ECU
        // project without its change being visible in the relative path.
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("工程受控文件不能是链接。");
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
    /// One-time project-local migration of the user's HydEPB Configuration(2).xml
    /// reference values. No external XML is ever read at runtime. Existing
    /// intentional nonzero settings and pinned firmware/security resources stay
    /// untouched; a published immutable FLASH release is never modified.
    /// Called on engineering project open and again when its editor is opened.
    /// </summary>
    public static async Task<Snapshot?> EnsureHydEpbReferenceAsync(
        string root, string? modelCode, CancellationToken ct = default)
    {
        if (!string.Equals(modelCode, "HYDEPB", StringComparison.Ordinal))
            return await LoadAsync(root, ct).ConfigureAwait(false);

        // Retrying a concurrent revision change is safe: SaveDraftAsync checks
        // the exact last-read SHA, so this cannot override user edits.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await LoadAsync(root, ct).ConfigureAwait(false);
            if (existing is null)
            {
                try
                {
                    return await SaveDraftAsync(root,
                        NativeFlashProfileDocument.CreateHydEpbReferenceDraft(),
                        expectedSha256: null, ct).ConfigureAwait(false);
                }
                catch (IOException) when (attempt == 0 && File.Exists(FullProfilePath(root)))
                {
                    continue;
                }
            }
            else
            {
                if (!string.Equals(existing.Profile.EcuModelCode, "HYDEPB",
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "工程原生烧录配置的型号与 HYDEPB 不同，已拒绝自动替换。");
                var filled = existing.Profile.WithMissingHydEpbReference();
                if (Equals(filled, existing.Profile)) return existing;
                try
                {
                    return await SaveDraftAsync(root, filled,
                        existing.ProfileSha256, ct).ConfigureAwait(false);
                }
                catch (IOException) when (attempt == 0)
                {
                    continue;
                }
            }
        }
        throw new IOException("工程原生烧录草稿发生并发修改；请重新打开配置。");
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
                if (old is not null)
                {
                    if (!File.Exists(path))
                        throw new IOException("原生 Profile 在提交前被外部删除。");
                    var current = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                    var currentHash = Convert.ToHexString(SHA256.HashData(current));
                    if (!currentHash.Equals(old.ProfileSha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new IOException("原生 Profile 在保存过程中被外部修改，拒绝覆盖。");
                    File.Replace(temporary, path, path + ".bak");
                }
                else
                {
                    if (File.Exists(path))
                        throw new IOException("新原生 Profile 创建前已有其他写入者，拒绝覆盖。");
                    File.Move(temporary, path);
                }
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

    [Obsolete("The safety library is fixed in AutoTest; project import was retired.")]
    public static Task<ImportedAsset> ImportSecurityAsync(string root,
        string sourcePath, CancellationToken ct = default) =>
        throw new NotSupportedException("安全文件由 AutoTest 固定提供，不允许更换。");

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
        var security = FixedSafetyFile.EnsureAvailable();
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
