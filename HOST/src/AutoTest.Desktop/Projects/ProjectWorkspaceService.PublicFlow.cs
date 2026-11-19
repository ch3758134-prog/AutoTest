using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>Exact project and resource revision for a public-flow editing session.</summary>
public sealed record PublicFlowEditorSession(
    ProjectId ProjectId, string ManifestPath, AssetId AssetId,
    int AssetRevision, string AssetName, string FilePath, string FileHash,
    PublicFlowDocument Document);

public sealed partial class ProjectWorkspaceService
{
    public Task<AssetEntry> CreatePublicFlowAsync(string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        var json = PublicFlowCodec.Serialize(PublicFlowDocument.Create(name));
        return AddAssetAsync(PublicFlowKind, name, ".flow.json",
            new UTF8Encoding(false).GetBytes(json), ct);
    }

    public async Task<PublicFlowEditorSession> OpenPublicFlowEditorAsync(
        AssetId id, CancellationToken ct = default)
    {
        var current = Current ?? throw new InvalidOperationException("请先打开工程。");
        var asset = current.Definition.Assets.FirstOrDefault(a => a.Id == id)
            ?? throw new ProjectDataException("FLOW_ASSET_MISSING", "公共流程资源不存在。");
        if (asset.Kind != PublicFlowKind)
            throw new ArgumentException("此资源不是公共流程。");
        return await Task.Run(async () =>
        {
            var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("FLOW_ASSET_CHANGED",
                    "公共流程文件与工程登记指纹不符，不能静默覆盖。");
            var document = PublicFlowCodec.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
            return new PublicFlowEditorSession(current.Definition.Id, current.ManifestPath,
                asset.Id, asset.Revision, asset.Name, path, hash, document);
        }, ct);
    }

    public async Task<PublicFlowEditorSession> SavePublicFlowEditorAsync(
        PublicFlowEditorSession opened, PublicFlowDocument edited,
        CancellationToken ct = default)
    {
        var serialized = PublicFlowCodec.Serialize(edited);
        var decoded = PublicFlowCodec.Deserialize(serialized);
        if (decoded.FlowId != opened.Document.FlowId)
            throw new ProjectDataException("FLOW_ID_CHANGED", "公共流程身份不允许变化。");
        if (serialized == PublicFlowCodec.Serialize(opened.Document)) return opened;
        var bytes = new UTF8Encoding(false).GetBytes(serialized);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已经关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !string.Equals(current.ManifestPath, opened.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("FLOW_PROJECT_CHANGED", "工程已切换。");
            var index = Array.FindIndex(current.Definition.Assets.ToArray(),
                a => a.Id == opened.AssetId);
            if (index < 0) throw new ProjectDataException("FLOW_ASSET_MISSING", "公共流程已移出工程。");
            var asset = current.Definition.Assets[index];
            if (asset.Kind != PublicFlowKind || asset.Revision != opened.AssetRevision ||
                !string.Equals(asset.Sha256, opened.FileHash, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("FLOW_REVISION_CONFLICT",
                    "公共流程修订发生变化，请重新加载；不得覆盖旧修订。");
            foreach (var reference in edited.References)
            {
                reference.Validate();
                if (!current.Definition.Assets.Any(a =>
                    a.Kind == PublicFlowKind && a.Id.Value == reference.AssetId &&
                    a.Revision == reference.Revision &&
                    string.Equals(a.Sha256, reference.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new ProjectDataException("STALE_REFERENCE",
                        "被调用的公共流程修订已变化；请显式审查并更新调用引用。");
            }
            // Do not store a flow call graph with unresolved historical pins
            // or a recursion cycle. The validator is read-only and does no writes.
            await PublicFlowGraphValidator.ValidateAsync(current, opened.AssetId, edited, ct)
                .ConfigureAwait(false);
            var result = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
                if (!string.Equals(await HashFileAsync(path, ct).ConfigureAwait(false),
                    opened.FileHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("FLOW_FILE_CONFLICT", "公共流程文件被外部修改。");
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var backup = path + ".bak";
                var manifestAttempted = false;
                try
                {
                    await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                    _ = PublicFlowCodec.Deserialize(new UTF8Encoding(false, true).GetString(
                        await File.ReadAllBytesAsync(temp, ct).ConfigureAwait(false)));
                    ct.ThrowIfCancellationRequested();
                    if (!File.Exists(path) ||
                        !string.Equals(await HashFileAsync(path, ct).ConfigureAwait(false),
                            opened.FileHash, StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("FLOW_FILE_CONFLICT",
                            "暂存期间公共流程原件发生变化。");
                    File.Replace(temp, path, backup);
                    var hash = await HashFileAsync(path, ct).ConfigureAwait(false);
                    var reread = PublicFlowCodec.Deserialize(new UTF8Encoding(false, true).GetString(
                        await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false)));
                    if (reread.FlowId != edited.FlowId ||
                        !string.Equals(hash, Convert.ToHexString(SHA256.HashData(bytes)),
                            StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("FLOW_SAVE_VERIFY", "公共流程保存回读校验失败。");
                    var updated = asset with
                    {
                        Sha256 = hash,
                        Revision = checked(asset.Revision + 1)
                    };
                    // Synchronize the canonical manifest dependency index with
                    // the pinned call graph. This prevents deleting a flow still
                    // referenced by another flow, while retaining other kinds
                    // of dependencies and their original historical revisions.
                    var preserved = current.Definition.Dependencies
                        .Where(dep => dep.From != opened.AssetId ||
                            !string.Equals(dep.Purpose, "public-flow-call",
                                StringComparison.Ordinal))
                        .ToImmutableArray();
                    var linked = edited.References.Select(reference =>
                        new AssetDependency(opened.AssetId, AssetId.Create(reference.AssetId),
                            reference.Revision, "public-flow-call"));
                    var next = (current.Definition with
                    {
                        Assets = current.Definition.Assets.SetItem(index, updated),
                        Dependencies = preserved.AddRange(linked)
                    }).WithRevision().Validate();
                    await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                    manifestAttempted = true;
                    await ProjectRepository.SaveDefinitionAsync(next, current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var verified = await ProjectRepository.OpenDefinitionAsync(
                        current.ManifestPath, ct).ConfigureAwait(false);
                    var manifestHash = await HashFileAsync(current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    return (Context: new OpenProjectContext(verified,
                            current.ManifestPath, manifestHash),
                        Session: new PublicFlowEditorSession(verified.Id,
                            current.ManifestPath, asset.Id, updated.Revision,
                            asset.Name, path, hash, reread));
                }
                catch
                {
                    if (!manifestAttempted && File.Exists(backup) && File.Exists(path) &&
                        string.Equals(await HashFileAsync(path, CancellationToken.None)
                                .ConfigureAwait(false),
                            Convert.ToHexString(SHA256.HashData(bytes)),
                            StringComparison.OrdinalIgnoreCase))
                        File.Copy(backup, path, overwrite: true);
                    throw;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }, ct);
            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }
}
