using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Specialists.Wave;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Desktop.Projects;

/// <summary>A project entry is an invocation, not a second copy of its TEST program.</summary>
public sealed record AutomaticTestCaseBinding(TestProjectEntry Entry, AssetEntry Asset,
    TestDocument Program, string ContentSha256);

/// <summary>
/// The same resolved, ordered scope is used by the automatic-test screen and
/// real production preparation. Issues block physical execution, but opening
/// an incomplete project for inspection remains possible.
/// </summary>
public sealed record AutomaticTestResourceSnapshot(
    AssetEntry TestProjectAsset, IReadOnlyList<AutomaticTestCaseBinding> Cases,
    IReadOnlyList<string> Issues)
{
    /// <summary>Project-wide physical environment stages are distinct from
    /// judged TEST cases yet reuse the same pinned TestDocument DSL.</summary>
    public AutomaticTestCaseBinding? ProjectSetup { get; init; }
    public AutomaticTestCaseBinding? ProjectTeardown { get; init; }
    public IReadOnlyList<ResourceDependencyGroup> DependencyGroups { get; init; } =
        Array.Empty<ResourceDependencyGroup>();

    public IReadOnlyList<AutomaticTestCaseBinding> OrderedExecutions()
    {
        var result = new List<AutomaticTestCaseBinding>(Cases.Count + 2);
        if (ProjectSetup is not null) result.Add(ProjectSetup);
        result.AddRange(Cases);
        if (ProjectTeardown is not null) result.Add(ProjectTeardown);
        return result;
    }

    public bool CanExecute => Cases.Count > 0 && Issues.Count == 0;
    public void RequireExecutable()
    {
        if (!CanExecute)
            throw new InvalidDataException("自动测试资源未准备完成：" +
                string.Join("；", Issues.Take(12)));
    }
}

