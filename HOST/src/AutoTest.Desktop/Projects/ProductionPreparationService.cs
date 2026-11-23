using System.IO;
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
public sealed record FrozenProductionPreparation(
    string ModelCode, string DutSerial, Guid ProjectId, string ManifestPath,
    string ManifestSha256, Guid TestProjectAssetId, string TestProjectSha256,
    IReadOnlyList<FrozenProductionCase> OrderedEnabledCases,
    string NativeProfileSha256, string JobFingerprint, string FirmwareSha256,
    string SecuritySha256, int EcuCanKbps, string PreparationFingerprint,
    CanToolFlashJob Job);

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
        CancellationToken ct = default)
    {
        try
        {
            return new("PRODUCTION_CONFIG_PREPARED",
                await FreezeAsync(project, scannedModelCode, dutSerial, ct)
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
        CancellationToken ct)
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

        await ProjectBundleContractAudit.ValidateAsync(actual, opened.ManifestPath, ct)
            .ConfigureAwait(false);
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

        // Never promote an autosaved engineering draft into production.
        var native = await NativeFlashPublicationStore.LoadPublishedAsync(
            opened.RootDirectory, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("该型号工程尚未发布当前有效烧录配置。");
        if (native.Profile.EcuModelCode != modelCode)
            throw new InvalidDataException("已发布 Flash Profile 与扫描型号不一致。");
        var frozenJob = await Task.Run(() =>
            NativeFlashPublicationStore.FreezePublished(opened.RootDirectory, native),
            ct).ConfigureAwait(false);
        var pinned = string.Join("\n",
            "AUTOTEST-PRODUCTION-PREPARE-V1",
            modelCode, dutSerial, actual.Id.Value.ToString("D"), manifestSha,
            testAsset.Id.Value.ToString("D"), testAsset.Sha256,
            native.ProfileSha256, frozenJob.Fingerprint,
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
            frozenCases.ToArray(), native.ProfileSha256, frozenJob.Fingerprint,
            frozenJob.FirmwareSha256, frozenJob.SecuritySha256,
            native.Profile.EcuCanKbps, fingerprint, frozenJob);
    }
}
