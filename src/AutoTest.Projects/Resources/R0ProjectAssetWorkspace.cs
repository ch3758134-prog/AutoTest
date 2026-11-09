using System.Collections.Immutable;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.ControllerConfig;

namespace AutoTest.Projects.Resources;

/// <summary>
/// PROJECT-owned offline asset verification and copy-on-write revision publication.
/// This is not a global ResourceRef contract or an automatic reference remapper.
/// </summary>
public sealed record R0AssetCheck(AssetEntry Asset, string FullPath, string Status,
    string ActualSha256, string Diagnostic)
{
    public bool IsValid => Status == "OK";
}

public sealed class R0ProjectAssetWorkspace
{
    private readonly string _path;
    private string _manifestSha;
    private ImmutableArray<R0AssetCheck> _checks = [];
    public ProjectDefinition Definition { get; private set; }
    public string ProjectPath => _path;
    public IReadOnlyList<R0AssetCheck> Assets => _checks;
    public IReadOnlyList<AssetDependency> StaleDependencies => ReferenceValidator.ScanStale(Definition);

    private R0ProjectAssetWorkspace(string path, ProjectDefinition definition, string sha)
    {
        _path = path;
        Definition = definition;
        _manifestSha = sha;
    }

    public static async Task<R0ProjectAssetWorkspace> OpenAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var hashBefore = await HashFileAsync(path, ct);
        var definition = await ProjectRepository.OpenDefinitionAsync(path, ct);
        var hashAfter = await HashFileAsync(path, ct);
        if (!string.Equals(hashBefore, hashAfter, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("PROJECT_CONFLICT", "Manifest changed during project open.");
        var workspace = new R0ProjectAssetWorkspace(path, definition, hashAfter);
        await workspace.RefreshChecksAsync(ct);
        return workspace;
    }

    public R0AssetCheck Find(AssetId id) => _checks.FirstOrDefault(a => a.Asset.Id == id)
        ?? throw new ProjectDataException("ASSET_NOT_FOUND", "AssetId does not belong to this project.");

    public static async Task<R0ProjectAssetWorkspace> CreateAsync(string projectPath, string projectName,
        CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        if (File.Exists(path)) throw new ProjectDataException("PROJECT_EXISTS", "Choose a new manifest path; existing projects cannot be overwritten.");
        var created = ProjectDefinition.Create(projectName);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".create";
        try
        {
            await ProjectRepository.SaveDefinitionAsync(created, tempPath, ct);
            File.Move(tempPath, path);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        return await OpenAsync(path, ct);
    }

    /// <summary>Import a copy of the file. No source file is ever modified or deleted.</summary>
    public async Task<R0AssetCheck> ImportAsync(string sourcePath, string kind, string name,
        CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if ((kind.Equals("DBC", StringComparison.OrdinalIgnoreCase) && ext != ".dbc") ||
            ((kind.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
              kind.Equals("Profile", StringComparison.OrdinalIgnoreCase)) && ext != ".json") ||
            !(kind.Equals("DBC", StringComparison.OrdinalIgnoreCase) ||
              kind.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
              kind.Equals("Profile", StringComparison.OrdinalIgnoreCase)))
            throw new ProjectDataException("ASSET_KIND", "R0 imports only .dbc DBC and .json Controller/Profile resources.");
        var info = new FileInfo(source);
        if (!info.Exists || info.Length < 1 || info.Length > 4 * 1024 * 1024)
            throw new ProjectDataException("ASSET_SIZE", "Resource is missing or exceeds R0 4 MiB limit.");
        var content = await File.ReadAllBytesAsync(source, ct);
        if (kind.Equals("DBC", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = DbcSource.Load(content);
            DbcWorkbenchDocument.Import(Path.GetFileNameWithoutExtension(source), parsed.Text);
        }
        else
        {
            await ControllerRepository.OpenAsync(source, ct);
        }
        var id = AssetId.New();
        var relative = $"assets/{id.Value:N}{ext}";
        var root = Path.GetDirectoryName(_path)!;
        var destination = ProjectPathPolicy.Normalize(root, relative);
        var asset = new AssetEntry(id, 1, kind, name, relative, Hash(content)).Validate();
        await RequireCurrentManifestAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var moved = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, content, ct);
            if (!string.Equals(await HashFileAsync(temporary, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("ASSET_HASH", "Copied resource did not match its fingerprint.");
            File.Move(temporary, destination);
            moved = true;
            var next = (Definition with
            {
                Revision = checked(Definition.Revision + 1),
                Assets = Definition.Assets.Add(asset)
            }).Validate();
            await CommitManifestAsync(next, ct);
            return Find(id);
        }
        catch
        {
            if (moved && File.Exists(destination))
            {
                var current = await ProjectRepository.OpenDefinitionAsync(_path, CancellationToken.None);
                if (!current.Assets.Any(a => a.Id == id)) File.Delete(destination);
            }
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task RenameAsync(AssetId id, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName)) throw new ProjectDataException("ASSET_NAME", "Resource name is required.");
        var entry = Find(id).Asset;
        var index = Definition.Assets.IndexOf(entry);
        var next = (Definition with
        {
            Revision = checked(Definition.Revision + 1),
            Assets = Definition.Assets.SetItem(index, entry with { Name = newName })
        }).Validate();
        await CommitManifestAsync(next, ct);
    }

    public async Task UnlinkAsync(AssetId id, CancellationToken ct = default)
    {
        var entry = Find(id).Asset;
        if (Definition.Dependencies.Any(d => d.From == id || d.To == id))
            throw new ProjectDataException("ASSET_REFERENCED", "Asset has dependent resources; unlink refused.");
        var next = (Definition with
        {
            Revision = checked(Definition.Revision + 1),
            Assets = Definition.Assets.Remove(entry)
        }).Validate();
        await CommitManifestAsync(next, ct);
    }

    public async Task<R0AssetCheck> VerifyPinnedAssetAsync(AssetId id, int expectedRevision,
        string expectedSha256, CancellationToken ct = default)
    {
        await RequireCurrentManifestAsync(ct);
        await RefreshChecksAsync(ct);
        var current = Find(id);
        if (current.Asset.Revision != expectedRevision ||
            !string.Equals(current.Asset.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("REFERENCE_STALE", "Pinned asset revision/hash differs from the active project.");
        if (!current.IsValid)
            throw new ProjectDataException("ASSET_STALE", current.Diagnostic);
        return current;
    }

    private async Task RequireCurrentManifestAsync(CancellationToken ct)
    {
        if (!string.Equals(await HashFileAsync(_path, ct), _manifestSha, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("PROJECT_CONFLICT", "Project manifest changed outside this tool.");
    }

    private async Task CommitManifestAsync(ProjectDefinition next, CancellationToken ct)
    {
        await RequireCurrentManifestAsync(ct);
        await ProjectRepository.SaveDefinitionAsync(next, _path, ct);
        Definition = next;
        _manifestSha = await HashFileAsync(_path, ct);
        await RefreshChecksAsync(ct);
    }

    public async Task RefreshChecksAsync(CancellationToken ct = default)
    {
        var root = Path.GetDirectoryName(_path)!;
        var checkedAssets = ImmutableArray.CreateBuilder<R0AssetCheck>();
        foreach (var asset in Definition.Assets)
        {
            ct.ThrowIfCancellationRequested();
            var full = ProjectPathPolicy.Normalize(root, asset.RelativePath);
            if (!File.Exists(full))
            {
                checkedAssets.Add(new(asset, full, "MISSING", "", "Asset file is missing."));
                continue;
            }
            var actual = await HashFileAsync(full, ct);
            checkedAssets.Add(string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase)
                ? new(asset, full, "OK", actual, "Fingerprint matches the project manifest.")
                : new(asset, full, "HASH_MISMATCH", actual, "Asset content differs from the manifest revision."));
        }
        _checks = checkedAssets.ToImmutable();
    }

    public async Task<R0AssetCheck> PublishRevisionAsync(AssetId assetId, ReadOnlyMemory<byte> bytes,
        CancellationToken ct = default)
    {
        var check = Find(assetId);
        if (!check.IsValid) throw new ProjectDataException("ASSET_STALE", check.Diagnostic);
        if (bytes.Length is 0 or > 4 * 1024 * 1024)
            throw new ProjectDataException("ASSET_SIZE", "R0 DBC/Controller revision must be 1..4 MiB.");
        if (!string.Equals(await HashFileAsync(_path, ct), _manifestSha, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("PROJECT_CONFLICT", "Project manifest changed outside this editor.");
        if (!string.Equals(await HashFileAsync(check.FullPath, ct), check.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("ASSET_STALE", "Asset file changed outside this editor.");
        var nextHash = Hash(bytes.Span);
        if (string.Equals(nextHash, check.Asset.Sha256, StringComparison.OrdinalIgnoreCase)) return check;

        var nextRev = checked(check.Asset.Revision + 1);
        var currentRelative = check.Asset.RelativePath.Replace('\\', '/');
        var slash = currentRelative.LastIndexOf('/');
        var folder = slash < 0 ? "" : currentRelative[..(slash + 1)];
        var name = slash < 0 ? currentRelative : currentRelative[(slash + 1)..];
        var extension = Path.GetExtension(name);
        var newRelative = $"{folder}{check.Asset.Id.Value:N}.r{nextRev}{extension}";
        var root = Path.GetDirectoryName(_path)!;
        var destination = ProjectPathPolicy.Normalize(root, newRelative);
        if (File.Exists(destination)) throw new ProjectDataException("ASSET_REVISION_CONFLICT", "Revision output already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var moved = false;
        try
        {
            await File.WriteAllBytesAsync(temp, bytes.ToArray(), ct);
            if (!string.Equals(await HashFileAsync(temp, ct), nextHash, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("ASSET_HASH", "Staged asset hash mismatch.");
            ct.ThrowIfCancellationRequested();
            File.Move(temp, destination);
            moved = true;
            var updated = (check.Asset with { Revision = nextRev, Sha256 = nextHash, RelativePath = newRelative }).Validate();
            var index = Definition.Assets.IndexOf(check.Asset);
            var newDefinition = (Definition with
            {
                Revision = checked(Definition.Revision + 1),
                Assets = Definition.Assets.SetItem(index, updated)
            }).Validate();
            if (!string.Equals(await HashFileAsync(_path, ct), _manifestSha, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("PROJECT_CONFLICT", "Project manifest changed during publication.");
            await ProjectRepository.SaveDefinitionAsync(newDefinition, _path, ct);
            Definition = newDefinition;
            _manifestSha = await HashFileAsync(_path, ct);
            await RefreshChecksAsync(ct);
            return Find(assetId);
        }
        catch
        {
            if (moved)
            {
                var manifest = await ProjectRepository.OpenDefinitionAsync(_path, CancellationToken.None);
                if (!manifest.Assets.Any(a => a.RelativePath == newRelative) && File.Exists(destination))
                    File.Delete(destination);
            }
            throw;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task<string> HashFileAsync(string file, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
}
