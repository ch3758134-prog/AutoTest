using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Station-local index of explicitly installed ECU model projects, not an
/// external flash configuration locator. Every resolution reopens the manifest
/// and verifies its declared model and stable ProjectId. No execution rights.
/// </summary>
public sealed class StationModelCatalog
{
    public sealed record Entry(string ModelCode, string ProjectId, string ManifestPath);
    private sealed record CatalogFile(int Version, List<Entry> Entries);
    public sealed record ResolveResult(string Code, Entry? Entry,
        ProjectDefinition? Definition, string? ManifestSha256, string Explanation)
    {
        public bool Matched => Code == "STATION_MODEL_RESOLVED";
    }

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public StationModelCatalog() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "installed-ecu-models.json")) { }

    public StationModelCatalog(string path) => _path = Path.GetFullPath(path);

    private async Task<CatalogFile> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new(1, new());
        var bytes = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
        if (bytes.Length > 1024 * 1024)
            throw new InvalidDataException("已安装 ECU 型号索引损坏或超过大小限制。");
        var document = JsonSerializer.Deserialize<CatalogFile>(bytes, Json);
        if (document is null || document.Version != 1 || document.Entries is null ||
            document.Entries.Any(e => e is null ||
                string.IsNullOrWhiteSpace(e.ModelCode) ||
                string.IsNullOrWhiteSpace(e.ProjectId) ||
                string.IsNullOrWhiteSpace(e.ManifestPath)))
            throw new InvalidDataException("已安装 ECU 型号索引格式无效。");
        return document;
    }

    private async Task SaveAsync(CatalogFile document, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(folder);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary,
                JsonSerializer.SerializeToUtf8Bytes(document, Json), ct).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Explicit install: do not silently replace an earlier project of the
    /// same model. A broken/uninstalled old entry must be removed deliberately.
    /// </summary>
    public async Task RegisterAsync(OpenProjectContext opened, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(opened.Definition.EcuModelCode))
            throw new InvalidDataException("请先配置当前工程的正式 ECU 型号代码。");
        var path = Path.GetFullPath(opened.ManifestPath);
        var live = await ProjectRepository.OpenDefinitionAsync(path, ct).ConfigureAwait(false);
        if (live.Id != opened.Definition.Id ||
            !string.Equals(live.EcuModelCode, opened.Definition.EcuModelCode, StringComparison.Ordinal))
            throw new InvalidDataException("工程型号或工程身份在登记时改变，请重新打开工程。");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            var duplicates = current.Entries.Where(x =>
                x.ModelCode == live.EcuModelCode &&
                !(x.ProjectId == live.Id.Value.ToString("D") &&
                  string.Equals(x.ManifestPath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (duplicates.Length > 0)
                throw new InvalidDataException(
                    "同一型号已登记另一个 ECU 工程。必须先明确移除旧映射，禁止自动覆盖或按工程名猜测。");
            current.Entries.RemoveAll(x => x.ProjectId == live.Id.Value.ToString("D") ||
                string.Equals(x.ManifestPath, path, StringComparison.OrdinalIgnoreCase));
            current.Entries.Add(new(live.EcuModelCode!, live.Id.Value.ToString("D"), path));
            await SaveAsync(current, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task UnregisterAsync(string modelCode, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            current.Entries.RemoveAll(x => x.ModelCode == modelCode);
            await SaveAsync(current, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<ResolveResult> ResolveAsync(string modelCode, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        CatalogFile catalog;
        try { catalog = await LoadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }

        var matching = catalog.Entries.Where(x => x.ModelCode == modelCode).ToArray();
        if (matching.Length == 0)
            return new("STATION_MODEL_NOT_REGISTERED", null, null, null,
                "型号未在当前工位登记，禁止猜测或自动打开其他 ECU 工程。");
        if (matching.Length != 1)
            return new("STATION_MODEL_AMBIGUOUS", null, null, null,
                "同型号存在多个工位登记，必须先解决映射冲突。");

        var selected = matching[0];
        if (!File.Exists(selected.ManifestPath))
            return new("STATION_PROJECT_NOT_FOUND", selected, null, null,
                "登记的型号工程不在原路径；没有执行任何工位动作。");
        try
        {
            var definition = await ProjectRepository.OpenDefinitionAsync(
                selected.ManifestPath, ct).ConfigureAwait(false);
            if (definition.Id.Value.ToString("D") != selected.ProjectId ||
                definition.EcuModelCode != modelCode)
                return new("STATION_MODEL_CHANGED", selected, definition, null,
                    "登记工程已改变正式型号或身份，必须重新登记。");
            var bytes = await File.ReadAllBytesAsync(selected.ManifestPath, ct)
                .ConfigureAwait(false);
            var currentHash = Convert.ToHexString(SHA256.HashData(bytes));
            return new("STATION_MODEL_RESOLVED", selected, definition, currentHash,
                "已找到唯一对应型号工程；资源 SHA、原生 Flash Job 与实体工位仍须预检。");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      ProjectDataException or UnauthorizedAccessException or
                                      JsonException or ArgumentException)
        {
            return new("STATION_PROJECT_INVALID", selected, null, null,
                "型号工程不能安全打开：" + error.Message);
        }
    }

    public async Task<IReadOnlyList<Entry>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return (await LoadAsync(ct).ConfigureAwait(false)).Entries.ToArray(); }
        finally { _gate.Release(); }
    }
}
