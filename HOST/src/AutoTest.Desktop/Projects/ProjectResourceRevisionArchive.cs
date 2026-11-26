using System.IO;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Content-verified, local immutable authoring history. It is NOT a production
/// publication, does not authorize older hardware resources, and is not a
/// substitute for a signed release. Saves fail before replacing the current
/// file if its previously pinned bytes cannot be archived.
/// </summary>
public static class ProjectResourceRevisionArchive
{
    private static string PathFor(string root, AssetId id,
        int revision, string sha256)
    {
        if (id.Value == Guid.Empty || revision < 1 ||
            sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("历史资源身份、修订或 SHA256 无效。");
        var relative = System.IO.Path.Combine(".autotest-history",
            id.Value.ToString("N"),
            revision.ToString("D8") + "-" +
                sha256.ToLowerInvariant() + ".bin");
        return ProjectPathPolicy.Normalize(root, relative);
    }

    private static bool Matches(byte[] bytes, string expectedSha256) =>
        Convert.ToHexString(SHA256.HashData(bytes))
            .Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);

    public static async Task PreserveCurrentAsync(string root, AssetEntry asset,
        string currentFilePath, CancellationToken ct = default)
    {
        var archive = PathFor(root, asset.Id, asset.Revision, asset.Sha256);
        var bytes = await File.ReadAllBytesAsync(currentFilePath, ct)
            .ConfigureAwait(false);
        if (!Matches(bytes, asset.Sha256))
            throw new InvalidDataException("工程资产原文件与当前修订指纹不一致，拒绝伪造历史版本：" +
                asset.Name);
        if (File.Exists(archive))
        {
            var existing = await File.ReadAllBytesAsync(archive, ct)
                .ConfigureAwait(false);
            if (!Matches(existing, asset.Sha256))
                throw new InvalidDataException("历史资产已存在但校验失败：" + asset.Name);
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archive)!);
        var temporary = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct)
                .ConfigureAwait(false);
            if (!Matches(await File.ReadAllBytesAsync(temporary, ct)
                .ConfigureAwait(false), asset.Sha256))
                throw new IOException("历史资产写盘后校验失败：" + asset.Name);
            try { File.Move(temporary, archive); }
            catch (IOException) when (File.Exists(archive))
            {
                var existing = await File.ReadAllBytesAsync(archive, ct)
                    .ConfigureAwait(false);
                if (!Matches(existing, asset.Sha256))
                    throw new InvalidDataException("历史资产出现同名不同内容冲突。");
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Return null for genuinely unavailable old content. Never
    /// substitute the current source, a newer revision or a mock snapshot.</summary>
    public static async Task<byte[]?> TryReadAsync(string root, AssetId id,
        int revision, string sha256, CancellationToken ct = default)
    {
        var archive = PathFor(root, id, revision, sha256);
        if (!File.Exists(archive)) return null;
        var bytes = await File.ReadAllBytesAsync(archive, ct)
            .ConfigureAwait(false);
        if (!Matches(bytes, sha256))
            throw new InvalidDataException("历史资源内容校验失败，不允许据此审查旧修订。");
        return bytes;
    }
}
