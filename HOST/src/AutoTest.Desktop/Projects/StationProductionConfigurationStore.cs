using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// ONE station-owned production identity/publication configuration, with
/// immutable public release. Historical project barcode expectations and
/// scanner-side recipe mappings are never consulted for formal acceptance.
/// </summary>
public static class StationProductionConfigurationStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "production-preparation");
    public static string DraftPath => Path.Combine(Root, "station-draft.json");
    public static string PublishedPath => Path.Combine(Root, "station-published.json");

    public static async Task<StationProductionConfiguration> LoadDraftAsync(
        CancellationToken ct = default)
    {
        if (!File.Exists(DraftPath)) return StationProductionConfiguration.New();
        var bytes = await File.ReadAllBytesAsync(DraftPath, ct).ConfigureAwait(false);
        if (bytes.Length is < 1 or > 24_000_000)
            throw new InvalidDataException("生产配置草稿文件缺失或超出限制。");
        var draft = JsonSerializer.Deserialize<StationProductionConfiguration>(bytes, Json)
            ?? throw new InvalidDataException("工位配置为空。");
        draft.ValidateDraft(); // No legacy JSON migration or silent fallback.
        return draft;
    }

    public static async Task SaveDraftAsync(StationProductionConfiguration draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.ValidateDraft();
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicAsync(DraftPath,
                JsonSerializer.SerializeToUtf8Bytes(draft, Json), ct).ConfigureAwait(false);
        }
        finally { WriteGate.Release(); }
    }

    /// <summary>
    /// Recover an unreadable station draft without migrating or interpreting
    /// older data. Preserve its exact bytes before replacing the draft.
    /// The active production publication is intentionally untouched.
    /// </summary>
    public static async Task ResetUnreadableDraftAsync(CancellationToken ct = default)
    {
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(DraftPath))
            {
                // Read again under the write gate. A valid draft may have been
                // repaired by another operation since the window first opened.
                try
                {
                    _ = await LoadDraftAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "工位设置已恢复，请重新打开窗口查看。");
                }
                catch (Exception error) when (error is InvalidDataException or
                    JsonException or NotSupportedException)
                {
                    // The original is retained as an opaque file, not migrated.
                }

                var bytes = await File.ReadAllBytesAsync(DraftPath, ct)
                    .ConfigureAwait(false);
                var backup = Path.Combine(Root,
                    "station-draft-backup-" +
                    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
                    Guid.NewGuid().ToString("N") + ".json");
                await AtomicAsync(backup, bytes, ct).ConfigureAwait(false);
                var backedUp = await File.ReadAllBytesAsync(backup, ct)
                    .ConfigureAwait(false);
                if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(
                        SHA256.HashData(backedUp)))
                    throw new IOException("工位配置备份校验失败，原设置保持不变。");
            }
            // Atomic replacement only after a verified backup. Neither the
            // published configuration nor the selected project is changed.
            var fresh = StationProductionConfiguration.New();
            fresh.ValidateDraft();
            await AtomicAsync(DraftPath,
                JsonSerializer.SerializeToUtf8Bytes(fresh, Json), ct)
                .ConfigureAwait(false);
        }
        finally { WriteGate.Release(); }
    }

    public static string ConfigurationSha(StationProductionConfiguration document) =>
        Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(document, Json)));

    public static async Task<PublishedStationProductionConfiguration?> LoadPublishedAsync(
        CancellationToken ct = default)
    {
        if (!File.Exists(PublishedPath)) return null;
        var bytes = await File.ReadAllBytesAsync(PublishedPath, ct).ConfigureAwait(false);
        if (bytes.Length is < 1 or > 4_000_000)
            throw new InvalidDataException("工位正式发布配置文件长度异常。");
        var published = JsonSerializer.Deserialize<PublishedStationProductionConfiguration>(
            bytes, Json) ?? throw new InvalidDataException("工位正式发布配置为空。");
        if (published.SchemaVersion != StationProductionConfiguration.CurrentSchema || published.PublicationId == Guid.Empty ||
            published.Configuration is null ||
            published.Projects is null || published.Projects.Count == 0)
            throw new InvalidDataException("已发布生产配置的版本或工程绑定无效。");
        published.Configuration.Validate();
        if (published.Projects.Count != published.Configuration.Products.Count ||
            published.Configuration.Products.Any(binding =>
                !published.Projects.Any(pin =>
                    pin.ProjectId == binding.ProjectId &&
                    string.Equals(pin.ProductKey, binding.ProductKey,
                        StringComparison.Ordinal) &&
                    string.Equals(pin.HardwareCode, binding.HardwareCode,
                        StringComparison.Ordinal) &&
                    string.Equals(pin.ManifestPath, binding.ManifestPath,
                        StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException(
                "已发布工程集合与工位配置绑定不一致，禁止扫码。");
        if (!string.Equals(ConfigurationSha(published.Configuration),
            published.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("已发布生产配置的 SHA256 不匹配，禁止扫码。");
        return published;
    }

    public static IReadOnlyList<string> InspectRules(
        StationProductionConfiguration config)
    {
        var issues = new List<string>();
        try { config.Validate(); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException)
        { return [e.Message]; }
        foreach (var rule in config.Rules.Where(x => x.Enabled))
        {
            // Older R1 drafts prefilled these exact artificial examples.
            // Do not let their mere existence satisfy a real station's
            // positive/negative verification contract.
            if (rule.Samples.Count == 2 &&
                rule.Samples.Any(x => x.Raw == "SN001234" && x.ShouldAccept &&
                    x.ExpectedSerial == "SN001234") &&
                rule.Samples.Any(x => x.Raw == "SN|BAD" && !x.ShouldAccept))
                issues.Add(rule.Name + "：仍使用旧版虚构的正反例；" +
                    "请用本工位真实扫码替换，错误样例可明确使用人工构造的拒绝输入。");
            // Default single-model stations take the entire ASCII barcode as
            // the unique serial. This is a deterministic parser contract, not
            // a claim that a physical scanner was tested. Publishing a saved
            // ECU project must NOT require a connected scanner or an invented
            // sample. Advanced rules and any explicitly entered samples still
            // require a valid positive/negative pair.
            var plainFixedSerial =
                config.Mode == StationProductionConfiguration.FixedStation &&
                config.Rules.Count(x => x.Enabled) == 1 &&
                rule.Format == ProductionBarcodeRule.Serial &&
                rule.Prefix.Length == 0 && rule.ExactLength == 0 &&
                rule.AllowedCharacters == @"[A-Za-z0-9_.-]+" &&
                rule.ModelField == 0 && rule.HardwareField == 0 &&
                rule.BatchField == 0;
            if (!(plainFixedSerial && rule.Samples.Count == 0) &&
                (!rule.Samples.Any(s => s.ShouldAccept) ||
                 !rule.Samples.Any(s => !s.ShouldAccept)))
                issues.Add(rule.Name + "：自定义编码规则发布前必须有正例和反例。");
            if (rule.Samples.Any(s => string.IsNullOrWhiteSpace(s.Raw)))
                issues.Add(rule.Name + "：编码校验样例不能留空，空字符串不得作为有效拒绝样例。");
            foreach (var sample in rule.Samples)
            {
                var match = rule.Parse(sample.Raw);
                if (match.Accepted != sample.ShouldAccept ||
                    match.Accepted &&
                    (!string.IsNullOrEmpty(sample.ExpectedSerial) &&
                     match.Serial != sample.ExpectedSerial ||
                     !string.IsNullOrEmpty(sample.ExpectedModel) &&
                     match.Model != sample.ExpectedModel))
                    issues.Add(rule.Name + "：样例不符合预期 [" + sample.Raw + "] " +
                        match.Code);
            }
        }
        foreach (var raw in config.Rules.SelectMany(x => x.Samples)
                     .Select(x => x.Raw).Distinct(StringComparer.Ordinal))
        {
            var matches = config.Rules.Where(x => x.Enabled)
                .Select(x => (x, parsed: x.Parse(raw)))
                .Where(x => x.parsed.Accepted).ToArray();
            if (matches.Length < 2) continue;
            var identities = matches.Select(x =>
                x.parsed.Serial + "\u001f" + x.parsed.Model + "\u001f" +
                x.parsed.Hardware).Distinct(StringComparer.Ordinal).ToArray();
            if (identities.Length > 1)
                issues.Add("样例同时命中不同身份的规则：" + raw + "；" +
                    string.Join("、", matches.Select(x => x.x.Name)));
        }
        return issues;
    }

    public static Task<ProductionPublishedProduct> InspectProjectAsync(
        ProductionProductBinding binding, CancellationToken ct = default) =>
        FreezeProjectAsync(binding, ct);

    private static async Task<ProductionPublishedProduct> FreezeProjectAsync(
        ProductionProductBinding binding, CancellationToken ct)
    {
        var manifest = Path.GetFullPath(binding.ManifestPath);
        if (!File.Exists(manifest))
            throw new FileNotFoundException("工程清单不存在：" + binding.ProjectId, manifest);
        var before = await File.ReadAllBytesAsync(manifest, ct).ConfigureAwait(false);
        var definition = await ProjectRepository.OpenDefinitionAsync(manifest, ct)
            .ConfigureAwait(false);
        var after = await File.ReadAllBytesAsync(manifest, ct).ConfigureAwait(false);
        if (!SHA256.HashData(before).AsSpan().SequenceEqual(SHA256.HashData(after)))
            throw new IOException("工程清单在冻结期间改变：" + binding.ProjectId);
        if (definition.Id.Value != binding.ProjectId)
            throw new InvalidDataException(
                "工程清单的 ProjectId 与工位绑定不一致，禁止按文件名替代。");
        var opened = new OpenProjectContext(definition, manifest,
            Convert.ToHexString(SHA256.HashData(after)));
        var test = await AutomaticTestResourceContract.ResolveAsync(opened, ct)
            .ConfigureAwait(false);
        test.RequireExecutable();
        var flash = await NativeFlashPublicationStore.LoadPublishedAsync(
            opened.RootDirectory, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("工程尚未冻结真实原生 FLASH 工艺：" + binding.ProjectId);
        var job = NativeFlashPublicationStore.FreezePublished(opened.RootDirectory, flash);
        var fixture = definition.Assets.Where(x =>
            x.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
        if (fixture.Length > 1)
            throw new InvalidDataException("工程有多份工位工艺，无法唯一发布。");
        if (fixture.Length == 1)
        {
            var bytes = await AutomaticTestResourceContract.ReadPinnedAsync(
                opened, fixture[0], ct).ConfigureAwait(false);
            var procedure = (InspectionStationProcedureDocument)
                ProjectConfigurationCodec.Decode(fixture[0].Kind, bytes);
            procedure.Validate();
            // The STANDARD production clamp/stop sequence is station-owned.
            // Old project BeforeStart/AfterFinish clamp outputs would duplicate
            // it and could unexpectedly energize the actuator out of sequence.
            // Keep explicit IGN and non-actuating waits; fail closed for clamps.
            var conflicts = procedure.BeforeStart.Concat(procedure.AfterFinish)
                .Where(x => x.Kind.StartsWith("Clamp", StringComparison.Ordinal))
                .Select(x => x.Kind).Distinct().ToArray();
            if (conflicts.Length > 0)
                throw new InvalidDataException(
                    "旧工程工位动作包含重复夹具控制（" +
                    string.Join("、", conflicts) +
                    "）；请在工程设置中移除旧夹紧/释放动作，改由生产准备标准夹具配置管理。");
        }
        return new ProductionPublishedProduct(binding.ProductKey, binding.HardwareCode,
            manifest, definition.Id.Value, opened.ManifestSha256,
            test.TestProjectAsset.Id.Value, test.TestProjectAsset.Sha256,
            flash.ProfileSha256, job.Fingerprint, job.FirmwareSha256,
            flash.Profile.EcuCanKbps,
            fixture.SingleOrDefault()?.Sha256 ?? StationDeploymentStore.NoProcedureSha256,
            test.Cases.Select(x => x.Entry.DisplayName).ToArray());
    }

    public static async Task<PublishedStationProductionConfiguration> PublishAsync(
        StationProductionConfiguration config, CancellationToken ct = default)
    {
        config.Validate();
        var issues = InspectRules(config);
        if (issues.Count != 0)
            throw new InvalidDataException("编码形制校验未通过：" + string.Join("；", issues.Take(12)));
        var pinned = new List<ProductionPublishedProduct>();
        foreach (var binding in config.Products)
            pinned.Add(await FreezeProjectAsync(binding, ct).ConfigureAwait(false));
        var publication = new PublishedStationProductionConfiguration(StationProductionConfiguration.CurrentSchema,
            ConfigurationSha(config), Guid.NewGuid(), DateTimeOffset.UtcNow,
            config, pinned.ToArray());
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Publishing a config cannot silently overwrite a newer draft:
            // its displayed hash must still match the exact saved bytes.
            var saved = await LoadDraftAsync(ct).ConfigureAwait(false);
            if (ConfigurationSha(saved) != publication.ConfigurationSha256)
                throw new IOException("配置草稿在资源发布检查期间已变化，请先保存并重新发布。");
            await AtomicAsync(PublishedPath,
                JsonSerializer.SerializeToUtf8Bytes(publication, Json), ct)
                .ConfigureAwait(false);
        }
        finally { WriteGate.Release(); }
        return publication;
    }

    public static async Task<ProductionIdentityDecision> PreviewDraftAsync(
        StationProductionConfiguration config, string raw,
        CancellationToken ct = default)
    {
        config.Validate();
        var pins = new List<ProductionPublishedProduct>();
        foreach (var product in config.Products)
            pins.Add(await FreezeProjectAsync(product, ct).ConfigureAwait(false));
        var ephemeral = new PublishedStationProductionConfiguration(
            StationProductionConfiguration.CurrentSchema,
            ConfigurationSha(config), Guid.NewGuid(), DateTimeOffset.UtcNow,
            config, pins.ToArray());
        return ProductionIdentityResolver.Resolve(ephemeral, raw);
    }

    public static async Task<ProductionPublishedProduct> VerifyProductAsync(
        ProductionPublishedProduct pin, CancellationToken ct = default)
    {
        var current = await FreezeProjectAsync(
            new(pin.ProductKey, pin.HardwareCode, pin.ManifestPath, pin.ProjectId), ct)
            .ConfigureAwait(false);
        if (current.ProjectId != pin.ProjectId ||
            current.TestProjectId != pin.TestProjectId ||
            current.ManifestSha256 != pin.ManifestSha256 ||
            current.TestProjectSha256 != pin.TestProjectSha256 ||
            current.NativeFlashSha256 != pin.NativeFlashSha256 ||
            current.FlashJobFingerprint != pin.FlashJobFingerprint ||
            current.FirmwareSha256 != pin.FirmwareSha256 ||
            current.EcuCanKbps != pin.EcuCanKbps ||
            current.FixtureSha256 != pin.FixtureSha256 ||
            !current.OrderedCaseNames.SequenceEqual(pin.OrderedCaseNames))
            throw new InvalidDataException("发布 ECU 工程的 FLASH、TEST、工艺或资源版本已经变化。");
        return current;
    }

    private static async Task AtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>
/// Reserved identity lookup seam: CSV-backed now; remote MES may implement
/// the same interface later without changing barcode or TEST execution.
/// </summary>
public interface IProductionIdentityRegistry
{
    ProductionIdentityRow? Lookup(string uniqueSerial);
}

public sealed class PublishedLocalIdentityRegistry(
    IReadOnlyList<ProductionIdentityRow> rows) : IProductionIdentityRegistry
{
    private readonly IReadOnlyDictionary<string, ProductionIdentityRow> _rows =
        rows.ToDictionary(x => x.Serial, StringComparer.Ordinal);
    public ProductionIdentityRow? Lookup(string uniqueSerial) =>
        _rows.TryGetValue(uniqueSerial, out var row) &&
        (row.ExpiresUtc is null || row.ExpiresUtc > DateTimeOffset.UtcNow)
            ? row : null;
}

/// <summary>No physical I/O, no partial deployment switching, no inferred model.
/// Runtime uses only exact published product pins, then independently verifies
/// this station's active deployment before accepting a production scan.</summary>
public static class ProductionIdentityResolver
{
    public static ProductionIdentityDecision Resolve(
        PublishedStationProductionConfiguration release, string raw)
    {
        var config = release.Configuration;
        var matches = config.Rules.Where(x => x.Enabled)
            .Select(x => (Rule: x, Fields: x.Parse(raw)))
            .Where(x => x.Fields.Accepted).OrderBy(x => x.Rule.RuleId,
                StringComparer.Ordinal).ToArray();
        ProductionIdentityDecision Reject(string code, string message) =>
            new(code, message, raw, "", 0, "", "", "", "", "", null);
        if (matches.Length == 0)
        {
            // Prefer concrete rule failures so the engineer can fix a prefix,
            // checksum, length, or forbidden character without guessing.
            var diagnostics = config.Rules.Where(x => x.Enabled)
                .Take(4).Select(x => x.Name + "：" + x.Parse(raw).Code)
                .ToArray();
            return Reject("NO_BARCODE_RULE_MATCHED",
                "没有已发布编码形制匹配该编码。 " +
                string.Join("；", diagnostics));
        }
        var resolutions = new List<ProductionIdentityDecision>();
        var registry = new PublishedLocalIdentityRegistry(config.IdentityRows);
        foreach (var matched in matches)
        {
            var f = matched.Fields;
            string key = "", hardware = "", basis;
            ProductionPublishedProduct? project;
            if (config.Mode == StationProductionConfiguration.FixedStation)
            {
                project = release.Projects.SingleOrDefault(x =>
                    x.ProjectId == config.FixedProjectId);
                basis = "FIXED_PROJECT_ID";
            }
            else
            {
                if (config.Mode == StationProductionConfiguration.BarcodeModel)
                {
                    key = f.Model; // Parsed product segment, never ProjectId.
                    hardware = f.Hardware;
                    basis = "BARCODE_PRODUCT_KEY";
                    if (string.IsNullOrWhiteSpace(key))
                        return Reject("PRODUCT_KEY_MISSING",
                            "条码没有可信产品识别字段，不能自动猜工程。");
                }
                else
                {
                    var row = registry.Lookup(f.Serial);
                    if (row is null)
                        return Reject("IDENTITY_REGISTRY_NOT_FOUND",
                            "编号在受控工程归属表中不存在或已过期。");
                    key = row.ProductKey;
                    hardware = row.HardwareCode;
                    basis = "REGISTRY_PRODUCT_KEY";
                }
                if ((!string.IsNullOrEmpty(f.Model) && f.Model != key) ||
                    (!string.IsNullOrEmpty(f.Hardware) && f.Hardware != hardware))
                    return Reject("PRODUCT_KEY_CONFLICT",
                        "条码产品字段与受控工程归属冲突。");
                var candidates = release.Projects.Where(x =>
                    x.ProductKey == key && x.HardwareCode == hardware).ToArray();
                project = candidates.Length == 1 ? candidates[0] : null;
            }
            if (project is null)
                return Reject("PROJECT_MAPPING_NOT_UNIQUE",
                    "扫码没有匹配到唯一且已冻结的工程。");
            resolutions.Add(new("PRODUCTION_IDENTITY_RESOLVED",
                "ECU 编号 → 唯一已冻结工程；尚未取得实体启动许可。",
                raw, matched.Rule.RuleId, matched.Rule.Revision, f.Serial,
                key, hardware, f.Batch, basis, project));
        }
        if (resolutions.Select(x => x.Serial + "\u001f" + x.ProductKey + "\u001f" +
                x.Hardware + "\u001f" + x.Batch + "\u001f" + x.Project!.ProjectId)
            .Distinct(StringComparer.Ordinal).Count() != 1)
            return Reject("BARCODE_RULE_CONFLICT",
                "多条已发布编码规则对同一原始条码得到不同身份/产品，拒绝按顺序猜选。");
        return resolutions[0];
    }

    public static async Task<ProductionIdentityDecision> ResolvePublishedAsync(
        string raw, CancellationToken ct = default) =>
        (await ResolvePublishedSnapshotAsync(raw, ct).ConfigureAwait(false)).Decision;

    public static async Task<(PublishedStationProductionConfiguration? Publication,
        ProductionIdentityDecision Decision)> ResolvePublishedSnapshotAsync(
        string raw, CancellationToken ct = default)
    {
        var published = await StationProductionConfigurationStore.LoadPublishedAsync(ct)
            .ConfigureAwait(false);
        if (published is null)
            return (null, new("STATION_PUBLICATION_MISSING",
                "尚未发布生产准备配置；旧扫码配方不能替代。",
                raw, "", 0, "", "", "", "", "", null));
        var selected = Resolve(published, raw);
        if (!selected.Accepted) return (published, selected);
        await StationProductionConfigurationStore.VerifyProductAsync(selected.Project!, ct)
            .ConfigureAwait(false);
        return (published, selected);
    }
}
