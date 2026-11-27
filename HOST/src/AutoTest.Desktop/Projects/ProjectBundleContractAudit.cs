using System.IO;
using System.Text;
using AutoTest.Core;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Canonical engineering resource closure. OPEN, creation and saves use the
/// same exact IDs, revisions, hashes and typed document codecs. No old-version
/// compatibility fallback. Incomplete newly authored steps may be edited;
/// physical execution additionally requires a complete compiled run plan.
/// </summary>
public static class ProjectBundleContractAudit
{
    public static async Task ValidateAsync(ProjectDefinition project, string manifestPath,
        CancellationToken ct = default)
    {
        var root = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("工程路径没有目录。");
        var assets = project.Assets.ToDictionary(x => x.Id.Value);
        var dbcs = new Dictionary<Guid, DbcDatabase>();
        var utf8 = new UTF8Encoding(false, true);
        AssetEntry Require(Guid id, int revision, string? sha, string name, string? kind = null)
        {
            if (!assets.TryGetValue(id, out var dependency) ||
                (kind is not null && dependency.Kind != kind) || dependency.Revision != revision ||
                !string.Equals(dependency.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(name + " 引用的工程资源身份、修订或指纹无效。");
            return dependency;
        }
        async Task<byte[]> Read(AssetEntry asset)
        {
            var file = ProjectPathPolicy.Normalize(root, asset.RelativePath);
            if (!File.Exists(file))
                throw new InvalidDataException("资源文件不存在：" + asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
            if (!asset.VerifyHash(bytes))
                throw new InvalidDataException("资源文件 SHA256 与工程清单不一致：" + asset.Name);
            return bytes;
        }
        async Task CheckMappings(IEnumerable<TestTargetMapping> mappings)
        {
            foreach (var mapping in mappings)
            {
                mapping.Validate();
                if (mapping.Kind is TestTargetKind.Ignition or TestTargetKind.RawCan or TestTargetKind.RawKey)
                    continue;
                var kind = mapping.Kind switch
                {
                    TestTargetKind.Signal or TestTargetKind.CanMessage => ProjectWorkspaceService.DbcKind,
                    TestTargetKind.Button => ProjectWorkspaceService.ControllerKind,
                    TestTargetKind.WaveProfile => ProjectWorkspaceService.WaveSettingsKind,
                    _ => throw new InvalidDataException("无效映射类型：" + mapping.Name)
                };
                var dependency = Require(mapping.Asset.Value, mapping.AssetRevision, mapping.AssetSha256,
                    "TEST 对象 " + mapping.Name, kind);
                if (kind != ProjectWorkspaceService.DbcKind) continue;
                if (!dbcs.TryGetValue(dependency.Id.Value, out var dbc))
                {
                    dbc = DbcParserAdapter.Parse(DbcSource.Load(await Read(dependency).ConfigureAwait(false)).Text,
                        dependency.Name, allowSignalOverlaps: true);
                    dbcs.Add(dependency.Id.Value, dbc);
                }
                var message = dbc.Messages.SingleOrDefault(m =>
                    m.ArbitrationId == mapping.CanId && m.IsExtended == mapping.Extended)
                    ?? throw new InvalidDataException("TEST 对象 " + mapping.Name + " 对应报文不存在。");
                if (mapping.Kind == TestTargetKind.Signal &&
                    !message.Signals.Any(s => s.Name == mapping.SignalName && s.Unit == mapping.Unit))
                    throw new InvalidDataException("TEST 对象 " + mapping.Name + " 对应信号或单位不一致。");
            }
        }
        void CheckCalls(IEnumerable<StepNode> nodes)
        {
            foreach (var node in nodes.Where(x => x.Type == "CallPublicFlow"))
            {
                if (!Guid.TryParse(node.Properties.GetValueOrDefault("flowAssetId", ""), out var id) ||
                    !int.TryParse(node.Properties.GetValueOrDefault("flowRevision", ""), out var revision))
                    throw new InvalidDataException("公共流程调用缺少资源身份：" + node.Name);
                Require(id, revision, node.Properties.GetValueOrDefault("flowSha", ""), node.Name,
                    ProjectWorkspaceService.PublicFlowKind);
            }
        }

        foreach (var dep in project.Dependencies)
        {
            if (!assets.TryGetValue(dep.To.Value, out var target) || target.Revision != dep.RequiredRevision)
                throw new InvalidDataException("工程依赖索引版本不一致：" + dep.Purpose);
        }
        foreach (var asset in project.Assets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var bytes = await Read(asset).ConfigureAwait(false);
                switch (asset.Kind)
                {
                    case ProjectWorkspaceService.DbcKind:
                        _ = DbcWorkbenchDocument.Import(asset.Name, DbcSource.Load(bytes).Text);
                        break;
                    case ProjectWorkspaceService.TestProgramKind:
                        var doc = TestDocumentCodec.Deserialize(utf8.GetString(bytes));
                        await CheckMappings(doc.Mappings).ConfigureAwait(false);
                        CheckCalls(doc.Walk());
                        break;
                    case ProjectWorkspaceService.PublicFlowKind:
                        var flow = PublicFlowCodec.Deserialize(utf8.GetString(bytes));
                        await CheckMappings(flow.Mappings).ConfigureAwait(false);
                        CheckCalls(flow.Walk());
                        foreach (var reference in flow.References)
                            Require(reference.AssetId, reference.Revision, reference.Sha256,
                                flow.Name, ProjectWorkspaceService.PublicFlowKind);
                        break;
                    case ProjectWorkspaceService.InspectionKind:
                        throw new InvalidDataException(
                            "工程仍包含旧版工位夹具/IGN 工艺资源。请移除该资源；" +
                            "固定夹具动作由工位设备负责，产品专属 IGN 动作应作为 TEST 步骤编写。");
                    case ProjectWorkspaceService.WaveSettingsKind:
                        var wave = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(asset.Kind, bytes);
                        if (wave.DbcAssetId is { } dbcId)
                            Require(dbcId, wave.DbcAssetRevision ?? 0, wave.DbcAssetFingerprint, wave.Name,
                                ProjectWorkspaceService.DbcKind);
                        if (wave.EvidenceAssetId is { } evidenceId)
                            Require(evidenceId, wave.EvidenceAssetRevision ?? 0, wave.EvidenceAssetFingerprint,
                                wave.Name, ProjectWorkspaceService.WaveKind);
                        break;
                    case ProjectWorkspaceService.TestProjectKind:
                        var testProject = TestProjectCodec.Decode(bytes);
                        foreach (var package in testProject.Packages)
                        {
                            if (!assets.TryGetValue(package.PackageAssetId, out var linked) ||
                                linked.Kind != ProjectWorkspaceService.TestProgramKind)
                                throw new InvalidDataException("项目用例不存在：" + package.DisplayName);
                            _ = await Read(linked).ConfigureAwait(false);
                        }
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or ArgumentException or
                InvalidDataException or FormatException or NotSupportedException or ProjectDataException or
                InvalidOperationException or System.Text.Json.JsonException)
            {
                throw new InvalidDataException("工程资源合同检查失败：" + asset.Kind + " / " +
                    asset.Name + "（" + asset.RelativePath + "）。" + error.Message, error);
            }
        }
    }
}
