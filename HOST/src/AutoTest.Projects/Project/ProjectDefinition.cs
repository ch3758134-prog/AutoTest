using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Projects.Project;

public sealed record ProjectDefinition(ProjectId Id, int SchemaVersion, int Revision,
    string Name, ImmutableArray<Resources.AssetEntry> Assets, ImmutableArray<Resources.AssetDependency> Dependencies,
    string? EcuModelCode = null, ProjectBarcodeExpectation? BarcodeExpectation = null)
{
    public const int CurrentSchema = 1;
    public static ProjectDefinition Create(string name) => new ProjectDefinition(ProjectId.New(), CurrentSchema, 1,
        name, ImmutableArray<Resources.AssetEntry>.Empty, ImmutableArray<Resources.AssetDependency>.Empty).Validate();
    public ProjectDefinition Validate()
    {
        if (Id.Value == Guid.Empty || SchemaVersion != CurrentSchema || Revision < 1 || string.IsNullOrWhiteSpace(Name))
            throw new ProjectDataException("PROJECT_SCHEMA", "Invalid project identity, name, revision or schema version.");
        if (EcuModelCode is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(EcuModelCode,
                @"^[A-Z0-9]{2,16}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
            throw new ProjectDataException("PROJECT_MODEL_CODE",
                "工程 ECU 型号代码仅允许 2～16 位大写字母和数字。");
        BarcodeExpectation?.Validate(EcuModelCode);
        if (Assets.IsDefault || Dependencies.IsDefault || Assets.Any(a => a is null))
            throw new ProjectDataException("PROJECT_SCHEMA", "Project collections are absent or malformed.");
        foreach (var asset in Assets) asset.Validate();
        if (Assets.GroupBy(a => a.Id).Any(g => g.Count() != 1))
            throw new ProjectDataException("ASSET_DUPLICATE", "Duplicate AssetId in project.");
        // Invalid project shapes are not something an operator should have
        // to discover during production preparation. Enforce at every edit.
        if (Assets.Count(a => a.Kind == Resources.ProjectResourceKinds.TestProject) > 1)
            throw new ProjectDataException("TEST_PROJECT_UNIQUE",
                "一个 ECU 工程只能包含一个有序 TEST 项目。");
        if (Assets.Count(a => a.Kind == Resources.ProjectResourceKinds.Dbc) > 1)
            throw new ProjectDataException("DBC_UNIQUE",
                "一个 ECU 工程只能绑定一份有效 DBC。");
        if (Assets.Any(a => a.Kind is "test-configuration" or "bench-configuration"))
            throw new ProjectDataException("RETIRED_CONFIGURATION",
                "不支持旧式 TCF/TBC 资源；工程直接绑定测试、DBC 与波形。");
        Resources.ReferenceValidator.RejectDangling(this);
        return this;
    }
    public ProjectDefinition WithRevision() => this with { Revision = checked(Revision + 1) };
}
public sealed class ProjectDataException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
