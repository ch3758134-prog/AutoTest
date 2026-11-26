using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

public sealed partial class ProjectWorkspaceService
{
    // Current files are authoritative. Runtime/RECORD receipts are computed
    // internally; missing resources never prevent opening the engineering tree.
    internal static async Task<ProjectDefinition> ReadCurrentResourceMetadataAsync(
        ProjectDefinition definition, string root, CancellationToken ct)
    {
        var assets = definition.Assets.ToBuilder();
        for (var i = 0; i < assets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var asset = assets[i];
            var path = ProjectPathPolicy.Normalize(root, asset.RelativePath);
            try
            {
                var hash = await HashFileAsync(path, ct).ConfigureAwait(false);
                if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    assets[i] = asset with { Sha256 = hash.ToLowerInvariant(), Revision = checked(asset.Revision + 1) };
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return definition with { Assets = assets.ToImmutable() };
    }

    // Resolve only the actual run scope. Never modify authored signal identity,
    // unit, step parameters, or historical evidence.
    internal static TestDocument BindCurrentRunResources(TestDocument document, OpenProjectContext? project)
    {
        if (project is null) return document;
        var required = TestRunStepScope.RequiredMappings(document).Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document with
        {
            Mappings = document.Mappings.Select(mapping =>
            {
                if (!required.Contains(mapping.Name) || mapping.Asset.Value == Guid.Empty) return mapping;
                var asset = project.Definition.Assets.SingleOrDefault(a => a.Id == mapping.Asset);
                return asset is null ? mapping : mapping with
                { AssetRevision = asset.Revision, AssetSha256 = asset.Sha256 };
            }).ToImmutableArray()
        };
    }

    // Save one edited file, not a recursive rewrite of the project.
    // The editor's observed content is used only to prevent lost updates.
    private static async Task<OpenProjectContext> SaveEditedResourceAsync(
        OpenProjectContext current, AssetEntry source, byte[] replacement,
        string expectedFileHash, CancellationToken ct)
    {
        await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
        var path = ProjectPathPolicy.Normalize(current.RootDirectory, source.RelativePath);
        var actual = await HashFileAsync(path, ct).ConfigureAwait(false);
        if (!actual.Equals(expectedFileHash, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("FILE_CHANGED", "文件在编辑期间被其他操作修改，请重新打开：" + source.Name);
        var hash = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        if (actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) return current;
        var updated = source with { Revision = checked(source.Revision + 1), Sha256 = hash };
        var next = (current.Definition with
        {
            Assets = current.Definition.Assets.Select(a => a.Id == source.Id ? updated : a).ToImmutableArray()
        }).WithRevision().Validate();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = path + "." + Guid.NewGuid().ToString("N") + ".bak";
        var replaced = false;
        var committed = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, replacement, ct).ConfigureAwait(false);
            await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
            if (!(await HashFileAsync(path, ct).ConfigureAwait(false)).Equals(actual, StringComparison.OrdinalIgnoreCase))
                throw new IOException("保存前文件发生变化：" + source.Name);
            // Preserve the exact previous bytes before replacing the ONLY
            // current working file. A missing old revision must never be
            // silently invented by binding to the newest asset.
            await ProjectResourceRevisionArchive.PreserveCurrentAsync(
                current.RootDirectory, source, path, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Replace(temporary, path, backup);
            replaced = true;
            await ProjectRepository.SaveDefinitionAsync(next, current.ManifestPath, ct).ConfigureAwait(false);
            committed = true;
            return new OpenProjectContext(next, current.ManifestPath,
                await HashFileAsync(current.ManifestPath, CancellationToken.None).ConfigureAwait(false));
        }
        catch
        {
            if (replaced && !committed && File.Exists(backup) &&
                (await HashFileAsync(path, CancellationToken.None).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                File.Replace(backup, path, null);
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (committed && File.Exists(backup))
            {
                try { File.Delete(backup); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
