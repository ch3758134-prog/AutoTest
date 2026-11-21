using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Projects;

public sealed record ProjectScanMatchResult(string Code, string? RecipeId, string? Identity,
    string Explanation, ScanRecipePreparationService.Preview? Preview = null)
{
    public bool Accepted => Code == "CONFIG_MATCHED";
}

/// <summary>Actual keyboard-wedge ingress -> published rule set -> pinned project asset references.
/// Matching only; never starts TEST, physical outputs or flashing.</summary>
public sealed class ProjectScanMatcher(ProjectWorkspaceService workspace)
{
    /// <summary>
    /// All identity dimensions matter. Two unrelated projects often have the
    /// same initial Revision, so revision alone cannot authorize a match.
    /// </summary>
    public static bool SameProjectSnapshot(OpenProjectContext expected, OpenProjectContext? current) =>
        current is not null &&
        current.Definition.Id == expected.Definition.Id &&
        current.Definition.Revision == expected.Definition.Revision &&
        string.Equals(current.ManifestPath, expected.ManifestPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.ManifestSha256, expected.ManifestSha256, StringComparison.OrdinalIgnoreCase);

    public async Task<ProjectScanMatchResult> MatchAsync(BarcodeScanEvent scan,
        CancellationToken ct = default)
    {
        var snapshot = workspace.Current;
        if (snapshot is null)
            return new("PROJECT_NOT_OPEN", null, null, "请先打开工程再配置扫码匹配。");
        var candidates = snapshot.Definition.Assets.Where(x =>
            x.Kind.Equals(ProjectWorkspaceService.ScanSettingsKind, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            return new("SCAN_RULES_NOT_CONFIGURED", null, null, "扫码已采集；当前工程还没有扫码匹配配置。");

        var published = new List<ScanMatchSettingsDocument>();
        foreach (var asset in candidates)
        {
            var session = await workspace.OpenConfigurationAsync(asset.Id, ct).ConfigureAwait(false);
            if (!SameProjectSnapshot(snapshot, workspace.Current))
                return new("SCAN_PROJECT_CHANGED", null, null,
                    "读取扫码配置期间工程身份或文件指纹变化；必须重新扫码。");
            var doc = (ScanMatchSettingsDocument)session.Document;
            if (doc.Published) published.Add(doc);
        }
        if (published.Count == 0)
            return new("SCAN_RULES_NOT_PUBLISHED", null, null, "扫码已采集；没有已发布的匹配配置。");
        if (published.Count > 1)
            return new("SCAN_CONFIG_AMBIGUOUS", null, null, "发现多个已发布扫码配置，无法自动选择。");
        if (!SameProjectSnapshot(snapshot, workspace.Current))
            return new("SCAN_PROJECT_CHANGED", null, null,
                "扫码期间工程身份、修订或文件指纹变化，请重新扫描。");

        var document = published[0];
        document.Validate();
        var rules = document.ToRules();
        var available = snapshot.Definition.Assets;
        AssetRef RequireAsset(Guid id, string kind, int? expectedRevision, string? expectedSha256)
        {
            var asset = available.FirstOrDefault(x => x.Id.Value == id &&
                x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
            if (asset is null || asset.Revision != expectedRevision ||
                !string.Equals(asset.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("SCAN_ASSET_STALE",
                    "发布配方引用的工程资源版本不一致，请回到配置编辑器重新确认资源：" + kind);
            return asset.ToReference();
        }
        var recipes = document.Recipes.Where(x => x.Published).Select(x =>
        {
            if (x.TestProgramAssetId is null && x.TestProjectAssetId is null)
                throw new ProjectDataException("SCAN_RECIPE_INVALID",
                    "发布配方没有选择测试项目或测试程序。");
            var assets = ImmutableArray.CreateBuilder<AssetRef>();
            if (x.TestProgramAssetId is Guid testId)
                assets.Add(RequireAsset(testId, ProjectWorkspaceService.TestProgramKind,
                    x.TestProgramRevision, x.TestProgramFingerprint));
            if (x.TestProjectAssetId is Guid projectId)
                assets.Add(RequireAsset(projectId, ProjectWorkspaceService.TestProjectKind,
                    x.TestProjectRevision, x.TestProjectFingerprint));
            if (x.FlashSettingsAssetId is Guid settingsId)
                assets.Add(RequireAsset(settingsId, ProjectWorkspaceService.FlashSettingsKind,
                    x.FlashSettingsRevision, x.FlashSettingsFingerprint));
            if (x.DbcAssetId is Guid dbcId)
                assets.Add(RequireAsset(dbcId, ProjectWorkspaceService.DbcKind,
                    x.DbcRevision, x.DbcFingerprint));
            if (x.FirmwareAssetId is Guid firmwareId)
                assets.Add(RequireAsset(firmwareId, ProjectWorkspaceService.FirmwareKind,
                    x.FirmwareRevision, x.FirmwareFingerprint));
            else if (x.RequiresFlash)
                throw new ProjectDataException("SCAN_RECIPE_INVALID", "要求烧录的配方没有固件。");
            return new ProductionRecipe(x.RecipeId, 1,
                string.IsNullOrWhiteSpace(x.Model) ? null : x.Model,
                string.IsNullOrWhiteSpace(x.Customer) ? null : x.Customer,
                assets.ToImmutable(), x.RequiresFlash, true);
        }).ToArray();

        var revision = new ProjectRevision(snapshot.Definition.Revision);
        var matching = new ConfigMatchService(rules, revision, recipes);
        var result = matching.Evaluate(scan.Scan, new RuleRevision(rules.Fingerprint), revision);
        ct.ThrowIfCancellationRequested();
        if (!SameProjectSnapshot(snapshot, workspace.Current))
            return new("SCAN_PROJECT_CHANGED", null, null,
                "扫码匹配完成前工程已切换或资源已修改，旧结果作废。");
        if (!result.Decision.Allowed)
            return new(result.Decision.RejectionCode ?? "CONFIG_REJECTED", null, null,
                "扫码已采集，但规则或工程资源条件不符合：" +
                (result.Decision.RejectionCode ?? "未知原因"));
        var selected = document.Recipes.Single(r => r.Published &&
            string.Equals(r.RecipeId, result.RecipeId, StringComparison.OrdinalIgnoreCase));
        try
        {
            var ready = await ScanRecipePreparationService.InspectAsync(
                workspace, snapshot, selected, requireFrozenJob: true, ct);
            if (!SameProjectSnapshot(snapshot, workspace.Current))
                return new("SCAN_PROJECT_CHANGED", null, null,
                    "扫码准备期间工程发生变化，请重新扫码。");
            return new("CONFIG_MATCHED", selected.RecipeId, result.Decision.DutIdentity,
                ready.Summary, ready);
        }
        catch (Exception error) when (error is ProjectDataException or IOException or
                                       InvalidDataException or FormatException or
                                       NotSupportedException or UnauthorizedAccessException)
        {
            return new("SCAN_RECIPE_PRECHECK", selected.RecipeId,
                result.Decision.DutIdentity,
                "已识别编号，但配置还不能用于生产准备：" + error.Message);
        }
    }
}
