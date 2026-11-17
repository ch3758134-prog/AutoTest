namespace AutoTest.UiPreview;

// A view-facing port. The preview has no Project/TEST references and stays runnable offline.
public sealed record UiTestAsset(string Id, string Name);
public sealed record UiProjectAsset(string Id, string Name, string Kind)
{
    public string DisplayKind => Kind switch
    {
        "dbc" => "DBC",
        "controller" => "控制器",
        "test-project" => "测试项目",
        "wave" => "波形",
        "firmware" => "固件",
        _ => "资源"
    };
}
public sealed record UiProjectSnapshot(string Name, string ManifestPath,
    IReadOnlyList<UiTestAsset> TestPrograms, IReadOnlyList<UiProjectAsset> Resources);
public sealed record UiTestSnapshot(string AssetId, string Name, IReadOnlyList<DemoPhase> Phases);

public interface IEditorWorkspaceGateway
{
    IReadOnlyList<PaletteKind> StepTypes { get; }
    DemoStep CreateStep(string type);
    Task<UiProjectSnapshot> OpenProjectAsync(string manifestPath, CancellationToken ct = default);
    Task<UiProjectSnapshot> CreateProjectAsync(string name, string parentFolder, CancellationToken ct = default);
    Task<UiProjectSnapshot> CreateTestAsync(string name, CancellationToken ct = default);
    Task<UiProjectSnapshot> RenameTestAsync(string assetId, string name, CancellationToken ct = default);
    Task<UiProjectSnapshot> ImportDbcAsync(string filePath, CancellationToken ct = default);
    Task<UiTestSnapshot> OpenTestAsync(string assetId, CancellationToken ct = default);
    Task<UiTestSnapshot> SaveTestAsync(string assetId, IReadOnlyList<DemoPhase> phases, CancellationToken ct = default);
    IReadOnlyList<string> Validate(string assetId, IReadOnlyList<DemoPhase> phases);
}
