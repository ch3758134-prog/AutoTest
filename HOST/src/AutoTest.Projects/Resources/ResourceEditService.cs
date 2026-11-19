using AutoTest.Core;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Resources;

public sealed class ResourceEditService(ProjectDefinition project)
{
    private ProjectDefinition _current = project.Validate();
    private readonly Stack<ProjectDefinition> _history = new();
    public ProjectDefinition Current => _current;
    private void Commit(ProjectDefinition next)
    {
        next = next.Validate(); _history.Push(_current); _current = next.WithRevision();
    }
    public AssetEntry Add(string kind, string name, string path, ReadOnlySpan<byte> bytes)
    {
        var asset = AssetEntry.Create(kind, name, path, bytes);
        if (_current.Assets.Any(a => a.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            throw new ProjectDataException("ASSET_PATH_DUPLICATE", "An asset already uses the path.");
        Commit(_current with { Assets = _current.Assets.Add(asset) }); return asset;
    }
    public void Rename(AssetId id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ProjectDataException("ASSET_NAME", "Name is required.");
        var index = -1;
        for (var i = 0; i < _current.Assets.Length; i++)
            if (_current.Assets[i].Id == id) { index = i; break; }
        if (index < 0) throw new ProjectDataException("ASSET_NOT_FOUND", "Asset missing.");
        Commit(_current with { Assets = _current.Assets.SetItem(index, _current.Assets[index] with { Name = name }) });
    }
    public void Remove(AssetId id)
    {
        if (_current.Dependencies.Any(d => d.To == id || d.From == id))
            throw new ProjectDataException("ASSET_REFERENCED", "Asset is referenced.");
        var index = -1;
        for (var i = 0; i < _current.Assets.Length; i++)
            if (_current.Assets[i].Id == id) { index = i; break; }
        if (index < 0) throw new ProjectDataException("ASSET_NOT_FOUND", "Asset missing.");
        Commit(_current with { Assets = _current.Assets.RemoveAt(index) });
    }
    public void Link(AssetId from, AssetId to)
    {
        var target = _current.Assets.FirstOrDefault(a => a.Id == to) ?? throw new ProjectDataException("ASSET_NOT_FOUND", "Target missing.");
        Commit(_current with { Dependencies = _current.Dependencies.Add(new(from, to, target.Revision, "resource")) });
    }
    public void Undo() { if (_history.Count > 0) _current = _history.Pop(); }
}
