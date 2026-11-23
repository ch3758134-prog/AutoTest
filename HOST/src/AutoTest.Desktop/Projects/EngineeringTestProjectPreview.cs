using System.IO;
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

public static class EngineeringTestProjectPreview
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
            var referenceCurrent = actual is not null &&
                actual.Revision == entry.PackageRevision &&
                string.Equals(actual.Sha256, entry.PackageSha256,
                    StringComparison.OrdinalIgnoreCase);
            return new EngineeringTestCasePreview(entry.PackageAssetId,
                entry.DisplayName, entry.Enabled, referenceCurrent);
        }).ToArray();

        try
        {
            var checkedScope = await AutomaticTestResourceContract.ResolveAsync(
                project, ct).ConfigureAwait(false);
            return new(project, displayed, checkedScope.Issues.Count == 0
                    ? "已读取 TEST 项目；实体运行仍须满足真实硬件和资源条件。"
                    : "TEST 还有 " + checkedScope.Issues.Count +
                      " 项运行前问题；可进入测试项目或对应步骤处理。",
                checkedScope.Issues.Count);
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
