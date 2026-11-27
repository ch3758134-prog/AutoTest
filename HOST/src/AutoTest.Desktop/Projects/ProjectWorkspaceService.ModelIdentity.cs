using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

public sealed partial class ProjectWorkspaceService
{
    /// <summary>One atomic project revision for model identity and barcode expectation.
    /// The expectation is portable with the project and never depends on COM settings.</summary>
    public async Task SaveBarcodeExpectationAsync(Guid projectId, string manifestPath,
        string? expectedRuleJson, ProjectBarcodeExpectation expectation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("请先打开 ECU 工程。");
            if (current.Definition.Id.Value != projectId ||
                !string.Equals(current.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("工程已切换，禁止将扫码配置保存到另一工程。");
            var currentRule = current.Definition.BarcodeExpectation is { } rule
                ? System.Text.Json.JsonSerializer.Serialize(rule) : null;
            if (currentRule != expectedRuleJson)
                throw new InvalidOperationException("工程扫码期望已被其他编辑器修改，请重新打开后编辑。");
            var model = expectation.Fields.FirstOrDefault(x =>
                x.Name == ProjectBarcodeExpectation.Model)?.Expected;
            var candidate = (current.Definition with
            {
                EcuModelCode = model,
                BarcodeExpectation = expectation
            }).WithRevision().Validate();
            await EnsureManifestUnchangedAsync(current, ct);
            await ProjectRepository.SaveDefinitionAsync(candidate, current.ManifestPath, ct);
            var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct);
            var hash = await HashFileAsync(current.ManifestPath, ct);
            Current = new OpenProjectContext(verified, current.ManifestPath, hash);
            NotifyCurrentChanged();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Updates the declared ECU model without conflating it with the display name.
    /// No silent migration: old projects remain unbound until explicitly configured.
    /// Uses the same revision/manifest compare-and-swap gate as other project edits.
    /// </summary>
    public async Task SetEcuModelCodeAsync(string modelCode, CancellationToken ct = default)
    {
        modelCode = modelCode?.Trim() ?? "";
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("请先打开 ECU 工程。");
            var candidate = (current.Definition with { EcuModelCode = modelCode })
                .WithRevision().Validate();
            await EnsureManifestUnchangedAsync(current, ct);
            await ProjectRepository.SaveDefinitionAsync(candidate, current.ManifestPath, ct)
                ;
            var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                ;
            var hash = await HashFileAsync(current.ManifestPath, ct);
            Current = new OpenProjectContext(verified, current.ManifestPath, hash);
            NotifyCurrentChanged();
        }
        finally { _writeLock.Release(); }
    }
}
