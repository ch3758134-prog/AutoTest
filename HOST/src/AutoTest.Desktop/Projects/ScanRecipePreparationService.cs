using AutoTest.Core;
using AutoTest.Desktop.Testing;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Read-only production-recipe preparation. The existing TEST Flash step is
/// the ONLY executable programming entry; a scanner never writes ECU CAN.
/// This checks the exact on-disk CanTool bundle before a recipe is published
/// and again when a scan tries to select that published recipe.
/// </summary>
public static class ScanRecipePreparationService
{
    public sealed record Preview(
        string RecipeId, string TestScope, IReadOnlyList<string> Cases,
        string FlashPolicy, string? FirmwareReleaseLabel, string? FirmwareName,
        string? FlashSettingsName, int? TargetEcuCanKbps, string? FlashJobFingerprint,
        Guid? TestProjectAssetId = null, Guid? TestProgramAssetId = null,
        IReadOnlyList<Guid>? CaseEntryIds = null)
    {
        public string Summary =>
            "TEST: " + TestScope + " · " + Cases.Count + " 个已启用用例" +
            " · 烧录: " + (FlashPolicy == ScanFlashPolicy.FlashRequired ? "必须烧录" : "不烧录") +
            (FlashPolicy == ScanFlashPolicy.FlashRequired
                ? " · 固件 " + FirmwareReleaseLabel + " / " + FirmwareName +
                  " · 工艺 " + FlashSettingsName +
                  " · CAN " + TargetEcuCanKbps + "k" +
                  " · Job SHA " + FlashJobFingerprint![..12] + "…"
                : (TargetEcuCanKbps.HasValue ? " · CAN " + TargetEcuCanKbps + "k" : "")) +
            "；仅为配置预检，未执行测试或烧录。";
    }

    public static async Task<ScanMatchSettingsDocument> FreezePublishedAsync(
        ProjectWorkspaceService workspace, ScanMatchSettingsDocument source,
        CancellationToken ct = default)
    {
        if (!source.Published) return source;
        var snapshot = workspace.Current ?? throw new ProjectDataException(
            "SCAN_PROJECT_CHANGED", "尚未打开正式工程。");
        var recipes = new List<ScanRecipeDraft>(source.Recipes.Count);
        foreach (var recipe in source.Recipes)
        {
            ct.ThrowIfCancellationRequested();
            if (!recipe.Published) { recipes.Add(recipe); continue; }
            var checkedPlan = await InspectAsync(workspace, snapshot, recipe,
                requireFrozenJob: false, ct);
            recipes.Add(recipe with
            {
                FlashPolicy = checkedPlan.FlashPolicy,
                FlashJobFingerprint = checkedPlan.FlashJobFingerprint
            });
        }
        if (!ProjectScanMatcher.SameProjectSnapshot(snapshot, workspace.Current))
            throw new ProjectDataException("SCAN_PROJECT_CHANGED",
                "发布过程中工程发生变化，配方尚未保存。");
        return source with { Recipes = recipes };
    }