/// <summary>
/// Read-only, deterministic resource-closure validation. Does not connect USB,
/// change project files, publish FLASH, select the operator's recipe or grant
/// permission to send CAN. Only entries participating in automatic testing
/// authorize resource dependencies; unused draft resources are not execution
/// blockers. Draft/production differ in authorization, never in asset identity.
/// </summary>
public static class AutomaticTestResourceContract
{
    public static async Task<AutomaticTestResourceSnapshot> ResolveAsync(
        OpenProjectContext project, CancellationToken ct = default)
    {
        var assets = project.Definition.Assets;
        var testProjects = assets.Where(a =>
            a.Kind == ProjectWorkspaceService.TestProjectKind).ToArray();
        if (testProjects.Length != 1)
            throw new InvalidDataException(testProjects.Length == 0
                ? "工程缺少自动测试项目。"
                : "工程有多个测试项目，尚未确定唯一的自动测试执行树。");
        var projectAsset = testProjects[0];
        var bytes = await ReadPinnedAsync(project, projectAsset, ct);
        var tree = TestProjectCodec.Decode(bytes);
        var included = tree.AutomaticRunEntries();
        var issues = new List<string>();
        var details = new List<ResourceDependencyIssue>();
        void Flag(string original, string key, string category, string title,
            string affected, string reason, Guid? resource = null, Guid? occurrence = null)
        {
            issues.Add(original);
            details.Add(new(key, category, title, affected, reason, resource, occurrence));
        }
        var cases = new List<AutomaticTestCaseBinding>();
        if (tree.Packages.Count == 0)
            Flag("自动测试项目尚未添加测试用例。", "PROJECT:EMPTY",
                "项目结构", "缺少有序测试用例", tree.Name,
                "在 TEST 项目中添加正式用例实例。", projectAsset.Id.Value);
        else if (tree.Packages.Any(entry => !entry.Enabled))
            Flag("工程包含历史排除的用例实例；正式生产必须运行完整有序项目。请在测试项目中逐条确认“恢复历史排除”并保存，或显式从工程顺序中移除不需要的实例。禁止静默跳过。",
                "PROJECT:EXCLUDED", "项目结构", "历史排除实例待处置", tree.Name,
                "明确恢复或者从有序项目中移除。", projectAsset.Id.Value);

        foreach (var entry in included)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var target = assets.SingleOrDefault(a => a.Id.Value == entry.PackageAssetId &&
                    a.Kind == ProjectWorkspaceService.TestProgramKind)
                    ?? throw new InvalidDataException("用例源资源不存在。");
                // Production must not substitute the latest TEST asset for
                // the entry's explicitly frozen revision and SHA256.
                RequirePinnedInvocation(entry, target);
                var source = await ReadPinnedAsync(project, target, ct);
                // Preserve the authored TEST mapping pins verbatim. Any
                // change in DBC/wave resource SHA or revision must produce a
                // preflight issue, not an automatic binding to the newest file.
                var program = TestDocumentCodec.Deserialize(
                    new UTF8Encoding(false, true).GetString(source));
                // Compile first, but DO NOT replace a case's authoring tree or
                // infer a successful test from absence of execution errors.
                var prepared = new PlanCompiler().Prepare(program,
                    allowRealOperations: true);
                foreach (var error in prepared.Diagnostics)
                    Flag("用例「" + entry.DisplayName + "」：" + error,
                        "PLAN:" + entry.PackageAssetId.ToString("N"),
                        "TEST 步骤", "TEST 定义预检失败", entry.DisplayName,
                        error, entry.PackageAssetId, entry.ItemId);
                // The automatic-test flow owns pre-run FLASH. An embedded
                // case FLASH would program the same ECU twice.
                if (TestRunStepScope.EnabledSteps(program).Any(s => s.Type == "Flash"))
                    Flag("用例「" + entry.DisplayName +
                        "」含烧录步骤；正式自动测试使用工程级前置烧录，不得重复烧录。",
                        "FLASH:" + entry.PackageAssetId.ToString("N"),
                        "执行冲突", "用例包含重复烧录", entry.DisplayName,
                        "移除用例内 FLASH，使用工程前置原生烧录。",
                        entry.PackageAssetId, entry.ItemId);
                foreach (var mapping in TestRunStepScope.RequiredMappings(program))
                {
                    try { await ValidateMappingAsync(project, mapping, ct); }
                    catch (Exception error) when (error is InvalidDataException or
                        IOException or ArgumentException or ProjectDataException or
                        FormatException or NotSupportedException)
                    {
                        Flag("用例「" + entry.DisplayName + "」资源「" +
                            mapping.Name + "」：" + error.Message,
                            "RESOURCE:" + mapping.Asset.Value.ToString("N") + ":" +
                                mapping.Kind, "工程资源", "资源修订/映射需要复核",
                            entry.DisplayName + " · " + mapping.Name,
                            error.Message, mapping.Asset.Value == Guid.Empty ? null :
                            mapping.Asset.Value, entry.ItemId);
                    }
                }
                cases.Add(new(entry, target, program, target.Sha256));
            }
            catch (Exception error) when (error is InvalidDataException or IOException or
                UnauthorizedAccessException or ArgumentException or ProjectDataException or
                FormatException or NotSupportedException)
            {
                Flag("用例「" + entry.DisplayName + "」：" + error.Message,
                    "CASE:" + entry.PackageAssetId.ToString("N"),
                    "TEST 引用", "TEST 定义修订不一致或资源缺失",
                    entry.DisplayName, error.Message, entry.PackageAssetId,
                    entry.ItemId);
            }
        }
        var projectSetup = tree.ProjectSetup is null ? null :
            await ResolveProjectStageAsync(project, tree.ProjectSetup,
                "项目准备", issues, details, ct).ConfigureAwait(false);
        var projectTeardown = tree.ProjectTeardown is null ? null :
            await ResolveProjectStageAsync(project, tree.ProjectTeardown,
                "项目收尾", issues, details, ct).ConfigureAwait(false);
        return new(projectAsset, cases.AsReadOnly(), issues.AsReadOnly())
        {
            ProjectSetup = projectSetup, ProjectTeardown = projectTeardown,
            DependencyGroups = ResourceDependencyGroup.Group(details)
        };
    }

    private static async Task<AutomaticTestCaseBinding?> ResolveProjectStageAsync(
        OpenProjectContext project, TestProjectEntry entry, string phaseName,
        List<string> issues, List<ResourceDependencyIssue> details,
        CancellationToken ct)
    {
        void StageProblem(string reason, string key, string category,
            string title, Guid? resource = null)
        {
            issues.Add(phaseName + "：" + reason);
            details.Add(new ResourceDependencyIssue(key, category, title,
                phaseName + " · " + entry.DisplayName, reason,
                resource ?? entry.PackageAssetId, entry.ItemId));
        }
        try
        {
            if (!entry.Enabled)
                throw new InvalidDataException("项目级阶段不能使用历史排除开关；请显式移除或恢复。");
            var asset = project.Definition.Assets.SingleOrDefault(a =>
                a.Kind == ProjectWorkspaceService.TestProgramKind &&
                a.Id.Value == entry.PackageAssetId)
                ?? throw new InvalidDataException("项目级阶段的 TEST 定义资源不存在。");
            RequirePinnedInvocation(entry, asset);
            var bytes = await ReadPinnedAsync(project, asset, ct).ConfigureAwait(false);
            var program = TestDocumentCodec.Deserialize(
                new UTF8Encoding(false, true).GetString(bytes));
            var prepared = new PlanCompiler().Prepare(program, allowRealOperations: true);
            foreach (var diag in prepared.Diagnostics)
                StageProblem(diag, "PLAN:" + entry.PackageAssetId.ToString("N"),
                    "TEST 步骤", "项目阶段步骤预检失败");
            if (TestRunStepScope.EnabledSteps(program).Any(s => s.Type == "Flash"))
                StageProblem("不可内嵌 FLASH；每台 ECU 前置烧录由工位工艺负责。",
                    "FLASH:" + entry.PackageAssetId.ToString("N"),
                    "执行冲突", "项目阶段包含重复 FLASH");
            foreach (var mapping in TestRunStepScope.RequiredMappings(program))
            {
                try { await ValidateMappingAsync(project, mapping, ct).ConfigureAwait(false); }
                catch (Exception error) when (error is InvalidDataException or
                    IOException or ArgumentException or ProjectDataException or
                    FormatException or NotSupportedException)
                {
                    issues.Add(phaseName + " 资源「" + mapping.Name + "」：" + error.Message);
                    details.Add(new ResourceDependencyIssue(
                        "RESOURCE:" + mapping.Asset.Value.ToString("N") + ":" + mapping.Kind,
                        "工程资源", "资源修订/映射需要复核",
                        phaseName + " · " + mapping.Name, error.Message,
                        mapping.Asset.Value == Guid.Empty ? null : mapping.Asset.Value,
                        entry.ItemId));
                }
            }
            return new(entry, asset, program, asset.Sha256);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or
            UnauthorizedAccessException or ArgumentException or ProjectDataException or
            FormatException or NotSupportedException)
        {
            StageProblem(error.Message,
                "CASE:" + entry.PackageAssetId.ToString("N"),
                "TEST 引用", "项目阶段引用未通过检查");
            return null;
        }
    }

    /// <summary>
    /// Production project entry provenance is independent from the physical
    /// source asset's own verified hash. Both sides must agree; only the
    /// engineer may explicitly adopt a new revision in TEST Project editor.
    /// </summary>
    public static void RequirePinnedInvocation(TestProjectEntry entry, AssetEntry asset)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(asset);
        if (entry.PackageAssetId != asset.Id.Value ||
            entry.PackageRevision != asset.Revision ||
            !string.Equals(entry.PackageSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "测试项目调用实例「" + entry.DisplayName + "」固定 r" +
                entry.PackageRevision + " / " + entry.PackageSha256 +
                "，当前 TEST 资源 r" + asset.Revision + " / " + asset.Sha256 +
                "；ID、修订或 SHA256 不一致。必须在工程编辑中核对并更新引用，" +
                "正式生产禁止静默替换最新版本。");
    }

    public static async Task<byte[]> ReadPinnedAsync(OpenProjectContext project,
        AssetEntry asset, CancellationToken ct = default)
    {
        var full = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
        var content = await File.ReadAllBytesAsync(full, ct);
        if (!asset.VerifyHash(content))
            throw new InvalidDataException("资源文件内容与工程索引 SHA256 不一致：" + asset.Name);
        return content;
    }

    internal static async Task ValidateMappingAsync(OpenProjectContext project,
        TestTargetMapping mapping, CancellationToken ct)
    {
        mapping.Validate();
        if (mapping.Kind is TestTargetKind.Ignition or
            TestTargetKind.RawCan or TestTargetKind.RawKey) return;
        var resource = project.Definition.Assets.SingleOrDefault(
            a => a.Id == mapping.Asset)
            ?? throw new InvalidDataException("映射的工程资源不存在。");
        if (resource.Revision != mapping.AssetRevision ||
            !string.Equals(resource.Sha256, mapping.AssetSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("映射仍引用旧的资源修订，需在编辑器中修复。");
        var kind = mapping.Kind;
        if (kind is TestTargetKind.Signal or TestTargetKind.CanMessage)
        {
            if (resource.Kind != ProjectWorkspaceService.DbcKind)
                throw new InvalidDataException("信号/报文未指向 DBC 资源。");
            var dbc = DbcSource.Load(await ReadPinnedAsync(project, resource, ct));
            var parsed = DbcParserAdapter.Parse(dbc.Text, resource.Name);
            var message = parsed.Messages.SingleOrDefault(m =>
                m.ArbitrationId == mapping.CanId && m.IsExtended == mapping.Extended)
                ?? throw new InvalidDataException("DBC 报文 ID 或帧类型已不匹配。");
            if (kind == TestTargetKind.Signal &&
                !message.Signals.Any(s => s.Name == mapping.SignalName &&
                    s.Unit == mapping.Unit))
                throw new InvalidDataException("DBC 信号或单位已变更。");
        }
        else if (kind == TestTargetKind.WaveProfile)
        {
            if (resource.Kind != ProjectWorkspaceService.WaveSettingsKind)
                throw new InvalidDataException("波形分析引用的不是波形配置。");
            var data = await ReadPinnedAsync(project, resource, ct);
            var cfg = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(
                resource.Kind, data);
            if (!cfg.DbcAssetId.HasValue ||
                string.IsNullOrWhiteSpace(cfg.SignalName) ||
                string.IsNullOrWhiteSpace(cfg.ReferenceName))
                throw new InvalidDataException("未配置 DBC 信号或明确的标准波形。");
            var signalAsset = project.Definition.Assets.SingleOrDefault(
                a => a.Id.Value == cfg.DbcAssetId.Value &&
                    a.Kind == ProjectWorkspaceService.DbcKind)
                ?? throw new InvalidDataException("波形绑定的 DBC 资源不存在。");
            if (signalAsset.Revision != cfg.DbcAssetRevision ||
                !string.Equals(signalAsset.Sha256, cfg.DbcAssetFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("波形配置仍引用旧 DBC 修订。");
            var catalog = new WaveReferenceCatalog(Path.Combine(
                project.RootDirectory, "wave-records", "references"));
            var refs = catalog.List().Where(x =>
                x.Name == cfg.ReferenceName &&
                x.Signal.DbcAsset.Id.Value == cfg.DbcAssetId.Value &&
                x.Signal.DbcAsset.Revision.Value == cfg.DbcAssetRevision &&
                string.Equals(x.Signal.DbcAsset.Fingerprint.Sha256,
                    cfg.DbcAssetFingerprint, StringComparison.OrdinalIgnoreCase) &&
                x.Signal.Role == cfg.Bus &&
                x.Signal.FrameId == cfg.CanId &&
                x.Signal.SignalName == cfg.SignalName &&
                x.Signal.Unit == cfg.Unit).ToArray();
            if (refs.Length != 1)
                throw new InvalidDataException("标准波形未找到唯一匹配的真实参考版本。");
            _ = catalog.Load(refs[0].Reference); // verify immutable reference bytes
        }
        else if (kind == TestTargetKind.Button)
        {
            if (resource.Kind != ProjectWorkspaceService.ControllerKind)
                throw new InvalidDataException("按键映射资源种类不一致。");
            _ = await ReadPinnedAsync(project, resource, ct);
        }
        else
        {
            throw new InvalidDataException("当前 TEST 不支持的映射类型：" + kind);
        }
    }
}
