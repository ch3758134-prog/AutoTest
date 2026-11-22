using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.FlashLab.Programming;

namespace AutoTest.FlashLab.Programming;

/// <summary>
/// Production uses ONLY a previously published immutable profile, not the
/// operator's autosaved engineering draft. Publication is a configuration
/// decision, never a physical FLASH consent or UUT release.
/// </summary>
public static class NativeFlashPublicationStore
{
    public const string CurrentRelativePath = "assets/flash/current-release.json";
    private sealed record ReleasePointer(int SchemaVersion, string ProfileSha256,
        string JobFingerprint, string ModelCode, DateTimeOffset PublishedAtUtc);
    public sealed record PublishedProfile(NativeFlashProfileDocument Profile,
        string ProfileSha256, string JobFingerprint, DateTimeOffset PublishedAtUtc);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<PublishedProfile> PublishAsync(string root,
        string expectedDraftSha256, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var draft = await NativeFlashProfileStore.LoadAsync(root, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("原生烧录草稿不存在。");
            if (!string.Equals(draft.ProfileSha256, expectedDraftSha256,
                StringComparison.OrdinalIgnoreCase))
                throw new IOException("配置在发布前已更改，必须重新核对。");
            var job = await Task.Run(() => NativeFlashJobBuilder.Freeze(
                draft.Profile, root), ct).ConfigureAwait(false);

            var versionPath = NativeFlashProfileStore.InsideRoot(root,
                "assets/flash/published/" + draft.ProfileSha256 + ".profile.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
            var data = await File.ReadAllBytesAsync(draft.FullPath, ct)
                .ConfigureAwait(false);
            var contentHash = Convert.ToHexString(SHA256.HashData(data));
            if (!string.Equals(contentHash, expectedDraftSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("工程草稿在发布时变更，已拒绝。");
            if (File.Exists(versionPath))
            {
                var existing = await File.ReadAllBytesAsync(versionPath, ct)
                    .ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(existing)),
                        contentHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("历史发布快照被篡改，拒绝覆盖。");
            }
            else
            {
                var tmp = versionPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(tmp, data, ct).ConfigureAwait(false);
                    File.Move(tmp, versionPath, overwrite: false);
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            var pointerPath = NativeFlashProfileStore.InsideRoot(root, CurrentRelativePath);
            var pointer = new ReleasePointer(1, contentHash, job.Fingerprint,
                draft.Profile.EcuModelCode, DateTimeOffset.UtcNow);
            var temp = pointerPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temp,
                    JsonSerializer.SerializeToUtf8Bytes(pointer, Json), ct)
                    .ConfigureAwait(false);
                File.Move(temp, pointerPath, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new(draft.Profile, contentHash, job.Fingerprint,
                pointer.PublishedAtUtc);
        }
        finally { Gate.Release(); }
    }

    public static async Task<PublishedProfile?> LoadPublishedAsync(string root,
        CancellationToken ct = default)
    {
        var currentPath = NativeFlashProfileStore.InsideRoot(root, CurrentRelativePath);
        if (!File.Exists(currentPath)) return null;
        var pointerBytes = await File.ReadAllBytesAsync(currentPath, ct).ConfigureAwait(false);
        if (pointerBytes.Length > 4096)
            throw new InvalidDataException("当前烧录生效配置指针损坏。");
        var pointer = JsonSerializer.Deserialize<ReleasePointer>(pointerBytes, Json)
            ?? throw new InvalidDataException("当前烧录生效配置为空。");
        if (pointer.SchemaVersion != 1 || pointer.ProfileSha256 is not { Length: 64 } ||
            !pointer.ProfileSha256.All(Uri.IsHexDigit) ||
            pointer.JobFingerprint is not { Length: 64 } ||
            !pointer.JobFingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("已发布烧录配置指纹无效。");
        var versionPath = NativeFlashProfileStore.InsideRoot(root,
            "assets/flash/published/" + pointer.ProfileSha256 + ".profile.json");
        if (!File.Exists(versionPath))
            throw new FileNotFoundException("已发布原生 Profile 快照不存在。", versionPath);
        var bytes = await File.ReadAllBytesAsync(versionPath, ct).ConfigureAwait(false);
        var computed = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(pointer.ProfileSha256, computed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("生产烧录 Profile 快照已损坏或被修改。");
        var profile = JsonSerializer.Deserialize<NativeFlashProfileDocument>(bytes, Json)
            ?? throw new InvalidDataException("已发布烧录 Profile JSON 内容无效。");
        profile.ValidateForFreeze();
        if (pointer.ModelCode != profile.EcuModelCode)
            throw new InvalidDataException("发布信息的 ECU 型号与配置内容不一致。");
        return new(profile, computed, pointer.JobFingerprint, pointer.PublishedAtUtc);
    }

    public static CanToolFlashJob FreezePublished(string root, PublishedProfile release)
    {
        var job = NativeFlashJobBuilder.Freeze(release.Profile, root);
        if (!string.Equals(job.Fingerprint, release.JobFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("已发布 JobSHA 与当前固件/安全算法/原生方法不一致，拒绝烧录。");
        return job;
    }
}
