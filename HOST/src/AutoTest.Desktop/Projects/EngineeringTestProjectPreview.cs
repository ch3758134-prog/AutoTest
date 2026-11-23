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
            var blockers = new List<string>(checkedScope.Issues);
            // Read existing configuration, without creating a new recipe.
            // This is a diagnosis of a draft, never a physical run admission.
            if (string.IsNullOrWhiteSpace(project.Definition.EcuModelCode))
                blockers.Add("工程尚未设置用于烧录匹配的 ECU 型号");
            var scannerPolicy = await StationScanIdentityPolicyStore.LoadAsync(ct)
                .ConfigureAwait(false);
            if (scannerPolicy is null)
                blockers.Add("固定扫码枪的唯一编号解析规则尚未保存");
            var flash = await NativeFlashPublicationStore.LoadPublishedAsync(
                project.RootDirectory, ct).ConfigureAwait(false);
            if (flash is null)
                blockers.Add("当前工程的前置烧录工艺尚未发布");
            else
            {
                if (!string.Equals(flash.Profile.EcuModelCode,
                        project.Definition.EcuModelCode, StringComparison.Ordinal))
                    blockers.Add("烧录工艺型号与当前工程不一致");
                else
                    _ = NativeFlashPublicationStore.FreezePublished(
                        project.RootDirectory, flash);
            }
            var fixtures = project.Definition.Assets.Where(x =>
                x.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
            if (fixtures.Length > 1)
                blockers.Add("工程存在多份夹具工艺，无法确定本次应使用哪一份");
            else if (fixtures.Length == 1)
            {
                var data = await AutomaticTestResourceContract.ReadPinnedAsync(
                    project, fixtures[0], ct).ConfigureAwait(false);
                var fixture = (InspectionStationProcedureDocument)
                    ProjectConfigurationCodec.Decode(fixtures[0].Kind, data);
                fixture.Validate();
            }
            return new(project, displayed, blockers.Count == 0
                ? "静态检测配置已核对；仍需真实工位连接及执行链验收。"
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
