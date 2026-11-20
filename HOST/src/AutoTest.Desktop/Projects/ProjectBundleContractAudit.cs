using System.IO;
using System.Text;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Read-only preflight at project OPEN. A manifest hash alone does not mean its
/// contained TEST/DBC resources can be decoded by the current application.
/// This check never connects to USB or authorizes any physical action.
/// </summary>
public static class ProjectBundleContractAudit
{
    public static async Task ValidateAsync(ProjectDefinition project, string manifestPath,
        CancellationToken ct = default)
    {
        var root = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("工程路径没有目录。");
        var assets = project.Assets.ToDictionary(x => x.Id.Value);
        foreach (var asset in project.Assets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = ProjectPathPolicy.Normalize(root, asset.RelativePath);
                if (!File.Exists(file))
                    throw new InvalidDataException("资源文件不存在：" + asset.RelativePath);
                var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
                if (!asset.VerifyHash(bytes))
                    throw new InvalidDataException("资源文件 SHA256 与工程清单不一致。");
                switch (asset.Kind)
                {
                    case ProjectWorkspaceService.DbcKind:
                        var dbc = DbcSource.Load(bytes);
                        _ = DbcWorkbenchDocument.Import(asset.Name, dbc.Text);
                        break;
                    case ProjectWorkspaceService.TestProgramKind:
                        var doc = TestDocumentCodec.Deserialize(
                            new UTF8Encoding(false, true).GetString(bytes));
                        foreach (var mapping in doc.Mappings)
                        {
                            mapping.Validate();
                            if (mapping.Asset.Value == Guid.Empty) continue;
                            if (!assets.TryGetValue(mapping.Asset.Value, out var dependency))
                                throw new InvalidDataException("TEST 对象 " + mapping.Name +
                                    " 指向不存在的工程资源。");
                            if (mapping.AssetRevision != dependency.Revision ||
                                !string.Equals(mapping.AssetSha256, dependency.Sha256,
                                    StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("TEST 对象 " + mapping.Name +
                                    " 引用的资源修订/指纹已失效。");
                        }
                        break;
                    case ProjectWorkspaceService.TestProjectKind:
                        var testProject = TestProjectCodec.Decode(bytes);
                        foreach (var package in testProject.Packages)
                        {
                            if (!assets.TryGetValue(package.PackageAssetId, out var target) ||
                                target.Kind != ProjectWorkspaceService.TestProgramKind)
                                throw new InvalidDataException("项目用例 " + package.DisplayName +
                                    " 未绑定存在的 TEST 程序资产。");
                            // An edited TEST advances its asset revision while the
                            // project intentionally keeps the previously pinned revision.
                            // This is a repairable edit-time state, not a corrupt project:
                            // do not lock the operator out of opening the workspace.
                            // The project editor must explicitly adopt and save the current
                            // revision. SaveTestProjectEditorAsync and real-run preflight
                            // still reject unresolved revision/SHA mismatches.
                            // Never rewrite the pinned reference during project OPEN.
                        }
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or ArgumentException or
                InvalidDataException or FormatException or NotSupportedException or
                AutoTest.Projects.Project.ProjectDataException)
            {
                throw new InvalidDataException("工程资源合同检查失败：" + asset.Kind + " / " +
                    asset.Name + "（" + asset.RelativePath + "）。" + error.Message, error);
            }
        }
    }
}
