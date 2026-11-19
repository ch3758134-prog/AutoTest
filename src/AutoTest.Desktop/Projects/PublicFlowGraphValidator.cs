using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Transitive fixed-version and cycle validation for G3 public flows.
/// Never resolves a call by mutable display name or silently upgrades revision.
/// </summary>
public static class PublicFlowGraphValidator
{
    private const int MaxDepth = 16;
    private const int MaxDependencies = 256;

    public static async Task ValidateAsync(OpenProjectContext project,
        AssetId rootAssetId, PublicFlowDocument proposed,
        CancellationToken cancellationToken = default)
    {
        var stack = new List<Guid>();
        var completed = new HashSet<Guid>();
        var visited = 0;
        async Task Visit(Guid assetId, PublicFlowDocument document, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stack.Contains(assetId))
            {
                var path = string.Join(" → ", stack.Concat([assetId]).Select(id =>
                    project.Definition.Assets.FirstOrDefault(a => a.Id.Value == id)?.Name ??
                    id.ToString("D")));
                throw new ProjectDataException("FLOW_CALL_CYCLE",
                    "公共流程循环调用：" + path);
            }
            if (depth > MaxDepth)
                throw new ProjectDataException("FLOW_DEPTH_LIMIT",
                    "公共流程嵌套深度超过安全上限。");
            if (++visited > MaxDependencies)
                throw new ProjectDataException("FLOW_DEPENDENCY_LIMIT",
                    "公共流程引用数量超过安全上限。");
            if (completed.Contains(assetId)) return;
            stack.Add(assetId);
            try
            {
                foreach (var reference in document.References)
                {
                    reference.Validate();
                    var asset = project.Definition.Assets.FirstOrDefault(
                        a => a.Id.Value == reference.AssetId);
                    if (asset is null || asset.Kind != ProjectWorkspaceService.PublicFlowKind ||
                        asset.Revision != reference.Revision ||
                        !string.Equals(asset.Sha256, reference.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("STALE_REFERENCE",
                            "公共流程引用缺失或版本已更新；请显式更新该引用：" +
                            reference.AssetId);
                    var path = ProjectPathPolicy.Normalize(
                        project.RootDirectory, asset.RelativePath);
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                        .ConfigureAwait(false);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes));
                    if (!string.Equals(hash, reference.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("FLOW_FILE_CONFLICT",
                            "被调用公共流程文件的 SHA 与固定引用不一致：" + asset.Name);
                    var child = PublicFlowCodec.Deserialize(
                        new UTF8Encoding(false, true).GetString(bytes));
                    await Visit(reference.AssetId, child, depth + 1)
                        .ConfigureAwait(false);
                }
            }
            finally { stack.RemoveAt(stack.Count - 1); }
            completed.Add(assetId);
        }

        await Visit(rootAssetId.Value, proposed, 0).ConfigureAwait(false);
    }
}
