using System.IO;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Dbc;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Wave;
using AutoTest.FlashLab.Programming;
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

public sealed partial class ProjectWorkspaceService
{
    public const string ManifestFileName = "project.autotest-project.json";
    public const string TestProgramKind = "test-program";
    public const string PublicFlowKind = "public-flow";
    public const string TestProjectKind = "test-project";
    public const string DbcKind = "dbc";
    public const string FirmwareKind = "firmware";
    public const string WaveKind = "wave";
    public const string ControllerKind = ProjectConfigurationCodec.ControllerKind;
    public const string WaveSettingsKind = ProjectConfigurationCodec.WaveSettingsKind;
    public const string FlashSettingsKind = ProjectConfigurationCodec.FlashSettingsKind;
    public const string ScanSettingsKind = ProjectConfigurationCodec.ScanSettingsKind;
    public const string TestConfigurationKind = ProjectConfigurationCodec.TestConfigurationKind;
    public const string BenchConfigurationKind = ProjectConfigurationCodec.BenchConfigurationKind;
    public const string InspectionKind = ProjectConfigurationCodec.InspectionKind;
    public const string KeyActionsKind = "bench-key-actions";
    private AssetId? _selectedTestConfiguration;
    private AssetId? _selectedBenchConfiguration;
    private string? _configurationProjectPath;
    public event EventHandler? SelectedConfigurationsChanged;
    public AssetId? SelectedTestConfiguration =>
        string.Equals(_configurationProjectPath, Current?.ManifestPath,
            StringComparison.OrdinalIgnoreCase) ? _selectedTestConfiguration : null;
    public AssetId? SelectedBenchConfiguration =>
        string.Equals(_configurationProjectPath, Current?.ManifestPath,
            StringComparison.OrdinalIgnoreCase) ? _selectedBenchConfiguration : null;
    public string SelectedTestConfigurationName =>
        Current?.Definition.Assets.FirstOrDefault(x => x.Id == SelectedTestConfiguration)?.Name
            ?? "未选择 TCF";
    public string SelectedBenchConfigurationName =>
        Current?.Definition.Assets.FirstOrDefault(x => x.Id == SelectedBenchConfiguration)?.Name
            ?? "固定台架（尚未选择 TBC）";
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

