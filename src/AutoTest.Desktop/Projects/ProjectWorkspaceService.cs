using System.IO;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Dbc;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Wave;
using System.Security.Cryptography;
using System.Text;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// The sole owner of the open project for the desktop UI. No second project model.
/// </summary>
public sealed record OpenProjectContext(ProjectDefinition Definition, string ManifestPath, string ManifestSha256)
{
    public string RootDirectory => Path.GetDirectoryName(ManifestPath)!;
}

public sealed class ProjectWorkspaceService
{
    public const string ManifestFileName = "project.autotest-project.json";
    public const string TestProgramKind = "test-program";
    public const string DbcKind = "dbc";
    public const string FirmwareKind = "firmware";
    public const string WaveKind = "wave";
    public const string ControllerKind = ProjectConfigurationCodec.ControllerKind;
    public const string WaveSettingsKind = ProjectConfigurationCodec.WaveSettingsKind;
    public const string FlashSettingsKind = ProjectConfigurationCodec.FlashSettingsKind;
    public const string ScanSettingsKind = ProjectConfigurationCodec.ScanSettingsKind;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public string SuggestedDirectory => Current?.RootDirectory ?? AppContext.BaseDirectory;
    public OpenProjectContext? Current { get; private set; }
    public event EventHandler? CurrentChanged;

