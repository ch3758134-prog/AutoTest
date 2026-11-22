using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

public sealed partial class ProjectWorkspaceService
{
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
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _writeLock.Release(); }
    }
}
