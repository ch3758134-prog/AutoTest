using System.IO;
using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

public sealed record FrozenProductionCase(
    Guid EntryId, Guid TestAssetId, string DisplayName, int Revision, string Sha256);
public sealed record FrozenStationProcedure(
    ImmutableArray<InspectionStationAction> BeforeStart,
    ImmutableArray<InspectionStationAction> AfterFinish);

/// <summary>
/// Accepted REAL scanner observation fixed to its exact interpretation-rule
/// JSON and checksum. This is a pre-run observation, NOT a claimed formal
/// RunRecord or DUT PASS. The future production RECORD adapter must link it
/// to its verified RunId before claiming end-to-end traceability.
/// </summary>
public sealed record FrozenScanIdentity(
    Guid ObservationId, DateTimeOffset CapturedAtUtc,
    string ScannerPort, string RawBarcode, string RuleJson,
    string RuleSha256, string ModelCode, string DutSerial,
    string? HardwareCode, IReadOnlyDictionary<string, string> MappedFields)
{
    public string PublishedConfigurationSha256 { get; init; } = "";
    public Guid PublicationId { get; init; }
    public string BarcodeRuleId { get; init; } = "";
    public int BarcodeRuleRevision { get; init; }
    public string ModelDecisionBasis { get; init; } = "";
    public string StationSlotId { get; init; } = "";
    public string ScannerPnpInstanceId { get; init; } = "";
}

public sealed record FrozenProductionPreparation(
    string ModelCode, string DutSerial, Guid ProjectId, string ManifestPath,
    string ManifestSha256, Guid TestProjectAssetId, string TestProjectSha256,
    IReadOnlyList<FrozenProductionCase> OrderedEnabledCases,
    string NativeProfileSha256, string JobFingerprint, string FirmwareSha256,
    string SecuritySha256, int EcuCanKbps, string PreparationFingerprint,
    CanToolFlashJob Job)
{
    public Guid FixtureAssetId { get; init; }
    public string FixtureContentSha256 { get; init; } = "";
    public FrozenStationProcedure? Fixture { get; init; }
    public FrozenScanIdentity? AcceptedScannerObservation { get; init; }
};

public sealed record ProductionPreparationDecision(
    string Code, FrozenProductionPreparation? Preparation, string Explanation)
{
    public bool ConfigurationReady => Code == "PRODUCTION_CONFIG_PREPARED" &&
                                      Preparation is not null;
}

