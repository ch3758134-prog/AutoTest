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
    IReadOnlyList<AutomaticTestCase> Cases)
{
    public IReadOnlyList<string> ResourceIssues { get; init; } = [];
    public bool ResourceReady => Cases.Count > 0 && ResourceIssues.Count == 0;
}

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
            Issue = string.Join("；", Current.ResourceIssues);
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
            Issue = string.Join("；", verified.ResourceIssues);
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
        var manifestSha = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(full, ct)));
        var project = new OpenProjectContext(definition, full, manifestSha);
        // Open incomplete projects for review, but do not authorize execution.
        // The production preflight uses the same exact TEST resource closure.
        var resolved = await AutomaticTestResourceContract.ResolveAsync(project, ct);
        var cases = resolved.Cases.Select((item, i) =>
            new AutomaticTestCase((i + 1).ToString("D2"),
                item.Entry.DisplayName, item.Program.Id)).ToArray();
        return new AutomaticTestPlan(project, cases)
        {
            ResourceIssues = resolved.Issues
        };
    }

}
