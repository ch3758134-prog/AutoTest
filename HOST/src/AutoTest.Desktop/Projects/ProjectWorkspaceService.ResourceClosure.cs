using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Core;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

public sealed partial class ProjectWorkspaceService
{
    // The editable engineering project has one current resource closure.
    // Pins are maintained by the workspace, not manually reselected by the
    // operator after every save. Deployed/run snapshots are not rewritten.
    // Called only with _writeLock held; validate all candidates before writing.
    private static async Task<OpenProjectContext> SaveResourceClosureAsync(
        OpenProjectContext current, AssetEntry source, byte[] replacement,
        CancellationToken ct, ImmutableArray<AssetDependency>? dependencies = null)
    {
        await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
        var old = current.Definition.Assets.ToDictionary(a => a.Id);
        var latest = old.ToDictionary(p => p.Key, p => p.Value);
        var changed = new Dictionary<AssetId, byte[]>();
        var originals = new Dictionary<AssetId, byte[]>();
        var utf8 = new UTF8Encoding(false, true);

        async Task<byte[]> Read(AssetEntry asset)
        {
            if (originals.TryGetValue(asset.Id, out var cached)) return cached;
            var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!asset.VerifyHash(data))
                throw new ProjectDataException(asset.Id == source.Id ? source.Kind == DbcKind
                    ? "DBC_FILE_CONFLICT" : source.Kind == TestProgramKind ? "TEST_FILE_CONFLICT" :
                    source.Kind == PublicFlowKind ? "FLOW_FILE_CONFLICT" : "CONFIG_FILE_CONFLICT"
                    : "PROJECT_FILE_CONFLICT", "工程文件在磁盘上变化：" + asset.Name);
            originals.Add(asset.Id, data);
            return data;
        }
        async Task<byte[]> Candidate(AssetEntry asset) =>
            changed.TryGetValue(asset.Id, out var data) ? data : await Read(asset).ConfigureAwait(false);

