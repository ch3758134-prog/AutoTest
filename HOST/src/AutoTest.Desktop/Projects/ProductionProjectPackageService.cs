using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

public sealed record ProductionPackageFile(string RelativePath, string Sha256, long Length);
public sealed record ProductionPackageManifest(
    int SchemaVersion, Guid ProjectId, int ProjectRevision, string ProjectName,
    string ManifestSha256, string FlashJobFingerprint,
    IReadOnlyList<ProductionPackageFile> Files)
{
    public const int CurrentSchema = 1;
}
public sealed record InstalledProductionPackage(
    Guid ProjectId, string ProjectName, int ProjectRevision, string PackageSha256,
    string ManifestPath, DateTimeOffset InstalledAtUtc);

/// <summary>
/// The engineering export and production import use the same canonical TEST/DBC,
/// WAVE and native FLASH validators. No mock resources, external paths or source
/// workspace references are allowed in an installed package.
/// </summary>
public static class ProductionProjectPackageService
{
    public const string Extension = ".atpkg";
    public const string MetadataEntry = "autotest-package.json";
    public const string ProjectManifestName = "project.autotest-project.json";
    private const int MaxEntries = 4096;
    private const long MaxFileBytes = 128L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<ProductionPackageManifest> ExportAsync(
        string projectManifestPath, string destination, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetFileName(projectManifestPath), ProjectManifestName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择完整 AutoTest 工程清单。");
        var manifestPath = Path.GetFullPath(projectManifestPath);
        var root = Path.GetDirectoryName(manifestPath)!;
        var project = await ProjectRepository.OpenDefinitionAsync(manifestPath, ct);
        await ProjectBundleContractAudit.ValidateAsync(project, manifestPath, ct);
        var draft = await NativeFlashProfileStore.LoadAsync(root, ct)
            ?? throw new InvalidDataException("工程尚未保存原生 FLASH 参数。");
        var job = NativeFlashJobBuilder.Freeze(draft.Profile, root);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ProjectManifestName,
            NativeFlashProfileStore.RelativeProfilePath
        };
        foreach (var item in project.Assets) paths.Add(item.RelativePath.Replace('\\', '/'));
        paths.Add(draft.Profile.FirmwareRelativePath
            ?? throw new InvalidDataException("缺少工程固件资源。"));
        // SafetyFile is embedded in AutoTest itself, not another asset selected
        // for each ECU project or included in the production engineering bundle.