    // Desktop New Project creates one empty TEST project; no fabricated cases,
    // model field, or legacy scanner matching configuration.
    public Task<OpenProjectContext> CreateAsync(
        string name, string parentDirectory, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(name, parentDirectory, false, cancellationToken);

    public Task<OpenProjectContext> CreateReadyToEditAsync(
        string name, string parentDirectory, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(name, parentDirectory, true, cancellationToken);

    private async Task<OpenProjectContext> CreateCoreAsync(
        string name, string parentDirectory, bool withInitialTest,
        CancellationToken cancellationToken)
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
                if (withInitialTest)
                {
                // A new project contains one real empty TEST sequence.
                // No fabricated test case and no extra plan selection.
                const string sequenceName = "生产 TEST 项目";
                var sequence = TestProjectDocument.Create(sequenceName);
                var sequenceBytes = TestProjectCodec.Encode(sequence);
                var sequencePath = "assets/projects/" +
                    Guid.NewGuid().ToString("N") + ".prj.json";
                var sequenceAsset = AssetEntry.Create(TestProjectKind,
                    sequenceName, sequencePath, sequenceBytes);
                definition = (definition with
                {
                    Assets = definition.Assets.Add(sequenceAsset)
                }).WithRevision().Validate();
                var sequenceFull = ProjectPathPolicy.Normalize(staging, sequencePath);
                Directory.CreateDirectory(Path.GetDirectoryName(sequenceFull)!);
                await File.WriteAllBytesAsync(sequenceFull, sequenceBytes,
                    cancellationToken).ConfigureAwait(false);
                }
                var stagedManifest = Path.Combine(staging, ManifestFileName);
                await ProjectRepository.SaveDefinitionAsync(definition, stagedManifest,
                    cancellationToken).ConfigureAwait(false);
                // Verify the complete saved asset closure before publishing the
                // newly created folder. On failure the staging folder is discarded.
                var verified = await ProjectRepository.OpenDefinitionAsync(stagedManifest,
                    cancellationToken).ConfigureAwait(false);
                if (verified.Id != definition.Id || verified.Name != name ||
                    (withInitialTest && verified.Assets.Length != 1))
                    throw new InvalidDataException("新建 ECU 工程的用例资源未通过保存检查。");
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
            definition = await ReadCurrentResourceMetadataAsync(definition, Path.GetDirectoryName(full)!, ct).ConfigureAwait(false);
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
            return new DbcEditorSession(current.Definition.Id, current.ManifestPath,
                current.ManifestSha256, asset.Id, asset.Revision, asset.Name,
                loaded.Document, loaded.Receipt);
        }, ct);
    }

    /// <summary>Persist edited DBC, verify reread, then advance project asset SHA and revision.</summary>
    public async Task<DbcEditorSession> SaveDbcEditorAsync(
        DbcEditorSession opened, CancellationToken ct = default)
    {
        if (!opened.Document.CanSave)
            throw new ProjectDataException("DBC_UNSUPPORTED_READONLY",
                "该 DBC 的结构修改暂不支持安全保存。中文别名可独立修改，并保留原文件内容。");
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
            if (asset.Kind != DbcKind)
                throw new ProjectDataException("DBC_REVISION_CONFLICT", "DBC 资源修订已变化，请重新加载后再保存。");

            var bytes = new UTF8Encoding(false).GetBytes(opened.Document.ExportText());
            var result = await Task.Run(async () =>
            {
                var next = await SaveEditedResourceAsync(current, asset, bytes, opened.FileReceipt.Sha256, ct).ConfigureAwait(false);
                var updated = next.Definition.Assets.Single(a => a.Id == asset.Id);
                var path = ProjectPathPolicy.Normalize(next.RootDirectory, updated.RelativePath);
                var check = await DbcDocumentStore.OpenAsync(path, CancellationToken.None).ConfigureAwait(false);
                if (!string.Equals(check.Receipt.Sha256, updated.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectDataException("DBC_SAVE_VERIFY", "DBC 保存后文件指纹不一致。");
                return (Context: next, Document: check.Document, Receipt: check.Receipt, Revision: updated.Revision);
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
            if (!string.Equals(asset.Kind, TestProgramKind, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("TEST_REVISION_CONFLICT",
                    "测试资源已被另一编辑操作修改，不能覆盖旧版本。");

            var result = await Task.Run(async () =>
            {
                var next = await SaveEditedResourceAsync(current, asset, bytes, opened.FileHash, ct).ConfigureAwait(false);
                var updated = next.Definition.Assets.Single(a => a.Id == asset.Id);
                var path = ProjectPathPolicy.Normalize(next.RootDirectory, updated.RelativePath);
                var reread = TestDocumentCodec.Deserialize(new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false)));
                return (Context: next, Session: new TestEditorSession(next.Definition.Id,
                    next.ManifestPath, asset.Id, updated.Revision, asset.Name, path, updated.Sha256, reread));
            }, ct);
            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>New formal execution Project, holding references to existing Packages only.</summary>
    public Task<AssetEntry> CreateTestProjectAsync(string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        return AddAssetAsync(TestProjectKind, name, ".prj.json",
            TestProjectCodec.Encode(TestProjectDocument.Create(name)), ct);
    }

    public async Task<TestProjectEditorSession> OpenTestProjectEditorAsync(
        AssetId id, CancellationToken ct = default)
    {
        var current = Current ?? throw new InvalidOperationException("请先打开工作空间。");
        var asset = current.Definition.Assets.SingleOrDefault(x => x.Id == id &&
            x.Kind == TestProjectKind)
            ?? throw new ProjectDataException("PROJECT_PLAN_MISSING", "测试项目资源不存在。");
        var path = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new TestProjectEditorSession(current.Definition.Id, current.ManifestPath,
            asset.Id, asset.Revision, asset.Name, path, hash, TestProjectCodec.Decode(bytes));
    }

    /// <summary>Transactional, content-hashed project-plan save without executing packages.</summary>
    public async Task<TestProjectEditorSession> SaveTestProjectEditorAsync(
        TestProjectEditorSession opened, TestProjectDocument document,
        CancellationToken ct = default)
    {
        var bytes = TestProjectCodec.Encode(document);
        if (document.Id != opened.Document.Id)
            throw new ProjectDataException("PROJECT_PLAN_ID", "测试项目持久身份不允许改变。");
        if (string.Equals(opened.FileSha256,
            Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            return opened;
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工作空间已关闭。");
            if (current.Definition.Id != opened.ProjectId ||
                !string.Equals(current.ManifestPath, opened.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("PROJECT_PLAN_SWITCH", "工作空间已切换。");
            var index = Array.FindIndex(current.Definition.Assets.ToArray(), x => x.Id == opened.AssetId);
            if (index < 0) throw new ProjectDataException("PROJECT_PLAN_MISSING", "测试项目已被移除。");
            var asset = current.Definition.Assets[index];
            if (asset.Kind != TestProjectKind)
                throw new ProjectDataException("PROJECT_PLAN_CONFLICT", "测试项目版本已变化，请重新打开。");
            var result = await Task.Run(async () =>
            {
                var next = await SaveEditedResourceAsync(current, asset, bytes, opened.FileSha256, ct).ConfigureAwait(false);
                var updated = next.Definition.Assets.Single(a => a.Id == asset.Id);
                var path = ProjectPathPolicy.Normalize(next.RootDirectory, updated.RelativePath);
                var reread = TestProjectCodec.Decode(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false));
                return (Context: next, Session: new TestProjectEditorSession(next.Definition.Id,
                    next.ManifestPath, asset.Id, updated.Revision, asset.Name, path, updated.Sha256, reread));
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
        // Each ECU engineering project has ONE active DBC. Importing a new
        // verified database replaces the bytes of that same asset, keeping its
        // stable identity; the revision advances so existing TEST/WAVE pins
        // must be explicitly reviewed instead of silently switching signals.
        var before = Current ?? throw new InvalidOperationException("请先打开工程。");
        var previous = before.Definition.Assets.Where(a => a.Kind == DbcKind).ToArray();
        if (previous.Length > 1)
            throw new InvalidDataException(
                "该旧工程含多份 DBC，无法自动猜测哪份生效。请先整理为唯一 DBC。");
        if (previous.Length == 0)
            return await AddAssetAsync(DbcKind, name, ".dbc", bytes, ct);

        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("工程已关闭。");
            var selected = current.Definition.Assets.Where(a => a.Kind == DbcKind).ToArray();
            if (selected.Length != 1 || selected[0].Id != previous[0].Id)
                throw new ProjectDataException("DBC_PROJECT_CHANGED",
                    "导入期间当前工程的 DBC 已改变，请重新选择文件。");
            var context = await SaveEditedResourceAsync(
                current, selected[0], bytes, selected[0].Sha256, ct);
            // A failed manifest/file validation never exposes the replacement
            // as the active engineering source.
            var updated = context.Definition.Assets.Single(a => a.Id == selected[0].Id);
            var persisted = ProjectPathPolicy.Normalize(context.RootDirectory, updated.RelativePath);
            var document = await DbcDocumentStore.OpenAsync(persisted, CancellationToken.None);
            if (!string.Equals(document.Receipt.Sha256, updated.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("DBC_IMPORT_VERIFY",
                    "替换 DBC 后的文件与工程索引不一致。");
            Current = context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return updated;
        }
        finally { _writeLock.Release(); }
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
        // Destructive R1 authoring contract: the ECU project owns one TEST,
        // one DBC and one native FLASH. Scanner routes and fixture control
        // never become project assets or a second recipe/configuration.
        if (kind is TestConfigurationKind or BenchConfigurationKind or
                    ScanSettingsKind or FlashSettingsKind or InspectionKind)
            throw new ProjectDataException("LEGACY_CONFIGURATION_RETIRED",
                "旧工程内扫码配方、夹具工艺、TCF/TBC 和重复 FLASH 配置已废弃。" +
                "当前工程只维护 TEST、唯一 DBC、波形和原生烧录工艺。");
        if (!ProjectConfigurationCodec.IsConfiguration(kind))
            throw new ArgumentException("不支持的配置资源类型：" + kind);
        var extension = kind switch
        {
            TestConfigurationKind => ".tcf.json",
            BenchConfigurationKind => ".tbc.json",
            InspectionKind => ".inspection.json",
            _ => ".settings.json"
        };
        return AddAssetAsync(kind, name, extension,
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
            var decoded = ProjectConfigurationCodec.Decode(asset.Kind, bytes);
            return new ProjectConfigurationSession(current.Definition.Id, current.ManifestPath,
                asset.Id, asset.Revision, asset.Name, asset.Kind, path, sha, decoded);
        }, ct);
    }

    /// <summary>
    /// Selection is a functional editing context. It does NOT connect USB, assert IGN,
    /// change KEY_SIM outputs, or authorize a test. TBC always describes the fixed station.
    /// </summary>
    public async Task SelectConfigurationAsync(AssetId id, CancellationToken ct = default)
    {
        var session = await OpenConfigurationAsync(id, ct);
        var project = Current ?? throw new InvalidOperationException("请先打开工程。");
        ValidateConfigurationReferences(session.Kind, session.Document, project.Definition);
        if (session.Kind is not (TestConfigurationKind or BenchConfigurationKind))
            throw new ArgumentException("只能选择 TCF 被测系统或 TBC 台架配置。");
        if (!string.Equals(_configurationProjectPath, project.ManifestPath,
                StringComparison.OrdinalIgnoreCase))
        {
            _selectedTestConfiguration = null;
            _selectedBenchConfiguration = null;
        }
        _configurationProjectPath = project.ManifestPath;
        if (session.Kind == TestConfigurationKind) _selectedTestConfiguration = id;
        else _selectedBenchConfiguration = id;
        SelectedConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TestSystemConfigurationDocument?> ReadSelectedTestConfigurationAsync(
        CancellationToken ct = default)
    {
        if (SelectedTestConfiguration is not { } id) return null;
        var selected = await OpenConfigurationAsync(id, ct);
        if (selected.Kind != TestConfigurationKind)
            throw new ProjectDataException("TCF_SELECTION_INVALID", "当前 TCF 资源类型已变化。");
        var document = (TestSystemConfigurationDocument)selected.Document;
        ValidateConfigurationReferences(selected.Kind, document, Current!.Definition);
        return document;
    }

    /// <summary>Prevent a config from silently following a different version of its referenced resource.</summary>
    private static void ValidateConfigurationReferences(string kind, object document, ProjectDefinition project)
    {
        void Check(Guid? id, string expectedKind, int? revision, string? fingerprint)
        {
            if (!id.HasValue) return;
            var asset = project.Assets.FirstOrDefault(x => x.Id.Value == id.Value &&
                x.Kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
                throw new ProjectDataException("CONFIG_ASSET_STALE",
                    "引用资源已不存在或发生版本变更，请重新选择并确认：" + expectedKind);
        }

        if (kind == TestConfigurationKind && document is TestSystemConfigurationDocument tcf)
        {
            Check(tcf.DbcAssetId, DbcKind, tcf.DbcRevision, tcf.DbcFingerprint);
            Check(tcf.ButtonAssetId, ControllerKind, tcf.ButtonRevision, tcf.ButtonFingerprint);
        }
        else if (kind == WaveSettingsKind && document is WaveSettingsDocument wave)
        {
            Check(wave.DbcAssetId, DbcKind, wave.DbcAssetRevision, wave.DbcAssetFingerprint);
            Check(wave.EvidenceAssetId, WaveKind, wave.EvidenceAssetRevision, wave.EvidenceAssetFingerprint);
        }
        else if (kind == FlashSettingsKind && document is FlashSettingsDocument flash)
            Check(flash.FirmwareAssetId, FirmwareKind, flash.FirmwareRevision, flash.FirmwareFingerprint);
        else if (kind == ScanSettingsKind && document is ScanMatchSettingsDocument scan && scan.Published)
            foreach (var recipe in scan.Recipes.Where(x => x.Published))
            {
                Check(recipe.TestProgramAssetId, TestProgramKind,
                    recipe.TestProgramRevision, recipe.TestProgramFingerprint);
                Check(recipe.DbcAssetId, DbcKind, recipe.DbcRevision, recipe.DbcFingerprint);
                Check(recipe.FirmwareAssetId, FirmwareKind,
                    recipe.FirmwareRevision, recipe.FirmwareFingerprint);
            }
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
            if (!string.Equals(asset.Kind, opened.Kind, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("CONFIG_REVISION_CONFLICT",
                    "工程配置已经更新，请重新打开再编辑。");

            var result = await Task.Run(async () =>
            {
                var next = await SaveEditedResourceAsync(current, asset, bytes, opened.FileSha256, ct).ConfigureAwait(false);
                var updated = next.Definition.Assets.Single(a => a.Id == asset.Id);
                var path = ProjectPathPolicy.Normalize(next.RootDirectory, updated.RelativePath);
                var reread = ProjectConfigurationCodec.Decode(asset.Kind,
                    await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false));
                return (Context: next, Session: new ProjectConfigurationSession(next.Definition.Id,
                    next.ManifestPath, asset.Id, updated.Revision, asset.Name, asset.Kind,
                    path, updated.Sha256, reread));
            }, ct);
            Current = result.Context;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return result.Session;
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Create this ECU project's only active DBC directly in AutoTest.</summary>
    public Task<AssetEntry> CreateDbcAsync(string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        var empty = DbcParserAdapter.Export(DbcDatabase.Create(name));
        _ = DbcWorkbenchDocument.Import(name, empty);
        return AddAssetAsync(DbcKind, name, ".dbc", Encoding.UTF8.GetBytes(empty), ct);
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
                // There is one authored execution order for one ECU project.
                // Keep this at the storage boundary as well as in the UI:
                // concurrent callers must never create a second TEST project.
                if (kind == TestProjectKind && current.Definition.Assets.Any(x =>
                    x.Kind == TestProjectKind))
                    throw new InvalidDataException(
                        "一个 ECU 工程只能有一份完整 TEST 项目。请直接打开并编辑已有 TEST。");
                if (kind == DbcKind && current.Definition.Assets.Any(x => x.Kind == DbcKind))
                    throw new InvalidDataException(
                        "一个 ECU 工程只允许一份当前生效 DBC。请使用替换 DBC 操作。");
                if (current.Definition.Assets.Any(x =>
                    x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("同类资源中已存在同名资源，请先重命名。");

                var folder = kind switch
                {
                    DbcKind => "dbc",
                    TestProgramKind => "tests",
                    PublicFlowKind => "flows",
                    TestProjectKind => "projects",
                    TestConfigurationKind => "tcf",
                    BenchConfigurationKind => "tbc",
                    InspectionKind => "inspection",
                    FirmwareKind => "firmware",
                    WaveKind => "wave",
                    ControllerKind => "controller",
                    KeyActionsKind => "key-actions",
                    WaveSettingsKind => "wave",
                    FlashSettingsKind => "flash",
                    ScanSettingsKind => "scan",
                    _ => throw new ArgumentException("不支持的工程资源类型：" + kind)
                };
                // DBC is a real user-managed database file, not a GUID-named
                // opaque asset. Other internal definitions keep stable paths.
                var relative = kind == DbcKind
                    ? "assets/dbc/" + name + ".dbc"
                    : "assets/" + folder + "/" + Guid.NewGuid().ToString("N") + extension;
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

    /// <summary>
    /// Rename the ECU project itself. The internal manifest filename and
    /// resource locations remain stable; editor sessions and exported bundles
    /// continue to refer to the same project identity.
    /// </summary>
    public async Task RenameProjectAsync(string name, CancellationToken ct = default)
    {
        ValidateResourceName(name);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ?? throw new InvalidOperationException("请先打开工程。");
            if (string.Equals(current.Definition.Name, name, StringComparison.Ordinal))
                return;
            await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
            var renamed = (current.Definition with { Name = name })
                .WithRevision().Validate();
            await ProjectRepository.SaveDefinitionAsync(renamed, current.ManifestPath, ct)
                .ConfigureAwait(false);
            var verified = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath, ct)
                .ConfigureAwait(false);
            var hash = await HashFileAsync(current.ManifestPath, ct).ConfigureAwait(false);
            Current = new OpenProjectContext(verified, current.ManifestPath, hash);
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _writeLock.Release(); }
    }

    public async Task RenameAssetAsync(AssetId id, string name,
        CancellationToken ct = default)
    {
        ValidateResourceName(name);
        var target = Current?.Definition.Assets.FirstOrDefault(a => a.Id == id);
        if (target?.Kind != DbcKind)
        {
            await EditDefinitionAsync(editor => editor.Rename(id, name), ct);
            return;
        }

        // For DBC, rename the actual .dbc file as well as its display name.
        // Its stable AssetId, content revision and fingerprint do not change:
        // no TEST signal interpretation changes because of a filename.
        var stem = name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase)
            ? name[..^4] : name;
        ValidateResourceName(stem);
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = Current ??
                throw new InvalidOperationException("工程已经关闭。");
            var index = Array.FindIndex(current.Definition.Assets.ToArray(), a => a.Id == id);
            if (index < 0 || current.Definition.Assets[index].Kind != DbcKind)
                throw new ProjectDataException("DBC_NOT_FOUND",
                    "当前工程 DBC 已变化，请重新打开。");
            var asset = current.Definition.Assets[index];
            var updatedRel = "assets/dbc/" + stem + ".dbc";
            if (asset.Name == stem &&
                string.Equals(asset.RelativePath, updatedRel, StringComparison.Ordinal))
                return;

            await EnsureManifestUnchangedAsync(current, ct).ConfigureAwait(false);
            var previousPath = ProjectPathPolicy.Normalize(current.RootDirectory, asset.RelativePath);
            var nextPath = ProjectPathPolicy.Normalize(current.RootDirectory, updatedRel);
            var moved = !string.Equals(previousPath, nextPath,
                StringComparison.OrdinalIgnoreCase);
            if (moved && File.Exists(nextPath))
                throw new IOException("工程中已经存在同名 DBC 文件，不能覆盖。");
            if (!File.Exists(previousPath))
                throw new FileNotFoundException("原 DBC 文件不存在，无法重命名。", previousPath);

            var nextAsset = asset with
            {
                Name = stem,
                RelativePath = updatedRel
            };
            var definition = (current.Definition with
            {
                Assets = current.Definition.Assets.SetItem(index, nextAsset)
            }).WithRevision().Validate();

            var committed = false;
            try
            {
                if (moved) File.Move(previousPath, nextPath);
                await ProjectRepository.SaveDefinitionAsync(definition,
                    current.ManifestPath, ct).ConfigureAwait(false);
                committed = true;
                var reopened = await ProjectRepository.OpenDefinitionAsync(current.ManifestPath,
                    CancellationToken.None).ConfigureAwait(false);
                var hash = await HashFileAsync(current.ManifestPath, CancellationToken.None)
                    .ConfigureAwait(false);
                Current = new OpenProjectContext(reopened, current.ManifestPath, hash);
            }
            catch
            {
                // Only roll back if the manifest itself was not committed.
                if (!committed && moved && File.Exists(nextPath) &&
                    !File.Exists(previousPath))
                    File.Move(nextPath, previousPath);
                throw;
            }
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _writeLock.Release(); }
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
