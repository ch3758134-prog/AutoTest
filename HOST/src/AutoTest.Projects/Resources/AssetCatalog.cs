using AutoTest.Core;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Resources;

public sealed class AssetCatalog
{
    private readonly Dictionary<AssetId, AssetEntry> _entries = new();
    public IReadOnlyCollection<AssetEntry> Entries => _entries.Values.ToArray();
    public AssetCatalog(IEnumerable<AssetEntry>? initial = null)
    {
        foreach (var entry in initial ?? [])
        {
            entry.Validate();
            if (!_entries.TryAdd(entry.Id, entry)) throw new ProjectDataException("ASSET_DUPLICATE", "Duplicate asset id.");
        }
    }
    public AssetEntry Import(string kind, string name, string relativePath, ReadOnlySpan<byte> bytes)
    {
        var entry = AssetEntry.Create(kind, name, relativePath, bytes);
        _entries.Add(entry.Id, entry); return entry;
    }
    public AssetEntry ReplaceRevision(AssetId id, ReadOnlySpan<byte> bytes)
    {
        var old = Get(id);
        var updated = old with { Revision = checked(old.Revision + 1), Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() };
        return _entries[id] = updated;
    }
    public AssetEntry Get(AssetId id) => _entries.TryGetValue(id, out var value) ? value :
        throw new ProjectDataException("ASSET_NOT_FOUND", $"Asset {id} not found.");
    public AutoTest.Core.OperationResult<AssetRef> Resolve(AssetId id, AssetRevision revision)
    {
        if (!_entries.TryGetValue(id, out var asset)) return AutoTest.Core.OperationResult<AssetRef>.Rejected("ASSET_NOT_FOUND", "Asset does not exist.");
        if (asset.Revision != revision.Value) return AutoTest.Core.OperationResult<AssetRef>.Rejected("REFERENCE_STALE", "Asset revision mismatch.");
        return AutoTest.Core.OperationResult<AssetRef>.Accepted(asset.ToReference());
    }
    public void Rename(AssetId id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ProjectDataException("ASSET_NAME", "Name is required.");
        _entries[id] = Get(id) with { Name = name };
    }
    public void Remove(AssetId id, IEnumerable<AssetDependency> dependencies)
    {
        if (dependencies.Any(d => d.From == id || d.To == id))
            throw new ProjectDataException("ASSET_REFERENCED", "Remove or remap references before deleting the asset.");
        if (!_entries.Remove(id)) throw new ProjectDataException("ASSET_NOT_FOUND", "Asset not found.");
    }
}
