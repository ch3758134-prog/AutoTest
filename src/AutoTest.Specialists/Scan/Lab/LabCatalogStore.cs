using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Specialists.Scan.Lab;

public sealed class LabCatalogStore
{
    private readonly string _path;
    private readonly string _draftPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LabCatalogStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A local catalog path is required", nameof(path));
        _path = path + ".published.json";
        _draftPath = path + ".draft.json";
    }

    public Task<LabCatalog?> LoadAsync(CancellationToken ct = default) => ReadAsync(_path, ct);
    public Task<LabCatalog?> LoadDraftAsync(CancellationToken ct = default) => ReadAsync(_draftPath, ct);

    private async Task<LabCatalog?> ReadAsync(string path, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return null;
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (await JsonSerializer.DeserializeAsync<LabCatalog>(input, JsonOptions, ct)
                .ConfigureAwait(false))?.Copy();
        }
        finally { _mutex.Release(); }
    }

    // Saving an invalid draft is allowed. It MUST NOT replace the published snapshot in a run.
    public Task<LabCatalog> SaveDraftAsync(LabCatalog draft, CancellationToken ct = default) =>
        WriteNewRevisionAsync(draft with { Published = false }, _draftPath, ct);

    public async Task<LabCatalog> PublishAsync(LabCatalog draft, CancellationToken ct = default)
    {
        var errors = LabCatalogValidator.Validate(draft, forPublication: true);
        if (errors.Count != 0) throw new InvalidOperationException("PUBLICATION_BLOCKED:" +
            string.Join("|", errors));
        return await WriteNewRevisionAsync(draft with { Published = true }, _path, ct).ConfigureAwait(false);
    }

    private async Task<LabCatalog> WriteNewRevisionAsync(LabCatalog state, string path, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var changed = state.Copy() with { Revision = Guid.NewGuid().ToString("N") };
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(output, changed, JsonOptions, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
            return changed;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _mutex.Release();
        }
    }
}

public static class LabDemoCatalog
{
    // Deliberately MOCK ONLY; never inferred from a connected controller or a customer serial format.
    public static LabCatalog Create() => new("demo-mock-v1", true,
        new[] { new LabIdentityRule("MOCK-SERIAL", @"(?<serial>M[0-9]{6})", "serial", true) },
        new[] { new LabRecipe("MOCK-RECIPE", "mock-v1", "", "",
            new LabArtifact("mock://test-program", "mock-v1", "MOCK_ONLY", LabOrigin.Mock, true),
            new LabArtifact("mock://station", "mock-v1", "MOCK_ONLY", LabOrigin.Mock, true),
            null, false, true) },
        new[] { new LabRuleSample("M000001", "MOCK-SERIAL", "MOCK-RECIPE") });
}