    /// <summary>Validate strings without touching disk; safe for each TextBox keystroke.</summary>
    public static string ValidateInput(string name, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "请输入工程名称。";
        if (name != name.Trim() || name.Length > 100)
            return "工程名称不能以空格开头或结尾，长度不得超过 100 个字符。";
        if (name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "工程名称包含不允许的字符或结尾符号。";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                  stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             stem[3] is >= '1' and <= '9'))
            return "工程名称不能使用 Windows 保留设备名。";
        if (string.IsNullOrWhiteSpace(parentDirectory))
            return "请选择工程保存位置。";
        try
        {
            var full = Path.GetFullPath(parentDirectory);
            if (!Path.IsPathFullyQualified(full))
                return "请选择完整的保存目录路径。";
            _ = Path.Combine(full, name, ManifestFileName);
            return "";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "工程保存路径无效：" + error.Message;
        }
    }

    public static string PreviewManifestPath(string name, string parentDirectory)
    {
        if (!string.IsNullOrEmpty(ValidateInput(name, parentDirectory)))
            return "填写工程名称与保存位置后显示创建路径";
        try { return Path.Combine(Path.GetFullPath(parentDirectory), name, ManifestFileName); }
        catch (Exception) { return "工程路径暂不可用"; }
    }

    public async Task<OpenProjectContext> CreateAsync(
        string name, string parentDirectory, CancellationToken cancellationToken = default)
    {
        var error = ValidateInput(name, parentDirectory);
        if (error.Length > 0) throw new ArgumentException(error);

        // ProjectRepository has an atomic FILE replace API. New PROJECT creation
        // must instead be non-overwriting. Build in a sibling staging directory,
        // and move it into its final name only when fully written and validated.
        var result = await Task.Run(async () =>
        {
            var parent = Path.GetFullPath(parentDirectory);
            if (!Directory.Exists(parent))
                throw new DirectoryNotFoundException("工程保存位置不存在，请重新选择目录。");
            var target = Path.Combine(parent, name);
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("同名工程目录已经存在，不能覆盖。请更换工程名称或保存位置。");

            var staging = Path.Combine(parent, ".autotest-create-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = ProjectDefinition.Create(name);
                var stagedManifest = Path.Combine(staging, ManifestFileName);
                await ProjectRepository.SaveDefinitionAsync(definition, stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                // Read the actual persisted file; no fabricated successful project state.
                var verified = await ProjectRepository.OpenDefinitionAsync(stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                if (verified.Id != definition.Id || verified.Name != name)
                    throw new InvalidDataException("工程文件写入后校验未通过。");
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, target); // refuses a pre-existing target
                var finalManifest = Path.Combine(target, ManifestFileName);
                var hash = await HashFileAsync(finalManifest, cancellationToken).ConfigureAwait(false);
                return new OpenProjectContext(verified, finalManifest, hash);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, recursive: true); }
                    catch (IOException) { /* Keep cleanup failure from masking the original error. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }, cancellationToken);

        // The desktop caller resumes on the WPF synchronization context.
        Current = result;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
    /// <summary>Open any formal project manifest without scanning every asset on the UI thread.</summary>
    public async Task<OpenProjectContext> OpenAsync(string manifestPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("请选择一个工程文件。", nameof(manifestPath));
        var result = await Task.Run(async () =>
        {
            var full = Path.GetFullPath(manifestPath);
            var definition = await ProjectRepository.OpenDefinitionAsync(full, ct).ConfigureAwait(false);
            var hash = await HashFileAsync(full, ct).ConfigureAwait(false);
            return new OpenProjectContext(definition, full, hash);
        }, ct);
        Current = result;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Open a DBC asset through the existing, fail-closed Projects workbench.</summary>
    public async Task<DbcEditorSession> OpenDbcEditorAsync(AssetId id, CancellationToken ct = default)
    {
        var current = Current ?? throw new InvalidOperationException("请先打开工程。");
        var asset = current.Definition.Assets.FirstOrDefault(x => x.Id == id)
            ?? throw new ProjectDataException("ASSET_NOT_FOUND", "DBC 工程资源已不存在。");
        if (!string.Equals(asset.Kind, DbcKind, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("该工程资源不是 DBC。");

        return await Task.Run(async () =>
        {
            var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var loaded = await DbcDocumentStore.OpenAsync(path, ct).ConfigureAwait(false);
            if (!string.Equals(loaded.Receipt.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("DBC_ASSET_CHANGED",
                    "DBC 文件与工程登记的 SHA256 不一致。为避免覆盖外部修改，已阻止编辑，请先核对文件和工程清单。");
            return new DbcEditorSession(current.Definition.Id, current.ManifestPath,
                current.ManifestSha256, asset.Id, asset.Revision, asset.Name,
                loaded.Document, loaded.Receipt);
        }, ct);
    }

    /// <summary>Persist edited DBC, verify reread, then advance project asset SHA and revision.</summary>
    public async Task<DbcEditorSession> SaveDbcEditorAsync(
        DbcEditorSession opened, CancellationToken ct = default)
    {
        if (!opened.Document.CanEdit)
            throw new ProjectDataException("DBC_UNSUPPORTED_READONLY", "该 DBC 包含当前写入器不支持的结构，不能编辑保存。");
        if (!opened.Document.IsDirty)
            return opened;

        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !string.Equals(current.ManifestPath, opened.ManifestPath, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("DBC_PROJECT_CHANGED", "当前工程已切换，请重新打开 DBC。");
            var assetIndex = -1;
            for (var i = 0; i < current.Definition.Assets.Length; i++)
                if (current.Definition.Assets[i].Id == opened.AssetId) { assetIndex = i; break; }
            if (assetIndex < 0) throw new ProjectDataException("DBC_ASSET_MISSING", "DBC 资源已从工程移除。");
            var asset = current.Definition.Assets[assetIndex];
            if (asset.Revision != opened.AssetRevision ||
                !string.Equals(asset.Sha256, opened.FileReceipt.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("DBC_REVISION_CONFLICT", "DBC 资源修订已变化，请重新加载后再保存。");

            // The workbench file store handles unchanged-file conflict and atomic replacement.
            // The project manifest is updated only after verified re-read of the stored DBC.
            var result = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
                var receipt = await DbcDocumentStore.SaveAsync(
                    opened.Document, path, opened.FileReceipt.Sha256, ct).ConfigureAwait(false);
                var check = await DbcDocumentStore.OpenAsync(path, ct).ConfigureAwait(false);
                if (!string.Equals(check.Receipt.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("DBC_SAVE_VERIFY", "DBC 文件保存后复核失败，请检查备份文件。");

                var updated = asset with
                {
                    Sha256 = receipt.Sha256,
                    Revision = checked(asset.Revision + 1)
                };
                var newProject = (current.Definition with
                {
                    Assets = current.Definition.Assets.SetItem(assetIndex, updated)
                }).WithRevision().Validate();

                // If the manifest save fails, DbcDocumentStore has retained a .bak copy.
                // Never silently claim success when file and manifest revisions differ.
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                await ProjectRepository.SaveDefinitionAsync(newProject, current.ManifestPath, ct)
                    .ConfigureAwait(false);
                var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                    .ConfigureAwait(false);
                var manifestHash = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
                return (Context: new OpenProjectContext(verified, current.ManifestPath, manifestHash),
                    Document: check.Document, Receipt: check.Receipt, Revision: updated.Revision);
            }, ct);

            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return new DbcEditorSession(result.Context.Definition.Id, result.Context.ManifestPath,
                result.Context.ManifestSha256, opened.AssetId, result.Revision, asset.Name,
                result.Document, result.Receipt);
        }
        finally { _writeLock.Release(); }
    }


    /// <summary>Read a formal TEST resource with exact project and file hashes.</summary>
    public async Task<TestEditorSession> OpenTestEditorAsync(AssetId assetId, CancellationToken ct = default)
    {
        var current = Current ?? throw new InvalidOperationException("请先打开工程。");
        var asset = current.Definition.Assets.FirstOrDefault(x => x.Id == assetId)
            ?? throw new ProjectDataException("ASSET_NOT_FOUND", "TEST 资源不存在。");
        if (!string.Equals(asset.Kind, TestProgramKind, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("选中资源不是测试程序。");
        return await Task.Run(async () =>
        {
            var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("TEST_ASSET_CHANGED",
                    "TEST 文件与工程登记指纹不符，不能直接编辑。请检查外部修改。");
            var document = TestDocumentCodec.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
            return new TestEditorSession(current.Definition.Id, current.ManifestPath,
                asset.Id, asset.Revision, asset.Name, path, hash, document);
        }, ct);
    }

    /// <summary>Save a TEST document through its canonical codec and update the project asset.</summary>
    public async Task<TestEditorSession> SaveTestEditorAsync(
        TestEditorSession opened, TestDocument edited, CancellationToken ct = default)
    {
        // Validate the candidate before making *any* disk changes.
        var serialized = TestDocumentCodec.Serialize(edited);
        var decoded = TestDocumentCodec.Deserialize(serialized);
        if (opened.Document.Id != decoded.Id)
            throw new ProjectDataException("TEST_ID_CHANGED", "不能替换测试程序的持久身份。");
        if (TestDocumentCodec.Serialize(opened.Document) == serialized)
            return opened;
        var bytes = new UTF8Encoding(false).GetBytes(serialized);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已经关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !string.Equals(current.ManifestPath, opened.ManifestPath, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("TEST_PROJECT_CHANGED", "工程已切换，请重新打开测试程序。");
            var index = -1;
            for (var i = 0; i < current.Definition.Assets.Length; i++)
                if (current.Definition.Assets[i].Id == opened.AssetId) { index = i; break; }
            if (index < 0) throw new ProjectDataException("TEST_ASSET_MISSING", "测试程序已不在工程清单内。");
            var asset = current.Definition.Assets[index];
            if (!string.Equals(asset.Kind, TestProgramKind, StringComparison.OrdinalIgnoreCase) ||
                asset.Revision != opened.AssetRevision ||
                !asset.Sha256.Equals(opened.FileHash, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("TEST_REVISION_CONFLICT",
                    "测试资源已被另一编辑操作修改，不能覆盖旧版本。");

            var result = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
                var fileHash = await HashFileAsync(path, ct).ConfigureAwait(false);
                if (!fileHash.Equals(opened.FileHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("TEST_FILE_CONFLICT",
                        "TEST 文件在磁盘上发生变化，已拒绝覆盖。");
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var backup = path + ".bak";
                var manifestAttempted = false;
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                    var staged = TestDocumentCodec.Deserialize(
                        new UTF8Encoding(false, true).GetString(
                            await File.ReadAllBytesAsync(temporary, ct).ConfigureAwait(false)));
                    if (staged.Id != edited.Id)
                        throw new ProjectDataException("TEST_SAVE_VERIFY", "暂存测试程序复核失败。");

                    ct.ThrowIfCancellationRequested();
                    if (!File.Exists(path))
                        throw new ProjectDataException("TEST_FILE_CONFLICT", "原测试文件已丢失，不能覆盖。");
                    if (!string.Equals(await HashFileAsync(path, ct).ConfigureAwait(false),
                        opened.FileHash, StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("TEST_FILE_CONFLICT", "保存前测试文件被外部修改。");
                    File.Replace(temporary, path, backup);
                    var newHash = await HashFileAsync(path, ct).ConfigureAwait(false);
                    var reloaded = TestDocumentCodec.Deserialize(
                        new UTF8Encoding(false, true).GetString(
                            await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false)));
                    if (reloaded.Id != edited.Id || !newHash.Equals(
                        Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("TEST_SAVE_VERIFY", "写入测试文件后复核失败。");

                    var nextAsset = asset with
                    {
                        Sha256 = newHash,
                        Revision = checked(asset.Revision + 1)
                    };
                    var next = (current.Definition with
                    {
                        Assets = current.Definition.Assets.SetItem(index, nextAsset)
                    }).WithRevision().Validate();

                    await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                    manifestAttempted = true;
                    await ProjectRepository.SaveDefinitionAsync(next, current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var manifestHash = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
                    return (Context: new OpenProjectContext(verified, current.ManifestPath, manifestHash),
                        Session: new TestEditorSession(verified.Id, current.ManifestPath, asset.Id,
                            nextAsset.Revision, asset.Name, path, newHash, reloaded));
                }
                catch
                {
                    // If manifest write has not been attempted, restore the original from
                    // File.Replace backup, provided this process still owns the changed file.
                    if (!manifestAttempted && File.Exists(backup) && File.Exists(path) &&
                        string.Equals(await HashFileAsync(path, CancellationToken.None).ConfigureAwait(false),
                            Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                        File.Copy(backup, path, overwrite: true);
                    throw;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }, ct);
            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<AssetEntry> ImportDbcAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".dbc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择 .dbc 文件。");
        var source = Path.GetFullPath(sourcePath);
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("找不到所选 DBC 文件。", source);
        if (info.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("现有 DBC 工作台仅支持打开最大 4 MB 的文件。请先精简或拆分 DBC。");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        var name = Path.GetFileNameWithoutExtension(source);
        // Do not register a resource that the existing Projects parser cannot open.
        // Rich directives remain importable but are explicitly read-only if unsafe to export.
        await Task.Run(() =>
        {
            var decoded = DbcSource.Load(bytes);
            _ = DbcWorkbenchDocument.Import(name, decoded.Text);
        }, ct);
        return await AddAssetAsync(DbcKind, name, ".dbc", bytes, ct);
    }

    /// <summary>Import and validate an Intel HEX or S-record image; never guess a BIN load address.</summary>
    public async Task<AssetEntry> ImportFirmwareAsync(string sourcePath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is not (".hex" or ".s19" or ".s28" or ".s37"))
            throw new ArgumentException("固件资源仅支持 Intel HEX 或 S19/S28/S37。BIN 必须先配置基地址。");
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("固件文件不存在。", source);
        if (info.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("固件源文件超过 64 MB，已拒绝导入。");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        var text = Encoding.UTF8.GetString(bytes);
        await Task.Run(() =>
        {
            if (ext == ".hex") _ = FirmwareImageParser.ParseHex(text);
            else _ = FirmwareImageParser.ParseS19(text);
        }, ct);
        return await AddAssetAsync(FirmwareKind, Path.GetFileNameWithoutExtension(source), ext, bytes, ct);
    }

    /// <summary>Register validated waveform evidence; it does not claim to be a pinned reference.</summary>
    public async Task<AssetEntry> ImportWaveEvidenceAsync(string sourcePath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!source.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择波形证据 JSON 文件。");
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("波形文件不存在。", source);
        if (info.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("波形证据超过 64 MB，已拒绝导入。");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        await Task.Run(() =>
        {
            var document = WaveEvidenceCodec.Read(bytes);
            if (!WaveEvidenceCodec.Verify(document))
                throw new InvalidDataException("波形证据校验失败，不能登记为工程资源。");
        }, ct);
        var name = Path.GetFileNameWithoutExtension(source);
        return await AddAssetAsync(WaveKind, name, ".wave.json", bytes, ct);
    }

    public Task<AssetEntry> CreateConfigurationAsync(string kind, string name,
        CancellationToken ct = default)
    {
        if (!ProjectConfigurationCodec.IsConfiguration(kind))
            throw new ArgumentException("不支持的配置资源类型：" + kind);
        return AddAssetAsync(kind, name, ".settings.json",
            ProjectConfigurationCodec.Create(kind, name), ct);
    }

    public async Task<ProjectConfigurationSession> OpenConfigurationAsync(
        AssetId id, CancellationToken ct = default)
    {
        var current = Current ?? throw new InvalidOperationException("请先打开工程。");
        var asset = current.Definition.Assets.FirstOrDefault(x => x.Id == id)
            ?? throw new ProjectDataException("CONFIG_NOT_FOUND", "工程配置资源不存在。");
        if (!ProjectConfigurationCodec.IsConfiguration(asset.Kind))
            throw new ArgumentException("当前资源不是可编辑配置。");
        return await Task.Run(async () =>
        {
            var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            if (!sha.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("CONFIG_EXTERNAL_CHANGE",
                    "工程配置文件被外部修改，已阻止打开以避免覆盖。");
            var decoded = ProjectConfigurationCodec.Decode(asset.Kind, bytes);
            return new ProjectConfigurationSession(current.Definition.Id, current.ManifestPath,
                asset.Id, asset.Revision, asset.Name, asset.Kind, path, sha, decoded);
        }, ct);
    }

    /// <summary>Store validated typed configuration, then atomically advance project revision.</summary>
    public async Task<ProjectConfigurationSession> SaveConfigurationAsync(
        ProjectConfigurationSession opened, object document, CancellationToken ct = default)
    {
        var bytes = ProjectConfigurationCodec.Encode(opened.Kind, document);
        if (string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), opened.FileSha256,
            StringComparison.OrdinalIgnoreCase))
            return opened;
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !string.Equals(current.ManifestPath, opened.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("CONFIG_PROJECT_CHANGED", "工程已切换，不能保存旧配置。");
            var index = -1;
            for (var i = 0; i < current.Definition.Assets.Length; i++)
                if (current.Definition.Assets[i].Id == opened.AssetId) { index = i; break; }
            if (index < 0) throw new ProjectDataException("CONFIG_REMOVED", "工程配置已被移除。");
            var asset = current.Definition.Assets[index];
            if (!string.Equals(asset.Kind, opened.Kind, StringComparison.OrdinalIgnoreCase) ||
                asset.Revision != opened.AssetRevision ||
                !string.Equals(asset.Sha256, opened.FileSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("CONFIG_REVISION_CONFLICT",
                    "工程配置已经更新，请重新打开再编辑。");

            var result = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
                if (!string.Equals(await HashFileAsync(path, ct).ConfigureAwait(false),
                    opened.FileSha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("CONFIG_FILE_CONFLICT",
                        "配置文件被外部修改，已拒绝覆盖。");
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var backup = path + ".bak";
                var manifestAttempted = false;
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                    _ = ProjectConfigurationCodec.Decode(opened.Kind,
                        await File.ReadAllBytesAsync(temporary, ct).ConfigureAwait(false));
                    ct.ThrowIfCancellationRequested();
                    if (!File.Exists(path) || !string.Equals(
                        await HashFileAsync(path, ct).ConfigureAwait(false),
                        opened.FileSha256, StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("CONFIG_FILE_CONFLICT", "保存前原配置文件变化。");
                    File.Replace(temporary, path, backup);
                    var hash = await HashFileAsync(path, ct).ConfigureAwait(false);
                    var verifiedDocument = ProjectConfigurationCodec.Decode(opened.Kind,
                        await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
                    if (!hash.Equals(Convert.ToHexString(SHA256.HashData(bytes)),
                        StringComparison.OrdinalIgnoreCase))
                        throw new ProjectDataException("CONFIG_SAVE_VERIFY", "配置文件写入后校验失败。");

                    var nextAsset = asset with { Revision = checked(asset.Revision + 1), Sha256 = hash };
                    var nextProject = (current.Definition with
                    { Assets = current.Definition.Assets.SetItem(index, nextAsset) }).WithRevision().Validate();
                    await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                    manifestAttempted = true;
                    await ProjectRepository.SaveDefinitionAsync(nextProject, current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var manifestSha = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
                    var context = new OpenProjectContext(verified, current.ManifestPath, manifestSha);
                    var session = new ProjectConfigurationSession(verified.Id, current.ManifestPath,
                        asset.Id, nextAsset.Revision, asset.Name, asset.Kind, path, hash,
                        verifiedDocument);
                    return (Context: context, Session: session);
                }
                catch
                {
                    if (!manifestAttempted && File.Exists(backup) && File.Exists(path) &&
                        string.Equals(await HashFileAsync(path, CancellationToken.None).ConfigureAwait(false),
                            Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                        File.Copy(backup, path, overwrite: true);
                    throw;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, ct);
            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }

    public Task<AssetEntry> CreateTestProgramAsync(string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        var content = TestDocumentCodec.Serialize(TestDocument.Create(name));
        return AddAssetAsync(TestProgramKind, name, ".test.json", Encoding.UTF8.GetBytes(content), ct);
    }

    private async Task<AssetEntry> AddAssetAsync(
        string kind, string name, string extension, byte[] bytes, CancellationToken ct)
    {
        ValidateResourceName(name);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("请先新建或打开工程。");
            var entry = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                if (current.Definition.Assets.Any(x =>
                    x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("同类资源中已存在同名资源，请先重命名。");

                var folder = kind switch
                {
                    DbcKind => "dbc",
                    TestProgramKind => "tests",
                    FirmwareKind => "firmware",
                    WaveKind => "wave",
                    ControllerKind => "controller",
                    WaveSettingsKind => "wave",
                    FlashSettingsKind => "flash",
                    ScanSettingsKind => "scan",
                    _ => throw new ArgumentException("不支持的工程资源类型：" + kind)
                };
                var relative = "assets/" + folder + "/" + Guid.NewGuid().ToString("N") + extension;
                var editor = new ResourceEditService(current.Definition);
                var added = editor.Add(kind, name, relative, bytes);
                var path = ProjectPathPolicy.Normalize(current.RootDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var committed = false;
                try
                {
                    await using (var stream = new FileStream(
                        path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        81920, FileOptions.Asynchronous))
                    {
                        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                        await stream.FlushAsync(ct).ConfigureAwait(false);
                    }
                    await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                    await ProjectRepository.SaveDefinitionAsync(editor.Current, current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    committed = true;
                    var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                        .ConfigureAwait(false);
                    var hash = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
                    return (Asset: added, Context: new OpenProjectContext(verified, current.ManifestPath, hash));
                }
                catch
                {
                    // Never remove a successfully committed resource, even if the
                    // post-write verification itself failed.
                    if (!committed && File.Exists(path)) File.Delete(path);
                    throw;
                }
            }, ct);
            Current = entry.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return entry.Asset;
        }
        finally { _writeLock.Release(); }
    }

    public Task RenameAssetAsync(AssetId id, string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        return EditDefinitionAsync(editor => editor.Rename(id, name), ct);
    }

    /// <summary>Removes the manifest reference, NEVER deletes the underlying file.</summary>
    public Task RemoveAssetReferenceAsync(AssetId id, CancellationToken ct = default) =>
        EditDefinitionAsync(editor => editor.Remove(id), ct);

    private async Task EditDefinitionAsync(Action<ResourceEditService> change, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("请先打开工程。");
            var updated = await Task.Run(async () =>
            {
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                var editor = new ResourceEditService(current.Definition);
                change(editor);
                await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
                await ProjectRepository.SaveDefinitionAsync(editor.Current, current.ManifestPath, ct)
                    .ConfigureAwait(false);
                var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                    .ConfigureAwait(false);
                var hash = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
                return new OpenProjectContext(verified, current.ManifestPath, hash);
            }, ct);
            Current = updated;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _writeLock.Release(); }
    }

    private static void ValidateResourceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            name != name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("资源名称不能为空、不能包含文件名非法字符，长度不能超过 100 个字符。");
    }

    private static async Task EnsureManifestUnchangedAsync(OpenProjectContext current, CancellationToken ct)
    {
        var actual = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
        if (!string.Equals(actual, current.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("工程文件已被外部修改，请重新打开工程后再编辑，避免覆盖其他修改。");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

}
