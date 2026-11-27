using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

public sealed record FrozenProductionCase(
    Guid EntryId, Guid TestAssetId, string DisplayName, int Revision, string Sha256);
/// <summary>
/// Complete COM barcode observation and the ONLY selected project identity.
/// Production barcode routing and engineering manual verification are distinct
/// sources; neither supplies a physical START or a formal inspection verdict.
/// The hardware adapter must bind the scan to its actual scanner and station.
/// </summary>
public enum FrozenScanSource
{
    EngineeringRaw,
    ProductionFixedMarker
}

public sealed record FrozenScanIdentity(
    Guid ObservationId, DateTimeOffset CapturedAtUtc,
    string ScannerPort, string RawBarcode, FrozenScanSource Source,
    string RuleDefinition, string RuleSha256, string RecognitionMarker,
    int RuleRevision, string PackageSha256, string StationSlotId,
    string ScannerPnpInstanceId, string EngineeringFixtureSha256);

public sealed record FrozenProductionPreparation(
    string RecognitionMarker, string FullBarcode, Guid ProjectId, string ManifestPath,
    string ManifestSha256, Guid TestProjectAssetId, string TestProjectSha256,
    IReadOnlyList<FrozenProductionCase> OrderedCases,
    string NativeProfileSha256, string JobFingerprint, string FirmwareSha256,
    string SecuritySha256, int EcuCanKbps, string PreparationFingerprint,
    CanToolFlashJob Job)
{
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
        OpenProjectContext project, string recognitionMarker, string fullBarcode,
        CancellationToken ct = default, bool requireProductionDeployment = true)
    {
        try
        {
            return new("PRODUCTION_CONFIG_PREPARED",
                await FreezeAsync(project, recognitionMarker, fullBarcode, ct,
                    requireProductionDeployment)
                    .ConfigureAwait(false),
                "工程资源、原生 FLASH 方法、固件和完整 TEST 顺序已离线核对。" +
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
        OpenProjectContext opened, string recognitionMarker, string fullBarcode,
        CancellationToken ct, bool requireProductionDeployment)
    {
        if (string.IsNullOrWhiteSpace(fullBarcode))
            throw new InvalidDataException("缺少本次 ECU 完整扫码编号。");
        if (fullBarcode.Length > 128 || fullBarcode.Any(char.IsControl))
            throw new InvalidDataException("外部唯一编号长度或控制字符无效。");
        var actual = await ProjectRepository.OpenDefinitionAsync(
            opened.ManifestPath, ct).ConfigureAwait(false);
        if (actual.Id != opened.Definition.Id)
            throw new InvalidDataException("工程 ProjectId 与预检上下文不一致。");
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
        static FrozenProductionCase FreezeInvocation(AutomaticTestCaseBinding item) =>
            new(item.Entry.ItemId, item.Asset.Id.Value, item.Entry.DisplayName,
                item.Asset.Revision, item.ContentSha256);
        var frozenCases = resolved.Cases.Select(FreezeInvocation).ToList();
        // No fixture or IGN station scripts are accepted from a product
        // project. Fixed clamp control is strictly station-owned, while
        // product-specific IGN belongs in TEST.
        if (actual.Assets.Any(x =>
                x.Kind == ProjectWorkspaceService.InspectionKind))
            throw new InvalidDataException(
                "该工程包含旧工位动作资源，请删除并将产品专属动作改为 TEST 步骤。");

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
            "AUTOTEST-PRODUCTION-PREPARE-V2",
            recognitionMarker, fullBarcode, actual.Id.Value.ToString("D"), manifestSha,
            testAsset.Id.Value.ToString("D"), testAsset.Sha256,
            profileSha, frozenJob.Fingerprint,
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
        return new(recognitionMarker, fullBarcode, actual.Id.Value,
            opened.ManifestPath, manifestSha, testAsset.Id.Value, testAsset.Sha256,
            frozenCases.ToArray(), profileSha, frozenJob.Fingerprint,
            frozenJob.FirmwareSha256, frozenJob.SecuritySha256,
            profile.EcuCanKbps, fingerprint, frozenJob);
    }
}
