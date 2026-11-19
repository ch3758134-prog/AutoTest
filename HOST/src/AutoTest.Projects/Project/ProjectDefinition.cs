using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Projects.Project;

public sealed record ProjectDefinition(ProjectId Id, int SchemaVersion, int Revision,
    string Name, ImmutableArray<Resources.AssetEntry> Assets, ImmutableArray<Resources.AssetDependency> Dependencies)
{
    public const int CurrentSchema = 1;
    public static ProjectDefinition Create(string name) => new ProjectDefinition(ProjectId.New(), CurrentSchema, 1,
        name, ImmutableArray<Resources.AssetEntry>.Empty, ImmutableArray<Resources.AssetDependency>.Empty).Validate();
    public ProjectDefinition Validate()
    {
        if (Id.Value == Guid.Empty || SchemaVersion != CurrentSchema || Revision < 1 || string.IsNullOrWhiteSpace(Name))
            throw new ProjectDataException("PROJECT_SCHEMA", "Invalid project identity, name, revision or schema version.");
        if (Assets.IsDefault || Dependencies.IsDefault || Assets.Any(a => a is null))
            throw new ProjectDataException("PROJECT_SCHEMA", "Project collections are absent or malformed.");
        foreach (var asset in Assets) asset.Validate();
        if (Assets.GroupBy(a => a.Id).Any(g => g.Count() != 1))
            throw new ProjectDataException("ASSET_DUPLICATE", "Duplicate AssetId in project.");
        Resources.ReferenceValidator.RejectDangling(this);
        return this;
    }
    public ProjectDefinition WithRevision() => this with { Revision = checked(Revision + 1) };
}
public sealed class ProjectDataException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