    public static async Task<Preview> InspectAsync(
        ProjectWorkspaceService workspace, OpenProjectContext snapshot,
        ScanRecipeDraft recipe, bool requireFrozenJob, CancellationToken ct = default)
    {
        void CheckProject()
        {
            ct.ThrowIfCancellationRequested();
            if (!ProjectScanMatcher.SameProjectSnapshot(snapshot, workspace.Current))
                throw new ProjectDataException("SCAN_PROJECT_CHANGED",
                    "读取测试/烧录配置期间工程已变化，请重新配置或扫码。");
        }
        void CheckPin(Guid? assetId, int? revision, string? fingerprint,
            string expectedKind)
        {
            if (assetId is not Guid id || id == Guid.Empty ||
                !ProjectConfigurationCodec.ValidPin(revision, fingerprint) ||
                !snapshot.Definition.Assets.Any(a => a.Id.Value == id &&
                    a.Kind == expectedKind && a.Revision == revision &&
                    string.Equals(a.Sha256, fingerprint, StringComparison.OrdinalIgnoreCase)))
                throw new ProjectDataException("SCAN_ASSET_STALE",
                    "烧录/测试配方引用的 " + expectedKind +
                    " 已删除或改版；请在工程配置中重新选择并发布。");
        }
        CheckProject();
        var cases = new List<(Guid AssetId, string Name, TestDocument Program)>();
        var selectedEntryIds = new List<Guid>();
        string scope;
        if (recipe.TestProjectAssetId is Guid projectId)
        {
            CheckPin(projectId, recipe.TestProjectRevision,
                recipe.TestProjectFingerprint, ProjectWorkspaceService.TestProjectKind);
            var opened = await workspace.OpenTestProjectEditorAsync(AssetId.Create(projectId), ct);
            CheckProject();
            scope = opened.Document.Name;
            foreach (var entry in opened.Document.AutomaticRunEntries())
            {
                var current = snapshot.Definition.Assets.SingleOrDefault(a =>
                    a.Id.Value == entry.PackageAssetId &&
                    a.Kind == ProjectWorkspaceService.TestProgramKind)
                    ?? throw new ProjectDataException("SCAN_TEST_MISSING",
                        "TEST 用例定义不存在：" + entry.DisplayName);
                CheckPin(current.Id.Value, current.Revision, current.Sha256,
                    ProjectWorkspaceService.TestProgramKind);
                var package = await workspace.OpenTestEditorAsync(
                    AssetId.Create(entry.PackageAssetId), ct);
                CheckProject();
                cases.Add((entry.PackageAssetId, entry.DisplayName, package.Document));
                selectedEntryIds.Add(entry.ItemId);
            }
            if (recipe.TestProgramAssetId is Guid specificId)
                CheckPin(specificId, recipe.TestProgramRevision,
                    recipe.TestProgramFingerprint, ProjectWorkspaceService.TestProgramKind);
            if (recipe.TestProgramAssetId is Guid selectedId &&
                !cases.Any(item => item.AssetId == selectedId))
                throw new ProjectDataException("SCAN_TEST_SCOPE",
                    "配方关联的单用例不在当前测试项目的已启用范围内。");
        }
        else if (recipe.TestProgramAssetId is Guid testId)
        {
            CheckPin(testId, recipe.TestProgramRevision,
                recipe.TestProgramFingerprint, ProjectWorkspaceService.TestProgramKind);
            var opened = await workspace.OpenTestEditorAsync(AssetId.Create(testId), ct);
            CheckProject();
            scope = opened.Document.Name;
            cases.Add((testId, opened.Document.Name, opened.Document));
        }
        else
            throw new ProjectDataException("SCAN_TEST_SCOPE",
                "配方必须绑定已保存的测试项目或 TEST 程序。");
        if (cases.Count == 0)
            throw new ProjectDataException("SCAN_TEST_SCOPE",
                "当前测试项目没有已启用用例，无法准备运行范围。");

        if (recipe.DbcAssetId is Guid dbcId)
            CheckPin(dbcId, recipe.DbcRevision, recipe.DbcFingerprint,
                ProjectWorkspaceService.DbcKind);

        var enabledFlash = cases.SelectMany(item => TestRunStepScope.EnabledSteps(item.Program)
                .Where(step => step.Type == "Flash")
                .Select(step => (item.Name, item.Program, Step: step)))
            .ToArray();

        var policy = recipe.EffectiveFlashPolicy;
        if (policy == "LegacyFlashUnbound")
            throw new ProjectDataException("SCAN_FLASH_POLICY",
                "旧扫码配方只勾选了“要求烧录”，未绑定具体 CanTool 工艺。请重新选择烧录策略并发布。");
        if (policy == ScanFlashPolicy.NoFlash)
        {
            if (enabledFlash.Length != 0)
                throw new ProjectDataException("SCAN_FLASH_POLICY",
                    "配方选了“不烧录”，但运行范围仍包含启用的 Flash 步骤；请修改用例或改为必须烧录。");
            return new(recipe.RecipeId, scope, cases.Select(c => c.Name).ToArray(),
                policy, recipe.FirmwareReleaseLabel, null, null,
                recipe.TargetEcuCanKbps, null, recipe.TestProjectAssetId,
                recipe.TestProgramAssetId, selectedEntryIds.ToArray());
        }

        if (policy != ScanFlashPolicy.FlashRequired || enabledFlash.Length != 1)
            throw new ProjectDataException("SCAN_FLASH_SCOPE",
                "必须烧录的配置在整个已选测试范围内必须恰好有一个启用的 Flash 步骤。");
        if (recipe.FirmwareAssetId is not Guid firmwareId ||
            recipe.FlashSettingsAssetId is not Guid flashId)
            throw new ProjectDataException("SCAN_FLASH_BUNDLE",
                "配方还没有选择目标固件或烧录工艺配置。");
        CheckPin(firmwareId, recipe.FirmwareRevision,
            recipe.FirmwareFingerprint, ProjectWorkspaceService.FirmwareKind);
        CheckPin(flashId, recipe.FlashSettingsRevision,
            recipe.FlashSettingsFingerprint, ProjectWorkspaceService.FlashSettingsKind);
        var firmware = snapshot.Definition.Assets.Single(a => a.Id.Value == firmwareId);
        var flashEntry = snapshot.Definition.Assets.Single(a => a.Id.Value == flashId);
        var session = await workspace.OpenConfigurationAsync(AssetId.Create(flashId), ct);
        CheckProject();
        var settings = (FlashSettingsDocument)session.Document;
        if (settings.FirmwareAssetId != firmwareId ||
            settings.FirmwareRevision != recipe.FirmwareRevision ||
            !string.Equals(settings.FirmwareFingerprint,
                recipe.FirmwareFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("SCAN_FLASH_BUNDLE",
                "烧录工艺配置所引用的固件与扫码配方目标固件不一致，请重新绑定。");

        var flashTest = enabledFlash[0];
        var frozenJobs = await Task.Run(() =>
            ConfiguredRealTestFlashPort.FreezeJobs(
                flashTest.Program, snapshot.RootDirectory), ct);
        CheckProject();
        if (!frozenJobs.TryGetValue(flashTest.Step.Id, out var job))
            throw new ProjectDataException("SCAN_FLASH_BUNDLE",
                "未能从 TEST 的唯一 Flash 步骤取得可执行 CanTool Job。");
        if (!string.Equals(job.FirmwareSha256, firmware.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("SCAN_FLASH_IMAGE",
                "TEST Flash 步骤中的真实 HEX/S19 内容与目标固件资源 SHA 不一致。");
        var rate = job.ConfiguredBitrate.Trim().ToUpperInvariant() switch
        {
            "250K" or "250000" => 250,
            "500K" or "500000" => 500,
            _ => throw new ProjectDataException("SCAN_FLASH_BAUD",
                "CanTool XML 波特率不在当前已验证的 250k/500k 范围内。")
        };
        if (recipe.TargetEcuCanKbps != rate)
            throw new ProjectDataException("SCAN_FLASH_BAUD",
                "扫码配方目标 ECU CAN 码率与 TEST CanTool XML 不相同。");
        if (settings.RequestCanId.HasValue || settings.ResponseCanId.HasValue)
        {
            var matchesApp = settings.RequestCanId == job.App.RequestCanId &&
                             settings.ResponseCanId == job.App.ResponseCanId;
            var matchesBoot = settings.RequestCanId == job.Boot.RequestCanId &&
                              settings.ResponseCanId == job.Boot.ResponseCanId;
            if (!matchesApp && !matchesBoot)
                throw new ProjectDataException("SCAN_FLASH_IDS",
                    "烧录配置 CAN 请求/应答端点与真实 TEST CanTool Job 的 APP、BOOT 均不相符。");
        }
        if (requireFrozenJob &&
            !string.Equals(recipe.FlashJobFingerprint, job.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("SCAN_FLASH_CHANGED",
                "烧录 XML/HEX/SafetyFile 或 TEST 配置自发布后改变；请重新审核并发布扫码配方。");

        return new(recipe.RecipeId, scope, cases.Select(c => c.Name).ToArray(),
            policy, recipe.FirmwareReleaseLabel, firmware.Name,
            flashEntry.Name, rate, job.Fingerprint,
            recipe.TestProjectAssetId, recipe.TestProgramAssetId, selectedEntryIds.ToArray());
    }
}
