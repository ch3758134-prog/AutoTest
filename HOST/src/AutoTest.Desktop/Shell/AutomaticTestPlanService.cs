using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Shell;

/// <summary>One real test case from the operator's independently selected ECU project.</summary>
public sealed record AutomaticTestCase(string Index, string Name, TestDocumentId DocumentId,
    string State = "未执行");

public sealed record AutomaticTestPlan(OpenProjectContext Project,
    IReadOnlyList<AutomaticTestCase> Cases);

/// <summary>
/// The AUTOMATIC TEST plan is NOT the active project in the developer workbench.
/// Its selection is saved in a separate operator-local setting, verified on each
/// restore, and never modifies ProjectWorkspaceService.Current or recent IDE files.
/// No hardware command, test execution or fake evidence is produced here.
/// </summary>
public sealed class AutomaticTestPlanService
{
    private sealed record LocalSettings(int Version, string ManifestPath);
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "automatic-test-plan.json");

    public AutomaticTestPlan? Current { get; private set; }
    public string Issue { get; private set; } = "尚未选择自动测试方案";
    public string? ConfiguredPath { get; private set; }
    public event EventHandler? Changed;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = null;
                ConfiguredPath = null;
                Issue = "尚未配置自动测试方案";
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            var settings = JsonSerializer.Deserialize<LocalSettings>(
                await File.ReadAllTextAsync(SettingsPath, ct))
                ?? throw new InvalidDataException("自动测试方案设置为空");
            if (settings.Version != 1 || string.IsNullOrWhiteSpace(settings.ManifestPath))
                throw new InvalidDataException("自动测试方案设置格式无效");
            ConfiguredPath = settings.ManifestPath;
            Current = await VerifyAsync(settings.ManifestPath, ct);
            Issue = "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      InvalidDataException or ArgumentException or
                                      NotSupportedException or JsonException or FormatException or
                                      ProjectDataException)
        {
            Current = null;
            Issue = "已配置的自动测试方案不可用：" + error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SelectAsync(string path, CancellationToken ct = default)
    {
        // First validate the new selection. An invalid file must not overwrite
        // the last known-good operator setup.
        try
        {
            var verified = await VerifyAsync(path, ct);
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            var pending = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(new LocalSettings(1, full));
                await File.WriteAllTextAsync(pending, json, Encoding.UTF8, ct);
                File.Move(pending, SettingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(pending))
                    try { File.Delete(pending); }
                    catch (IOException) { /* Failed deletion never means the plan was selected. */ }
            }
            ConfiguredPath = full;
            Current = verified;
            Issue = "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      InvalidDataException or ArgumentException or
                                      NotSupportedException or JsonException or FormatException or
                                      ProjectDataException)
        {
            Issue = "无法使用所选自动测试方案：" + error.Message;
            // Preserve Current and ConfiguredPath; no speculative switching.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<AutomaticTestPlan> VerifyAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("请选择自动测试方案文件");
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("测试方案文件不存在", full);
        var definition = await ProjectRepository.OpenDefinitionAsync(full, ct);
        await ProjectBundleContractAudit.ValidateAsync(definition, full, ct);
        var manifestSha = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(full, ct)));
        var project = new OpenProjectContext(definition, full, manifestSha);
        var testAssets = definition.Assets
            .Where(a => a.Kind == ProjectWorkspaceService.TestProjectKind).ToArray();
        if (testAssets.Length == 0)
            throw new InvalidDataException("方案没有测试项目，请选择含测试用例的真实工程");
        if (testAssets.Length > 1)
            throw new InvalidDataException("方案含多个测试项目，尚未确定唯一执行集合");
        var testBytes = await ReadAssetAsync(project, testAssets[0], ct);
        var testProject = TestProjectCodec.Decode(testBytes);
        if (testProject.Packages.Count == 0)
            throw new InvalidDataException("测试项目没有关联用例");
        // The complete operator list and the real production execution plan
        // MUST use the same entry projection. RunSelected is debug-only; the
        // persisted legacy Enabled=false means explicitly excluded, not
        // "missing another enable click" after adding a new case.
        var included = testProject.AutomaticRunEntries();
        if (included.Count == 0)
            throw new InvalidDataException(
                "工程已有测试用例，但全部被旧项目配置排除在自动测试外。"
                + "需要一次性核对原有排除设置，不会自动恢复真实硬件操作。");
        var cases = new List<AutomaticTestCase>(included.Count);
        foreach (var entry in included)
        {
            var matches = definition.Assets.Where(a =>
                a.Id.Value == entry.PackageAssetId &&
                a.Kind == ProjectWorkspaceService.TestProgramKind).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("测试用例资源缺失或重复：" + entry.DisplayName);
            var asset = matches[0];
            if (asset.Revision != entry.PackageRevision ||
                !asset.Sha256.Equals(entry.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("测试用例资源版本不一致：" + entry.DisplayName);
            var content = await ReadAssetAsync(project, asset, ct);
            var document = TestDocumentCodec.Deserialize(new UTF8Encoding(false, true).GetString(content));
            cases.Add(new((cases.Count + 1).ToString("D2"), entry.DisplayName, document.Id));
        }
        return new AutomaticTestPlan(project, cases.AsReadOnly());
    }

    private static async Task<byte[]> ReadAssetAsync(OpenProjectContext context,
        AutoTest.Projects.Resources.AssetEntry asset, CancellationToken ct)
    {
        var file = ProjectPathPolicy.Normalize(context.RootDirectory, asset.RelativePath);
        var content = await File.ReadAllBytesAsync(file, ct);
        if (!asset.VerifyHash(content))
            throw new InvalidDataException("测试资源完整性校验失败：" + asset.RelativePath);
        return content;
    }
}