        void Stage(AssetEntry asset, byte[] bytes)
        {
            if (asset.VerifyHash(bytes)) { changed.Remove(asset.Id); latest[asset.Id] = asset; return; }
            changed[asset.Id] = bytes;
            latest[asset.Id] = asset with { Revision = checked(asset.Revision + 1),
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        }

        static bool SameSha(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        AssetEntry Pin(AssetId id, int revision, string? sha, string name, string? expectedKind = null)
        {
            if (!old.TryGetValue(id, out var previous) ||
                (expectedKind is not null && previous.Kind != expectedKind) ||
                previous.Revision != revision || !SameSha(previous.Sha256, sha))
                throw new ProjectDataException("PROJECT_REFERENCE_INVALID",
                    name + "：保存前引用已不属于当前工程版本；禁止猜测替换。");
            return latest[id];
        }

        var beforeSource = await Read(source).ConfigureAwait(false);
        Stage(source, replacement);
        if (changed.Count == 0) return current;

        DbcDatabase? originalDbc = null, updatedDbc = null;
        if (source.Kind == DbcKind)
        {
            originalDbc = DbcParserAdapter.Parse(DbcSource.Load(beforeSource).Text, source.Name, allowSignalOverlaps: true);
            updatedDbc = DbcParserAdapter.Parse(DbcSource.Load(replacement).Text, source.Name);
        }

        void VerifyDbcMapping(TestTargetMapping mapping)
        {
            if (mapping.Asset != source.Id || updatedDbc is null) return;
            var frame = updatedDbc.Messages.SingleOrDefault(m =>
                m.ArbitrationId == mapping.CanId && m.IsExtended == mapping.Extended);
            var previous = originalDbc!.Messages.SingleOrDefault(m =>
                m.ArbitrationId == mapping.CanId && m.IsExtended == mapping.Extended);
            if (frame is null || previous is null ||
                (mapping.Kind == TestTargetKind.CanMessage && frame.Dlc != previous.Dlc) ||
                (mapping.Kind == TestTargetKind.Signal &&
                 !frame.Signals.Any(s => s.Name == mapping.SignalName && s.Unit == mapping.Unit)))
                throw new ProjectDataException("DBC_REFERENCED_OBJECT_CHANGED",
                    "DBC 修改会使 TEST 对象失效：" + mapping.Name +
                    "。请先调整引用该对象的测试步骤，再改变报文 ID、DLC 或信号身份/单位。");
        }

        ImmutableArray<TestTargetMapping> UpdateMappings(ImmutableArray<TestTargetMapping> mappings)
        {
            var builder = mappings.ToBuilder();
            for (var i = 0; i < builder.Count; i++)
            {
                var m = builder[i];
                m.Validate();
                if (m.Asset.Value == Guid.Empty) continue;
                var kind = m.Kind switch
                {
                    TestTargetKind.Signal or TestTargetKind.CanMessage => DbcKind,
                    TestTargetKind.Button => ControllerKind,
                    TestTargetKind.WaveProfile => WaveSettingsKind,
                    _ => throw new ProjectDataException("PROJECT_MAPPING_KIND", "不支持的工程资源映射：" + m.Name)
                };
                var target = Pin(m.Asset, m.AssetRevision, m.AssetSha256, m.Name, kind);
                VerifyDbcMapping(m);
                if (m.AssetRevision != target.Revision || !SameSha(m.AssetSha256, target.Sha256))
                    builder[i] = m with { AssetRevision = target.Revision, AssetSha256 = target.Sha256 };
            }
            return builder.ToImmutable();
        }

        // A recorded waveform is evidence for the exact signal definition
        // under which it was acquired. Never forge its historical pin.
        // Unrecorded settings can follow a retained signal identity.
        if (updatedDbc is not null)
        {
            foreach (var asset in old.Values.Where(a => a.Kind == WaveSettingsKind))
            {
                var data = await Read(asset).ConfigureAwait(false);
                var cfg = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(asset.Kind, data);
                if (cfg.DbcAssetId != source.Id.Value) continue;
                var pin = Pin(source.Id, cfg.DbcAssetRevision ?? 0, cfg.DbcAssetFingerprint,
                    asset.Name, DbcKind);
                if (!string.IsNullOrEmpty(cfg.ReferenceName) || cfg.EvidenceAssetId.HasValue)
                    throw new ProjectDataException("DBC_WAVE_REFERENCE",
                        "波形配置「" + asset.Name + "」已绑定真实参考/证据；不能自动改写其采集版本。" +
                        "请先解除该配置的参考绑定并重新核定参考波形。此次 DBC 未保存。");
                var matches = updatedDbc.Messages.Where(m => m.ArbitrationId == cfg.CanId)
                    .SelectMany(m => m.Signals).Where(s => s.Name == cfg.SignalName && s.Unit == cfg.Unit).Count();
                if (matches != 1)
                    throw new ProjectDataException("DBC_WAVE_SIGNAL", "DBC 修改会使波形信号失效：" + asset.Name);
                var next = cfg with { DbcAssetRevision = pin.Revision, DbcAssetFingerprint = pin.Sha256 };
                Stage(asset, ProjectConfigurationCodec.Encode(asset.Kind, next));
            }
        }

        var visiting = new HashSet<AssetId>();
        var visited = new HashSet<AssetId>();

        async Task<StepNode> UpdateNode(StepNode node)
        {
            var props = node.Properties;
            if (node.Type == "CallPublicFlow")
            {
                if (!Guid.TryParse(props.GetValueOrDefault("flowAssetId", ""), out var id) ||
                    !int.TryParse(props.GetValueOrDefault("flowRevision", ""), out var revision))
                    throw new ProjectDataException("FLOW_PIN", "公共流程调用缺少有效引用：" + node.Name);
                var targetId = AssetId.Create(id);
                _ = Pin(targetId, revision, props.GetValueOrDefault("flowSha", ""), node.Name, PublicFlowKind);
                await UpdateFlow(targetId).ConfigureAwait(false);
                var target = latest[targetId];
                if (revision != target.Revision || !SameSha(props.GetValueOrDefault("flowSha", ""), target.Sha256))
                    props = props.SetItem("flowRevision", target.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .SetItem("flowSha", target.Sha256);
            }
            var slots = node.Slots.ToBuilder();
            var modified = !ReferenceEquals(props, node.Properties);
            foreach (var slot in node.Slots)
            {
                var nodes = slot.Value.ToBuilder();
                for (var i = 0; i < nodes.Count; i++)
                {
                    var updated = await UpdateNode(nodes[i]).ConfigureAwait(false);
                    if (!ReferenceEquals(updated, nodes[i])) { nodes[i] = updated; modified = true; }
                }
                if (!nodes.SequenceEqual(slot.Value)) slots[slot.Key] = nodes.ToImmutable();
            }
            return modified ? node with { Properties = props, Slots = slots.ToImmutable() } : node;
        }

        async Task UpdateFlow(AssetId id)
        {
            if (visited.Contains(id)) return;
            if (visiting.Count >= 16 || !visiting.Add(id))
                throw new ProjectDataException("FLOW_CALL_CYCLE", "公共流程存在循环调用或嵌套过深。");
            var asset = old[id];
            var raw = await Candidate(asset).ConfigureAwait(false);
            var flow = PublicFlowCodec.Deserialize(utf8.GetString(raw));
            var mappings = UpdateMappings(flow.Mappings);
            var refs = flow.References.ToBuilder();
            for (var i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                var child = AssetId.Create(r.AssetId);
                _ = Pin(child, r.Revision, r.Sha256, asset.Name, PublicFlowKind);
                await UpdateFlow(child).ConfigureAwait(false);
                var pin = latest[child];
                if (r.Revision != pin.Revision || !SameSha(r.Sha256, pin.Sha256))
                    refs[i] = r with { Revision = pin.Revision, Sha256 = pin.Sha256 };
            }
            var nodes = flow.Body.ToBuilder();
            for (var i = 0; i < nodes.Count; i++)
                nodes[i] = await UpdateNode(nodes[i]).ConfigureAwait(false);
            if (!mappings.SequenceEqual(flow.Mappings) || !refs.SequenceEqual(flow.References) ||
                !nodes.SequenceEqual(flow.Body))
                Stage(asset, utf8.GetBytes(PublicFlowCodec.Serialize(flow with
                { Mappings = mappings, References = refs.ToImmutable(), Body = nodes.ToImmutable() })));
            visiting.Remove(id);
            visited.Add(id);
        }

        foreach (var asset in old.Values.Where(a => a.Kind == PublicFlowKind))
            await UpdateFlow(asset.Id).ConfigureAwait(false);
        foreach (var asset in old.Values.Where(a => a.Kind == TestProgramKind))
        {
            var raw = await Candidate(asset).ConfigureAwait(false);
            var doc = TestDocumentCodec.Deserialize(utf8.GetString(raw));
            var mappings = UpdateMappings(doc.Mappings);
            var phases = doc.Phases.ToBuilder();
            var modified = !mappings.SequenceEqual(doc.Mappings);
            foreach (var phase in doc.Phases)
            {
                var nodes = phase.Value.ToBuilder();
                for (var i = 0; i < nodes.Count; i++)
                    nodes[i] = await UpdateNode(nodes[i]).ConfigureAwait(false);
                if (!nodes.SequenceEqual(phase.Value))
                { phases[phase.Key] = nodes.ToImmutable(); modified = true; }
            }
            if (modified)
                Stage(asset, utf8.GetBytes(TestDocumentCodec.Serialize(doc with
                { Mappings = mappings, Phases = phases.ToImmutable() })));
        }
        foreach (var asset in old.Values.Where(a => a.Kind == TestProjectKind))
        {
            var raw = await Candidate(asset).ConfigureAwait(false);
            var plan = TestProjectCodec.Decode(raw);
            var entries = plan.Packages.Select(entry =>
            {
                var pin = Pin(AssetId.Create(entry.PackageAssetId), entry.PackageRevision,
                    entry.PackageSha256, entry.DisplayName, TestProgramKind);
                return entry.PackageRevision == pin.Revision && SameSha(entry.PackageSha256, pin.Sha256)
                    ? entry : entry with { PackageRevision = pin.Revision, PackageSha256 = pin.Sha256 };
            }).ToList();
            if (!entries.SequenceEqual(plan.Packages))
                Stage(asset, TestProjectCodec.Encode(plan with { Packages = entries }));
        }

        var deps = (dependencies ?? current.Definition.Dependencies).Select(dep =>
        {
            if (!old.TryGetValue(dep.To, out var previous) || previous.Revision != dep.RequiredRevision)
                throw new ProjectDataException("PROJECT_REFERENCE_INVALID", "工程依赖索引存在过期版本：" + dep.Purpose);
            return dep with { RequiredRevision = latest[dep.To].Revision };
        }).ToImmutableArray();
        var next = (current.Definition with
        {
            Assets = current.Definition.Assets.Select(a => latest[a.Id]).ToImmutableArray(),
            Dependencies = deps
        }).WithRevision().Validate();

        // Stage the entire write set. Do not modify a single resource until
        // every affected file has been read, encoded and verified.
        var writes = new List<(AssetEntry Asset, string Path, string Temp, string Backup)>();
        var replaced = new List<(AssetEntry Asset, string Path, string Backup)>();
        var committed = false;
        try
        {
            foreach (var pair in changed)
            {
                var asset = old[pair.Key];
                _ = await Read(asset).ConfigureAwait(false);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".closure.tmp";
                var backup = path + "." + Guid.NewGuid().ToString("N") + ".closure.bak";
                writes.Add((asset, path, temp, backup));
                await File.WriteAllBytesAsync(temp, pair.Value, ct).ConfigureAwait(false);
                if (!latest[pair.Key].VerifyHash(await File.ReadAllBytesAsync(temp, ct).ConfigureAwait(false)))
                    throw new IOException("工程资源暂存校验失败：" + asset.Name);
            }
            await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
            foreach (var write in writes)
                if (!write.Asset.VerifyHash(await File.ReadAllBytesAsync(write.Path, ct).ConfigureAwait(false)))
                    throw new IOException("工程资源在保存期间发生变化：" + write.Asset.Name);
            ct.ThrowIfCancellationRequested();
            foreach (var write in writes)
            {
                File.Replace(write.Temp, write.Path, write.Backup);
                replaced.Add((write.Asset, write.Path, write.Backup));
            }
            // This is the same audit used by project OPEN, not a permissive
            // compatibility path. Manifest remains unchanged until it passes.
            await ProjectBundleContractAudit.ValidateAsync(next, current.ManifestPath, ct).ConfigureAwait(false);
            await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
            await ProjectRepository.SaveDefinitionAsync(next, current.ManifestPath, ct).ConfigureAwait(false);
            committed = true;
            var hash = await HashFileAsync(current.ManifestPath, CancellationToken.None).ConfigureAwait(false);
            return new OpenProjectContext(next, current.ManifestPath, hash);
        }
        catch
        {
            if (!committed)
            {
                foreach (var write in replaced.AsEnumerable().Reverse())
                    if (File.Exists(write.Backup) &&
                        latest[write.Asset.Id].VerifyHash(await File.ReadAllBytesAsync(write.Path, CancellationToken.None).ConfigureAwait(false)))
                        File.Replace(write.Backup, write.Path, null);
            }
            throw;
        }
        finally
        {
            foreach (var write in writes)
            {
                if (File.Exists(write.Temp)) File.Delete(write.Temp);
                // Retain rollback data if an exceptional restore did not finish.
                if (committed && File.Exists(write.Backup))
                {
                    try { File.Copy(write.Backup, write.Path + ".bak", true); File.Delete(write.Backup); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