        if (paths.Count > MaxEntries)
            throw new InvalidDataException("工程文件数量超过导入限制。");
        var files = new List<ProductionPackageFile>();
        long total = 0;
        foreach (var relative in paths.OrderBy(x => x, StringComparer.Ordinal))
        {
            var safe = SafeRelative(relative);
            var full = NativeFlashProfileStore.InsideRoot(root, safe);
            if (!File.Exists(full))
                throw new FileNotFoundException("工程依赖文件不存在：" + safe, full);
            var info = new FileInfo(full);
            if (info.Length is <= 0 or > MaxFileBytes ||
                checked(total + info.Length) > MaxTotalBytes)
                throw new InvalidDataException("工程资源大小超出安全限制：" + safe);
            total += info.Length;
            await using var stream = File.OpenRead(full);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            var asset = project.Assets.FirstOrDefault(x =>
                string.Equals(x.RelativePath.Replace('\\', '/'), safe,
                    StringComparison.OrdinalIgnoreCase));
            if (asset is not null && !string.Equals(asset.Sha256, sha,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工程资源已被修改：" + asset.Name);
            files.Add(new(safe, sha, info.Length));
        }
        var manifestFile = files.Single(x => x.RelativePath == ProjectManifestName);
        var package = new ProductionPackageManifest(
            ProductionPackageManifest.CurrentSchema, project.Id.Value,
            project.Revision, project.Name, manifestFile.Sha256,
            job.Fingerprint, files);
        var target = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(target), Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请导出 .atpkg 工程包。");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                var header = zip.CreateEntry(MetadataEntry, CompressionLevel.Optimal);
                header.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using (var headerOutput = header.Open())
                    await JsonSerializer.SerializeAsync(headerOutput, package, Json, ct);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = header.LastWriteTime;
                    await using var source = File.OpenRead(
                        NativeFlashProfileStore.InsideRoot(root, file.RelativePath));
                    await using var destinationStream = entry.Open();
                    await source.CopyToAsync(destinationStream, ct);
                    if (source.Length != file.Length)
                        throw new IOException("工程资源在导出期间发生变化。");
                    source.Position = 0;
                    if (!string.Equals(Convert.ToHexString(
                            await SHA256.HashDataAsync(source, ct)), file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new IOException("工程资源在导出期间被修改。");
                }
            }
            // Reopen and independently validate the finished archive before replacing output.
            await ValidateArchiveAsync(temporary, ct);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return package;
    }

    public static async Task<ProductionPackageManifest> ValidateArchiveAsync(
        string archivePath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return await ReadAndValidateArchiveAsync(zip, extractionRoot: null, ct);
    }

    /// <summary>
    /// Atomic immutable install. A corrupt or incomplete archive never replaces
    /// a previously installed version. The returned path is application-owned.
    /// </summary>
    public static async Task<InstalledProductionPackage> InstallAsync(
        string archivePath, string libraryRoot, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(libraryRoot);
        Directory.CreateDirectory(root);
        await using var packageStream = File.OpenRead(archivePath);
        var packageHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageStream, ct));
        using var archive = ZipFile.OpenRead(archivePath);
        var header = await ReadHeaderAsync(archive, ct);
        var projectDir = Path.Combine(root, header.ProjectId.ToString("N"));
        var target = Path.Combine(projectDir, packageHash);
        if (Directory.Exists(target))
        {
            await ValidateInstalledAsync(target, header, ct);
            return new(header.ProjectId, header.ProjectName,
                header.ProjectRevision, packageHash,
                Path.Combine(target, ProjectManifestName), DateTimeOffset.UtcNow);
        }
        Directory.CreateDirectory(projectDir);
        var stage = Path.Combine(projectDir, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var extracted = await ReadAndValidateArchiveAsync(archive, stage, ct);
            await ValidateInstalledAsync(stage, extracted, ct);
            await File.WriteAllBytesAsync(Path.Combine(stage, MetadataEntry),
                JsonSerializer.SerializeToUtf8Bytes(extracted, Json), ct);
            ct.ThrowIfCancellationRequested();
            // Directory rename is atomic on the local Windows volume.
            if (Directory.Exists(target))
                await ValidateInstalledAsync(target, extracted, ct);
            else
                Directory.Move(stage, target);
            return new(extracted.ProjectId, extracted.ProjectName,
                extracted.ProjectRevision, packageHash,
                Path.Combine(target, ProjectManifestName), DateTimeOffset.UtcNow);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    public static async Task ValidateInstalledAsync(
        string root, ProductionPackageManifest expected, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(root, ProjectManifestName);
        var definition = await ProjectRepository.OpenDefinitionAsync(manifestPath, ct);
        if (definition.Id.Value != expected.ProjectId ||
            definition.Revision != expected.ProjectRevision ||
            definition.Name != expected.ProjectName)
            throw new InvalidDataException("工程包身份与内容不一致。");
        foreach (var file in expected.Files)
        {
            var path = NativeFlashProfileStore.InsideRoot(root, SafeRelative(file.RelativePath));
            if (!File.Exists(path)) throw new InvalidDataException("工程包缺少：" + file.RelativePath);
            await using var stream = File.OpenRead(path);
            if (stream.Length != file.Length || !string.Equals(Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, ct)), file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工程包资源已损坏：" + file.RelativePath);
        }
        await ProjectBundleContractAudit.ValidateAsync(definition, manifestPath, ct);
        var profile = await NativeFlashProfileStore.LoadAsync(root, ct)
            ?? throw new InvalidDataException("工程包缺少原生 FLASH 配置。");
        var job = NativeFlashJobBuilder.Freeze(profile.Profile, root);
        if (job.Fingerprint != expected.FlashJobFingerprint)
            throw new InvalidDataException("工程包烧录版本不一致。");
    }

    private static async Task<ProductionPackageManifest> ReadHeaderAsync(
        ZipArchive archive, CancellationToken ct)
    {
        var entries = archive.Entries;
        if (entries.Count is < 2 or > MaxEntries + 1)
            throw new InvalidDataException("工程包文件数量无效。");
        var metadata = entries.SingleOrDefault(x => x.FullName == MetadataEntry)
            ?? throw new InvalidDataException("不是 AutoTest 工程包。");
        if (metadata.Length is < 1 or > 2_000_000)
            throw new InvalidDataException("工程包目录无效。");
        await using var stream = metadata.Open();
        var header = await JsonSerializer.DeserializeAsync<ProductionPackageManifest>(
            stream, Json, ct) ?? throw new InvalidDataException("工程包目录为空。");
        if (header.SchemaVersion != ProductionPackageManifest.CurrentSchema ||
            header.ProjectId == Guid.Empty || header.ProjectRevision < 1 ||
            string.IsNullOrWhiteSpace(header.ProjectName) ||
            header.Files is null || header.Files.Count is < 2 or > MaxEntries ||
            !ValidHash(header.ManifestSha256) ||
            !ValidHash(header.FlashJobFingerprint))
            throw new InvalidDataException("工程包版本或身份无效。");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var file in header.Files)
        {
            if (file is null || !unique.Add(SafeRelative(file.RelativePath)) ||
                !ValidHash(file.Sha256) || file.Length is <= 0 or > MaxFileBytes ||
                checked(size += file.Length) > MaxTotalBytes)
                throw new InvalidDataException("工程包资源目录无效。");
        }
        if (!unique.Contains(ProjectManifestName) ||
            !string.Equals(header.Files.Single(x =>
                string.Equals(x.RelativePath, ProjectManifestName,
                    StringComparison.OrdinalIgnoreCase)).Sha256,
                header.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程包清单身份不一致。");
        return header;
    }

    private static async Task<ProductionPackageManifest> ReadAndValidateArchiveAsync(
        ZipArchive archive, string? extractionRoot, CancellationToken ct)
    {
        var header = await ReadHeaderAsync(archive, ct);
        var members = archive.Entries.Where(x => x.FullName != MetadataEntry).ToArray();
        if (members.Length != header.Files.Count)
            throw new InvalidDataException("工程包文件数量与目录不符。");
        var expected = header.Files.ToDictionary(x => x.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in members)
        {
            ct.ThrowIfCancellationRequested();
            var name = SafeRelative(entry.FullName);
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000 || !visited.Add(name) ||
                !expected.TryGetValue(name, out var file) ||
                entry.Length != file.Length)
                throw new InvalidDataException("工程包包含非法、重复或未登记的文件。");
            await using var input = entry.Open();
            if (extractionRoot is null)
            {
                using var hash = SHA256.Create();
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    total += read;
                    if (total > file.Length) throw new InvalidDataException("工程包内容超出声明长度。");
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock([], 0, 0);
                if (total != file.Length || !string.Equals(
                        Convert.ToHexString(hash.Hash!), file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("工程包资源校验失败：" + name);
            }
            else
            {
                var full = ProjectPathPolicy.Normalize(extractionRoot, name);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await using (var output = new FileStream(full, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    using var hash = SHA256.Create();
                    while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
                    {
                        total += read;
                        if (total > file.Length)
                            throw new InvalidDataException("工程包内容超出声明长度。");
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        hash.TransformBlock(buffer, 0, read, buffer, 0);
                    }
                    hash.TransformFinalBlock([], 0, 0);
                    if (total != file.Length || !string.Equals(
                            Convert.ToHexString(hash.Hash!), file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("工程包资源校验失败：" + name);
                }
            }
        }
        return header;
    }

    private static string SafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':') ||
            path.StartsWith('/') || path.StartsWith('\\') ||
            path.Contains('\\') || path.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("工程包内含非法文件路径。");
        return path;
    }

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
