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
            if (asset.Kind != PublicFlowKind)
                throw new ProjectDataException("FLOW_REVISION_CONFLICT",
                    "公共流程修订发生变化，请重新加载；不得覆盖旧修订。");
            var result = await Task.Run(async () =>
            {
                var next = await SaveEditedResourceAsync(current, asset, bytes, opened.FileHash, ct).ConfigureAwait(false);
                var updated = next.Definition.Assets.Single(a => a.Id == asset.Id);
                var path = ProjectPathPolicy.Normalize(next.RootDirectory, updated.RelativePath);
                var reread = PublicFlowCodec.Deserialize(new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false)));
                return (Context: next, Session: new PublicFlowEditorSession(next.Definition.Id,
                    next.ManifestPath, asset.Id, updated.Revision, asset.Name, path, updated.Sha256, reread));
            }, ct);
            Current = result.Context;
            NotifyCurrentChanged();
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }
}
