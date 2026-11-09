using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Contracts;
using AutoTest.Projects.Resources;

namespace AutoTest.Projects.Project;

public sealed class ProjectRepository : IProjectService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    private sealed record StoredProject(int SchemaVersion, string Id, int Revision, string Name,
        StoredAsset[] Assets, StoredDependency[] Dependencies);
    private sealed record StoredAsset(string Id, int Revision, string Kind, string Name, string RelativePath, string Sha256);
    private sealed record StoredDependency(string From, string To, int RequiredRevision, string Purpose);
    public static ProjectDefinition Create(string name) => ProjectDefinition.Create(name);
    public static async Task SaveDefinitionAsync(ProjectDefinition project, string path, CancellationToken ct = default)
    {
        project.Validate();
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ProjectDataException("PROJECT_PATH", "Invalid project path.");
        Directory.CreateDirectory(dir);
        foreach (var asset in project.Assets) ProjectPathPolicy.Normalize(dir, asset.RelativePath);
        var data = new StoredProject(project.SchemaVersion, project.Id.Value.ToString("D"), project.Revision, project.Name,
            project.Assets.Select(a => new StoredAsset(a.Id.Value.ToString("D"), a.Revision, a.Kind, a.Name, a.RelativePath, a.Sha256)).ToArray(),
            project.Dependencies.Select(d => new StoredDependency(d.From.Value.ToString("D"), d.To.Value.ToString("D"), d.RequiredRevision, d.Purpose)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        var temporary = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            // Same-directory rename provides atomic content replacement on the supported Windows host.
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<ProjectDefinition> OpenDefinitionAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var bytes = await File.ReadAllBytesAsync(full, ct);
            if (bytes.Length > 16 * 1024 * 1024) throw new ProjectDataException("PROJECT_TOO_LARGE", "Project manifest exceeds size limit.");
            var stored = JsonSerializer.Deserialize<StoredProject>(bytes, Json)
                ?? throw new ProjectDataException("PROJECT_CORRUPT", "Project JSON is null.");
            if (stored.SchemaVersion != ProjectDefinition.CurrentSchema)
                throw new ProjectDataException("PROJECT_VERSION", "Unsupported project format version.");
            if (!ProjectId.TryParse(stored.Id, out var id) || stored.Assets is null || stored.Dependencies is null)
                throw new ProjectDataException("PROJECT_CORRUPT", "Invalid project identifier or collections.");
            var assets = stored.Assets.Select(a =>
            {
                if (a is null || !AssetId.TryParse(a.Id, out var assetId)) throw new ProjectDataException("PROJECT_CORRUPT", "Invalid asset id.");
                ProjectPathPolicy.Normalize(Path.GetDirectoryName(full)!, a.RelativePath);
                return new AssetEntry(assetId, a.Revision, a.Kind, a.Name, a.RelativePath, a.Sha256);
            }).ToImmutableArray();
            var dependencies = stored.Dependencies.Select(d =>
            {
                if (d is null || !AssetId.TryParse(d.From, out var from) || !AssetId.TryParse(d.To, out var to))
                    throw new ProjectDataException("PROJECT_CORRUPT", "Invalid dependency identifier.");
                return new AssetDependency(from, to, d.RequiredRevision, d.Purpose);
            }).ToImmutableArray();
            return new ProjectDefinition(id, stored.SchemaVersion, stored.Revision, stored.Name, assets, dependencies).Validate();
        }
        catch (JsonException e) { throw new ProjectDataException("PROJECT_CORRUPT", $"Invalid project JSON: {e.Message}"); }
    }
    public async ValueTask<OperationResult<ProjectSnapshot>> OpenAsync(string path, CancellationToken ct)
    {
        try
        {
            var definition = await OpenDefinitionAsync(path, ct);
            var fingerprint = RevisionFingerprint.FromBytes(await File.ReadAllBytesAsync(path, ct));
            return OperationResult<ProjectSnapshot>.Accepted(new ProjectSnapshot(definition.Id,
                new ProjectRevision(definition.Revision), fingerprint,
                definition.Assets.Select(a => a.ToReference()).ToImmutableArray(), definition.SchemaVersion));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ProjectDataException or JsonException)
        { return OperationResult<ProjectSnapshot>.Rejected(e is ProjectDataException x ? x.Code : "PROJECT_IO", e.Message); }
    }
    public async ValueTask<OperationResult<SaveReceipt>> SaveAsync(ProjectSaveRequest request, CancellationToken ct)
    {
        try
        {
            var path = Path.GetFullPath(request.Path);
            if (File.Exists(path))
            {
                var hash = RevisionFingerprint.FromBytes(await File.ReadAllBytesAsync(path, ct));
                if (!hash.Matches(request.ExpectedOnDisk))
                    return OperationResult<SaveReceipt>.Rejected("PROJECT_CONFLICT", "Project has changed on disk.");
            }
            else if (!string.IsNullOrEmpty(request.ExpectedOnDisk.Sha256))
                return OperationResult<SaveReceipt>.Rejected("PROJECT_CONFLICT", "Expected project file is missing.");
            var assets = request.ExpectedSnapshot.Assets.Select((a, i) => new AssetEntry(a.Id, a.Revision.Value,
                "external", "Asset " + (i + 1), "assets/" + a.Id.Value.ToString("N"), a.Fingerprint.Sha256)).ToImmutableArray();
            var project = new ProjectDefinition(request.ExpectedSnapshot.Id, request.ExpectedSnapshot.SchemaVersion,
                request.ExpectedSnapshot.Revision.Value, "AutoTest", assets, ImmutableArray<AssetDependency>.Empty);
            await SaveDefinitionAsync(project, path, ct);
            var written = RevisionFingerprint.FromBytes(await File.ReadAllBytesAsync(path, ct));
            return OperationResult<SaveReceipt>.Accepted(new SaveReceipt(project.Id, written, true, path));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ProjectDataException)
        { return OperationResult<SaveReceipt>.Rejected(e is ProjectDataException x ? x.Code : "PROJECT_IO", e.Message); }
    }
}