/// <summary>
/// Stage A: immutable read-only preparation. No scanner match, project filename,
/// output ACK, or TEST tree can authorize the physical FLASH operation.
/// The equipment readiness / START edge / production consent / evidence are
/// intentionally separate gates, NOT mocks implemented in this service.
/// </summary>
public static class ProductionPreparationService
{
    public static async Task<ProductionPreparationDecision> PrepareAsync(
        OpenProjectContext project, string scannedModelCode, string dutSerial,
        CancellationToken ct = default, bool requireProductionDeployment = true)
    {
        try
        {
            return new("PRODUCTION_CONFIG_PREPARED",
                await FreezeAsync(project, scannedModelCode, dutSerial, ct,
                    requireProductionDeployment)
                    .ConfigureAwait(false),
                "型号、工程资源、当前原生烧录配置、固件和正式自动测试用例顺序已离线核对。" +
                "实体夹具状态、START、新设备会话及运行授权仍未取得，不允许执行。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or ArgumentException or
                                      FormatException or InvalidDataException or
                                      NotSupportedException or ProjectDataException or
                                      UnauthorizedAccessException or JsonException)
        {
            return new("PRODUCTION_PREFLIGHT_REJECTED", null,
                "生产配置预检未通过：" + error.Message);
        }
    }

    private static async Task<FrozenProductionPreparation> FreezeAsync(
        OpenProjectContext opened, string modelCode, string dutSerial,
        CancellationToken ct, bool requireProductionDeployment)
    {
        if (string.IsNullOrWhiteSpace(modelCode) || string.IsNullOrWhiteSpace(dutSerial))
            throw new InvalidDataException("缺少本次实体型号或唯一编号。");
        if (dutSerial.Length > 128 || dutSerial.Any(char.IsControl))
            throw new InvalidDataException("外部唯一编号长度或控制字符无效。");
        var actual = await ProjectRepository.OpenDefinitionAsync(
            opened.ManifestPath, ct).ConfigureAwait(false);
        if (actual.Id != opened.Definition.Id ||
            !string.Equals(actual.EcuModelCode, modelCode, StringComparison.Ordinal))
            throw new InvalidDataException("扫描型号未匹配当前工程的正式型号字段。");
        var manifestBytes = await File.ReadAllBytesAsync(
            opened.ManifestPath, ct).ConfigureAwait(false);
        var manifestSha = Convert.ToHexString(SHA256.HashData(manifestBytes));
        if (actual.Revision != opened.Definition.Revision ||
            !manifestSha.Equals(opened.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程已发生变更，必须重新打开工程及扫码。");

        // The production manifest is the frozen authority. Never reconcile
        // modified files into a new in-memory SHA/Revision: that would hide
        // external TEST/DBC/wave asset substitution without a saved project
        // revision. ReadPinnedAsync must reject every changed required asset.
        opened = opened with { Definition = actual };
        StationDeploymentStore.Verified? deployed = null;
        if (requireProductionDeployment)
        {
            deployed = await StationDeploymentStore.RestoreAsync(ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    "本工位尚未确认正式生产工程；请先在开发与调试中核对并确认。");
            if (!string.Equals(deployed.Project.ManifestPath, opened.ManifestPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(deployed.Project.ManifestSha256, manifestSha,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "扫码冻结时工程版本与已确认的工位工程不符；禁止按草稿执行。");
        }

        // Only required production resources may authorize execution;
        // old inactive draft assets cannot widen its real dependency set.
        // The operator list and the physical preparation MUST resolve the
        // identical project entry IDs, TEST programs, DBC and waveform pins.
        // A failure never becomes a silent skipped case or a physical PASS.
        var resolved = await AutomaticTestResourceContract.ResolveAsync(opened, ct)
            .ConfigureAwait(false);
        resolved.RequireExecutable();
        var testAsset = resolved.TestProjectAsset;
        var frozenCases = resolved.Cases.Select(item =>
            new FrozenProductionCase(item.Entry.ItemId, item.Asset.Id.Value,
                item.Entry.DisplayName, item.Asset.Revision, item.ContentSha256)).ToList();
        // Freeze this model's explicit station procedure, never an implicit
        // clamp/IGN operation hidden in a UI or duplicated TEST script.
        var fixtureAssets = actual.Assets.Where(x =>
            x.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
        if (fixtureAssets.Length > 1)
            throw new InvalidDataException("工程存在多个工位动作定义，不能猜选其一。");
        var fixtureAsset = fixtureAssets.SingleOrDefault();
        InspectionStationProcedureDocument? fixture = null;
        if (fixtureAsset is not null)
        {
            var bytes = await AutomaticTestResourceContract.ReadPinnedAsync(
                opened, fixtureAsset, ct).ConfigureAwait(false);
            fixture = (InspectionStationProcedureDocument)
                ProjectConfigurationCodec.Decode(fixtureAsset.Kind, bytes);
            fixture.Validate();
        }

        // Formal production requires a published immutable FLASH profile;
        // engineering verification remains entitled to its saved draft.
        var native = requireProductionDeployment
            ? await NativeFlashPublicationStore.LoadPublishedAsync(opened.RootDirectory, ct)
                .ConfigureAwait(false)
            : null;
        var draft = requireProductionDeployment ? null :
            await NativeFlashProfileStore.LoadAsync(opened.RootDirectory, ct)
                .ConfigureAwait(false);
        if (native is null && draft is null)
            throw new InvalidDataException(requireProductionDeployment
                ? "本型号工程缺少已确认用于生产的原生 FLASH 工艺。"
                : "该型号工程尚未保存烧录配置。");
        var profile = native?.Profile ?? draft!.Profile;
        var profileSha = native?.ProfileSha256 ?? draft!.ProfileSha256;
        if (profile.EcuModelCode != modelCode)
            throw new InvalidDataException("烧录配置与扫描型号不一致。");
        var frozenJob = await Task.Run(() =>
            native is not null
                ? NativeFlashPublicationStore.FreezePublished(opened.RootDirectory, native)
                : NativeFlashJobBuilder.Freeze(profile, opened.RootDirectory),
            ct).ConfigureAwait(false);
        if (deployed is not null &&
            (!string.Equals(deployed.Pin.NativeProfileSha256, profileSha,
                StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(deployed.Pin.FlashJobFingerprint, frozenJob.Fingerprint,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "当前 FLASH 工艺或固件与已确认的生产版本不同；必须重新核对并确认。");
        var pinned = string.Join("\n",
            "AUTOTEST-PRODUCTION-PREPARE-V1",
            modelCode, dutSerial, actual.Id.Value.ToString("D"), manifestSha,
            testAsset.Id.Value.ToString("D"), testAsset.Sha256,
            profileSha, frozenJob.Fingerprint,
            "FIXTURE:" + (fixtureAsset is null
                ? "NO_EXTRA_ACTIONS:" + StationDeploymentStore.NoProcedureSha256
                : fixtureAsset.Id.Value.ToString("D") + ":" + fixtureAsset.Revision +
                  ":" + fixtureAsset.Sha256),
            string.Join("\n", frozenCases.Select(c =>
                c.EntryId.ToString("D") + ":" + c.TestAssetId.ToString("D") +
                ":" + c.Revision + ":" + c.Sha256)));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(pinned)));
        if (!string.Equals(Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(opened.ManifestPath, ct)
                    .ConfigureAwait(false))),
                manifestSha, StringComparison.OrdinalIgnoreCase))
            throw new IOException("生产配置冻结期间工程清单发生更改。");
        return new(modelCode, dutSerial, actual.Id.Value,
            opened.ManifestPath, manifestSha, testAsset.Id.Value, testAsset.Sha256,
            frozenCases.ToArray(), profileSha, frozenJob.Fingerprint,
            frozenJob.FirmwareSha256, frozenJob.SecuritySha256,
            profile.EcuCanKbps, fingerprint, frozenJob)
        {
            FixtureAssetId = fixtureAsset?.Id.Value ?? Guid.Empty,
            FixtureContentSha256 = fixtureAsset?.Sha256 ??
                StationDeploymentStore.NoProcedureSha256,
            Fixture = new FrozenStationProcedure(
                fixture?.BeforeStart.ToImmutableArray() ??
                    ImmutableArray<InspectionStationAction>.Empty,
                fixture?.AfterFinish.ToImmutableArray() ??
                    ImmutableArray<InspectionStationAction>.Empty)
        };
    }
}
