using System.IO;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Projects.KeyActions;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Projects;

public sealed record KeyActionCatalogSession(
    ProjectId ProjectId, string ManifestPath, AssetId AssetId, int AssetRevision,
    string FileSha256, KeyActionCatalog Document);

public sealed partial class ProjectWorkspaceService
{
    public async Task<KeyActionCatalogSession> OpenKeyActionsAsync(CancellationToken ct = default)
    {
        var project = Current ?? throw new InvalidOperationException("请先打开工程。");
        var asset = project.Definition.Assets.SingleOrDefault(a => a.Kind == KeyActionsKind);
        if (asset is null)
            throw new ProjectDataException("KEY_CATALOG_MISSING", "当前工程尚未创建按键动作库。");
        var full = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
        var bytes = await File.ReadAllBytesAsync(full, ct);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var document = KeyActionCatalogCodec.Decode(bytes);
        return new KeyActionCatalogSession(project.Definition.Id, project.ManifestPath,
            asset.Id, asset.Revision, sha, document);
    }

    public async Task<KeyActionCatalogSession> CreateKeyActionsAsync(CancellationToken ct = default)
    {
        var project = Current ?? throw new InvalidOperationException("请先打开工程。");
        if (project.Definition.Assets.Any(a => a.Kind == KeyActionsKind))
            return await OpenKeyActionsAsync(ct);
        await AddAssetAsync(KeyActionsKind, "按键动作库", ".key-actions.json",
            KeyActionCatalogCodec.Encode(KeyActionCatalog.Create()), ct);
        return await OpenKeyActionsAsync(ct);
    }

    /// <summary>Content first, verify and then advance the SAME project manifest.
    /// No second project file or ad-hoc JSON outside the registered asset tree.</summary>
    public async Task<KeyActionCatalogSession> SaveKeyActionsAsync(
        KeyActionCatalogSession opened, KeyActionCatalog updated, CancellationToken ct = default)
    {
        if (updated.CatalogId != opened.Document.CatalogId)
            throw new ProjectDataException("KEY_CATALOG_ID", "动作库身份不允许改变。");
        var bytes = KeyActionCatalogCodec.Encode(updated);
        var newSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !current.ManifestPath.Equals(opened.ManifestPath, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("KEY_CATALOG_PROJECT", "工程已切换，请重新加载动作库。");
            var index = -1;
            for (var i = 0; i < current.Definition.Assets.Length; i++)
                if (current.Definition.Assets[i].Id == opened.AssetId) { index = i; break; }
            if (index < 0) throw new ProjectDataException("KEY_CATALOG_REMOVED", "动作库已被移除。");
            var asset = current.Definition.Assets[index];
            if (asset.Kind != KeyActionsKind)
                throw new ProjectDataException("KEY_CATALOG_STALE", "动作库修订已变化，请重新打开。");
            await EnsureManifestUnchangedAsync(current, ct);
            var file = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var actual = await HashFileAsync(file, ct);
            if (!actual.Equals(opened.FileSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("KEY_CATALOG_FILE_CONFLICT",
                    "动作库文件已被外部修改，不允许覆盖。");
            if (newSha.Equals(actual, StringComparison.OrdinalIgnoreCase)) return opened;
            if (updated.Controller is { } pin)
            {
                var controller = current.Definition.Assets.SingleOrDefault(a =>
                    a.Id.Value == pin.AssetId && a.Kind == ControllerKind);
                if (controller is null || controller.Revision != pin.Revision ||
                    !controller.Sha256.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("KEY_CONTROLLER_STALE",
                        "保存动作时 Controller 已变化，请重新加载配置。");
                var cfg = await OpenConfigurationAsync(controller.Id, ct);
                if (!cfg.FileSha256.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("KEY_CONTROLLER_STALE", "Controller 文件已变化。");
            }
            var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bak = file + ".bak";
            var manifestAttempted = false;
            try
            {
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                KeyActionCatalogCodec.Decode(await File.ReadAllBytesAsync(tmp, ct));
                await EnsureManifestUnchangedAsync(current, ct);
                if (!string.Equals(await HashFileAsync(file, ct), actual,
                    StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("KEY_CATALOG_FILE_CONFLICT", "保存前动作库发生变化。");
                File.Replace(tmp, file, bak);
                var reloaded = KeyActionCatalogCodec.Decode(await File.ReadAllBytesAsync(file, ct));
                if (!newSha.Equals(await HashFileAsync(file, ct), StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("KEY_CATALOG_VERIFY", "保存后 SHA 验证失败。");
                var nextAsset = asset with { Revision = checked(asset.Revision + 1), Sha256 = newSha };
                var nextProject = (current.Definition with
                { Assets = current.Definition.Assets.SetItem(index, nextAsset) }).WithRevision().Validate();
                await EnsureManifestUnchangedAsync(current, ct);
                manifestAttempted = true;
                await ProjectRepository.SaveDefinitionAsync(nextProject, current.ManifestPath, ct);
                var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct);
                var hash = await HashFileAsync(current.ManifestPath, ct);
                Current = new OpenProjectContext(verified, current.ManifestPath, hash);
                var session = new KeyActionCatalogSession(verified.Id, current.ManifestPath,
                    asset.Id, nextAsset.Revision, newSha, reloaded);
                NotifyCurrentChanged();
                return session;
            }
            catch
            {
                if (!manifestAttempted && File.Exists(bak) && File.Exists(file) &&
                    (await HashFileAsync(file, CancellationToken.None)).Equals(newSha,
                        StringComparison.OrdinalIgnoreCase))
                    File.Copy(bak, file, true);
                throw;
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        finally { _writeLock.Release(); }
    }
}
