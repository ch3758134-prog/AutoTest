using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// The station deploys ONE checked ECU project, never a separate operator
/// test plan or recipe. This is a pinned configuration deployment, NOT a claim
/// of physical production readiness, a FLASH authorization or a PASS.
/// Editing the project after deployment invalidates the pin, fail closed.
/// </summary>
public static class StationDeploymentStore
{
    public sealed record Deployment(
        int SchemaVersion,
        string ManifestPath,
        Guid ProjectId,
        string ManifestSha256,
        Guid TestProjectId,
        string TestProjectSha256,
        string NativeProfileSha256,
        string FlashJobFingerprint,
        Guid InspectionProcedureId,
        string InspectionProcedureSha256,
        DateTimeOffset DeployedAtUtc);

    public sealed record Verified(
        Deployment Pin,
        OpenProjectContext Project,
        AutomaticTestResourceSnapshot Test);

    public static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "station-deployment.json");

    // An ECU without fixture or IGN setup actions has no extra authored
    // station-procedure resource. This sentinel denotes that explicit absence;
    // it is not evidence that a physical clamp reached any position.
    public static readonly string NoProcedureSha256 =
        Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<Verified?> RestoreAsync(CancellationToken ct = default)
    {
        if (!File.Exists(PathName)) return null;
        var bytes = await File.ReadAllBytesAsync(PathName, ct).ConfigureAwait(false);
        if (bytes.Length is <= 0 or > 16384)
            throw new InvalidDataException("当前工位的部署记录长度无效。");
        var pin = JsonSerializer.Deserialize<Deployment>(bytes, Json)
            ?? throw new InvalidDataException("当前工位部署记录为空。");
        return await VerifyAsync(pin, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Explicitly deploys a source project AFTER verifying the complete
    /// authoring resource closure and its published native FLASH job.
    /// Existing development edits are preserved, and no USB is opened.
    /// </summary>
    public static async Task<Verified> DeployAsync(OpenProjectContext source,
        CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = await ReadProjectAsync(source.ManifestPath, ct)
                .ConfigureAwait(false);
            if (source.Definition.Id != fresh.Definition.Id ||
                !string.Equals(source.ManifestSha256, fresh.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("部署前工程发生变更；请保存并重新核对。");
            // Legacy ProjectBarcodeExpectation is not a formal admission gate.
            // Station publication is checked for each accepted real scan.
            var snapshot = await AutomaticTestResourceContract.ResolveAsync(fresh, ct)
                .ConfigureAwait(false);
            snapshot.RequireExecutable();
            var native = await NativeFlashPublicationStore.LoadPublishedAsync(
                fresh.RootDirectory, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("该工程缺少已发布的原生前置烧录工艺。");
            // FLASH eligibility is constrained by its verified method,
            // immutable job fingerprint and actual safety approval, not a
            // free-form project model label.
            var job = NativeFlashPublicationStore.FreezePublished(
                fresh.RootDirectory, native);
            var station = await UniqueProcedureAsync(fresh, ct).ConfigureAwait(false);
            var pin = new Deployment(1, fresh.ManifestPath,
                fresh.Definition.Id.Value, fresh.ManifestSha256,
                snapshot.TestProjectAsset.Id.Value, snapshot.TestProjectAsset.Sha256,
                native.ProfileSha256, job.Fingerprint,
                station?.Id.Value ?? Guid.Empty,
                station?.Sha256 ?? NoProcedureSha256, DateTimeOffset.UtcNow);
            var verified = await VerifyAsync(pin, ct).ConfigureAwait(false);
            var folder = Path.GetDirectoryName(PathName)!;
            Directory.CreateDirectory(folder);
            var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary,
                    JsonSerializer.SerializeToUtf8Bytes(pin, Json), ct).ConfigureAwait(false);
                File.Move(temporary, PathName, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return verified;
        }
        finally { Gate.Release(); }
    }

    private static async Task<OpenProjectContext> ReadProjectAsync(string path,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("部署工程文件不存在。", path);
        var full = Path.GetFullPath(path);
        // Protect against the editor or an external process rewriting the
        // manifest while the station is opening and freezing its references.
        var before = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        var definition = await ProjectRepository.OpenDefinitionAsync(full, ct)
            .ConfigureAwait(false);
        await ProjectBundleContractAudit.ValidateAsync(definition, full, ct).ConfigureAwait(false);
        var after = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        if (!SHA256.HashData(before).AsSpan()
                .SequenceEqual(SHA256.HashData(after)))
            throw new IOException("ECU 工程清单在部署校验中发生变更，拒绝使用中间状态。");
        // Only the active TEST closure, native FLASH release and selected
        // station procedure can authorize a deployed production scope.
        var manifestSha = Convert.ToHexString(SHA256.HashData(after));
        return new(definition, full, manifestSha);
    }

    private static async Task<AutoTest.Projects.Resources.AssetEntry?> UniqueProcedureAsync(
        OpenProjectContext project, CancellationToken ct)
    {
        var matches = project.Definition.Assets.Where(a =>
            a.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1)
            throw new InvalidDataException("同一工程存在多个工位动作定义，不能自动猜选。");
        var bytes = await AutomaticTestResourceContract.ReadPinnedAsync(
            project, matches[0], ct).ConfigureAwait(false);
        var procedure = (InspectionStationProcedureDocument)
            ProjectConfigurationCodec.Decode(matches[0].Kind, bytes);
        procedure.Validate();
        return matches[0];
    }

    public static async Task<Verified> VerifyAsync(Deployment pin, CancellationToken ct)
    {
        if (pin.SchemaVersion != 1 || pin.ProjectId == Guid.Empty ||
            pin.TestProjectId == Guid.Empty ||
            (pin.InspectionProcedureId == Guid.Empty &&
             !string.Equals(pin.InspectionProcedureSha256,
                 NoProcedureSha256, StringComparison.OrdinalIgnoreCase)) ||
            new[] { pin.ManifestSha256, pin.TestProjectSha256,
                pin.NativeProfileSha256, pin.FlashJobFingerprint,
                pin.InspectionProcedureSha256 }.Any(x =>
                    x is not { Length: 64 } || !x.All(Uri.IsHexDigit)))
            throw new InvalidDataException("工位工程部署指纹或标识无效。");
        var current = await ReadProjectAsync(pin.ManifestPath, ct).ConfigureAwait(false);
        if (current.Definition.Id.Value != pin.ProjectId ||
            !string.Equals(current.ManifestSha256, pin.ManifestSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "已部署的 ECU 工程发生改变；开发草稿不会自动替换现场部署，请重新确认部署。");
        // Production identity is solely station-owned; no legacy project scanner gate.
        var test = await AutomaticTestResourceContract.ResolveAsync(current, ct)
            .ConfigureAwait(false);
        test.RequireExecutable();
        if (test.TestProjectAsset.Id.Value != pin.TestProjectId ||
            !string.Equals(test.TestProjectAsset.Sha256, pin.TestProjectSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("正式 TEST 项目已发生变化，必须重新部署。");
        var flash = await NativeFlashPublicationStore.LoadPublishedAsync(
            current.RootDirectory, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("工位部署引用的原生烧录发布已不存在。");
        if (!string.Equals(flash.ProfileSha256, pin.NativeProfileSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("烧录工艺已变更，必须重新部署。");
        var job = NativeFlashPublicationStore.FreezePublished(
            current.RootDirectory, flash);
        if (!string.Equals(job.Fingerprint, pin.FlashJobFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("生产固件或安全算法指纹已变化，必须重新部署。");
        var procedure = await UniqueProcedureAsync(current, ct).ConfigureAwait(false);
        if ((procedure?.Id.Value ?? Guid.Empty) != pin.InspectionProcedureId ||
            !string.Equals(procedure?.Sha256 ?? NoProcedureSha256,
                pin.InspectionProcedureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工位准备/收尾动作已变更，必须重新发布工程。");
        return new(pin, current, test);
    }
}
