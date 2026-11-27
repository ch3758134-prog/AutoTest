using System.IO;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Read-only projection of an engineer's currently opened ECU TEST project.
/// This is intentionally NOT a station deployment, a production job, or a
/// release: viewing/editing a project must never require scanner/FLASH/fixture
/// setup or authorize any physical output.
/// </summary>
public sealed record EngineeringTestCasePreview(
    Guid PackageAssetId, string Name, bool Included, bool ReferenceCurrent);

public sealed record EngineeringTestProjectPreview(
    OpenProjectContext Project,
    IReadOnlyList<EngineeringTestCasePreview> Cases,
    string Description,
    int IssueCount)
{
    public bool HasTestProject => Cases.Count > 0;
}

public static class EngineeringTestProjectPreviewReader
{
    public static async Task<EngineeringTestProjectPreview> ReadAsync(
        OpenProjectContext project, CancellationToken ct = default)
    {
        var projects = project.Definition.Assets
            .Where(x => x.Kind == ProjectWorkspaceService.TestProjectKind).ToArray();
        if (projects.Length != 1)
            return new(project, [],
                projects.Length == 0
                    ? "当前工程尚未创建 TEST 项目，可进入开发与调试建立。"
                    : "工程存在多个 TEST 项目；请在工程中确定正式的一个。",
                projects.Length == 0 ? 0 : projects.Length);

        // Display the actual ordered TEST entries even if later real-execution
        // validation finds missing mappings. Fail-closed on malformed data.
        var bytes = await AutomaticTestResourceContract.ReadPinnedAsync(
            project, projects[0], ct).ConfigureAwait(false);
        var tree = TestProjectCodec.Decode(bytes);
        var displayed = tree.Packages.Select(entry =>
        {
            var actual = project.Definition.Assets.FirstOrDefault(x =>
                x.Kind == ProjectWorkspaceService.TestProgramKind &&
                x.Id.Value == entry.PackageAssetId);
            var referenceCurrent = actual is not null;
            return new EngineeringTestCasePreview(entry.PackageAssetId,
                entry.DisplayName, entry.Enabled, referenceCurrent);
        }).ToArray();

        try
        {
            var checkedScope = await AutomaticTestResourceContract.ResolveAsync(
                project, ct).ConfigureAwait(false);
            var blockers = new List<string>(checkedScope.Issues);
            // Engineering TEST authoring does not own the station scanner,
            // barcode marker, fixture outputs, physical START or RECORD gate.
            // Those are checked by production when a real DUT is admitted.
            // A draft may be edited or debugged before native FLASH is ready.
            var flash = await NativeFlashProfileStore.LoadAsync(project.RootDirectory, ct)
                .ConfigureAwait(false);
            if (flash is null)
                blockers.Add("正式生产的前置 FLASH 尚未配置（不影响 TEST 编辑）。");
            else
            {
                if (!string.IsNullOrWhiteSpace(project.Definition.EcuModelCode) &&
                    !string.Equals(flash.Profile.EcuModelCode,
                        project.Definition.EcuModelCode, StringComparison.Ordinal))
                    blockers.Add("烧录工艺型号与工程产品身份不一致。");
                else
                    _ = NativeFlashJobBuilder.Freeze(flash.Profile, project.RootDirectory);
            }
            return new(project, displayed, blockers.Count == 0
                ? "工程 TEST 与产品资源已核对；正式生产仍需工位真实验收。"
                : "配置有 " + blockers.Count + " 项未完成：" +
                  string.Join("；", blockers.Take(4)), blockers.Count);
        }
        catch (Exception error) when (error is IOException or
            InvalidDataException or ProjectDataException or
            UnauthorizedAccessException or ArgumentException or FormatException or
            NotSupportedException)
        {
            return new(project, displayed,
                "工程可以浏览，但运行资源检查未通过：" + error.Message, 1);
        }
    }
}
