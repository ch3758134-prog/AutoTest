using System.IO;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using AutoTest.Devices.Station;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only operator projection of the SAME pinned TEST definition. A visual
/// step is never a second executable program and never supplies a verdict.
/// </summary>
public sealed record AutomaticTestStep(string Index, string Name, string State,
    IReadOnlyList<AutomaticTestStep> Children, string ActionSummary = "")
{
    // Authored StepId is retained strictly for visual correlation with real
    // execution facts; this does not create a second execution definition.
    public StepId? AuthoredStepId { get; init; }
    public bool IsExpanded => State is "执行中" or "执行异常" ||
        Children.Any(child => child.IsExpanded);
    public string Detail => string.IsNullOrEmpty(ActionSummary)
        ? State : State + " · " + ActionSummary;
}

/// <summary>An ordered invocation from the unique canonical ECU TEST project.</summary>
public sealed record AutomaticTestCase(string Index, string Name, TestDocumentId DocumentId,
    string State = "未执行")
{
    public IReadOnlyList<AutomaticTestStep> Children { get; init; } = [];
    public bool IsExpanded => State is "执行中" or "执行异常" ||
        Children.Any(child => child.IsExpanded);
    public string Detail => State;
    public string StepCountLabel => Children.Count + " 步";
}

public sealed record AutomaticTestPlan(OpenProjectContext Project,
    IReadOnlyList<AutomaticTestCase> Cases)
{
    public IReadOnlyList<string> ResourceIssues { get; init; } = [];
    public bool ResourceReady => Cases.Count > 0 && ResourceIssues.Count == 0;
}

/// <summary>Read the currently saved ECU project; no separate station deployment.</summary>
public sealed class AutomaticTestPlanService
{
    public AutomaticTestPlan? Current { get; private set; }
    public string Issue { get; private set; } = "请先打开 ECU 检测工程";
    public string? ConfiguredPath { get; private set; }
    public event EventHandler? Changed;

    /// <summary>
    /// Production starts without an engineer's last-opened project.
    /// Only a new real barcode and a checked package may populate a plan.
    /// No executor or hardware state is modified by this reset.
    /// </summary>
    public void ClearForProductionScan()
    {
        Current = null;
        ConfiguredPath = null;
        Issue = "请扫描 ECU 编号，系统将自动选择检测工程。";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task RestoreAsync(CancellationToken ct = default) =>
        RestoreAsync(null, requireProductionDeployment: false, ct);

    public Task RestoreAsync(OpenProjectContext? opened, CancellationToken ct = default) =>
        RestoreAsync(opened, requireProductionDeployment: false, ct);

    public async Task RestoreAsync(OpenProjectContext? opened,
        bool requireProductionDeployment, CancellationToken ct = default)
    {
        Current = null;
        ConfiguredPath = null;
        try
        {
            var path = opened?.ManifestPath ?? new RecentProjectStore().GetLastProjectManifest();
            if (string.IsNullOrWhiteSpace(path))
            {
                Issue = "请在开发与调试中打开或创建 ECU 工程。";
            }
            else
            {
                var full = Path.GetFullPath(path);
                var before = await File.ReadAllBytesAsync(full, ct);
                var definition = await ProjectRepository.OpenDefinitionAsync(full, ct);
                // Operator readiness uses the MANIFEST's declared revision
                // and SHA256, not an implicit metadata repair. A file changed
                // outside its saved project must be treated as a blocking
                // configuration error instead of an executable new revision.
                var after = await File.ReadAllBytesAsync(full, ct);
                if (!before.SequenceEqual(after))
                    throw new IOException("读取期间工程文件发生改变，请重新打开。");
                var project = new OpenProjectContext(definition, full,
                    Convert.ToHexString(SHA256.HashData(after)));
                AutomaticTestResourceSnapshot test;
                if (requireProductionDeployment)
                {
                    // Only the single project that was explicitly checked and
                    // frozen for this station is visible as a production plan.
                    // Never auto-promote an engineer's autosaved draft.
                    var deployed = await StationDeploymentStore.RestoreAsync(ct)
                        ?? throw new InvalidDataException(
                            "本工位尚未确认正式检测工程；请在“开发与调试 → 检测准备 → 运行前检查”中确认当前工程。");
                    if (!string.Equals(deployed.Project.ManifestPath, project.ManifestPath,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(deployed.Project.ManifestSha256, project.ManifestSha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "本工位已确认的工程与当前工程不一致；请在开发与调试中重新核对并确认。");
                    project = deployed.Project;
                    test = deployed.Test;
                }
                else
                    test = await AutomaticTestResourceContract.ResolveAsync(project, ct);
                Current = Map(project, test);
                ConfiguredPath = full;
                Issue = string.Join("；", test.Issues.Take(4));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException or
            System.Text.Json.JsonException or FormatException or ProjectDataException)
        {
            Issue = "当前工程读取或检测配置有问题：" + error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AutomaticTestPlan Map(OpenProjectContext project, AutomaticTestResourceSnapshot test)
    {
        var cases = test.Cases.Select((item, i) =>
            new AutomaticTestCase((i + 1).ToString("D2"),
                item.Entry.DisplayName, item.Program.Id)
            {
                Children = BuildCaseSteps(item.Program)
            }).ToArray();
        return new(project, cases) { ResourceIssues = test.Issues };
    }

    // One read-only projection for production and engineering; it never creates execution facts.
    public static IReadOnlyList<AutomaticTestStep> BuildCaseSteps(TestDocument document)
    {
        // Legacy TEST retains three internal phases. The operator sees one
        // continuous, ordered program without mandatory empty phase headings.
        // The original source and StepId are not modified by this projection.
        var steps = new List<AutomaticTestStep>();
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            foreach (var node in document.Get(phase))
                steps.Add(ProjectStep(node, (steps.Count + 1).ToString("D2"),
                    document.Mappings));
        }
        return steps;
    }

    private static AutomaticTestStep ProjectStep(StepNode node, string index,
        IReadOnlyList<TestTargetMapping> mappings, string? branch = null)
    {
        var children = new List<AutomaticTestStep>();
        // Name the IF/ELSE/WHEN/SWITCH/LOOP slot explicitly. Otherwise a
        // read-only operator would see a flat list and mistake alternative
        // branches for sequential unconditional physical actions.
        foreach (var slot in node.Slots.OrderBy(x => node.Type == "If" ? x.Key switch
        {
            "then" => 0, "else" => 1, _ => 2
        } : 0).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            for (var i = 0; i < slot.Value.Length; i++)
            {
                var child = slot.Value[i];
                children.Add(ProjectStep(child, (i + 1).ToString("D2"),
                    mappings, slot.Key));
            }
        }
        var state = StepExecutionPolicy.IsEnabled(node)
            ? "未执行" : "步骤已禁用（不执行）";
        var branchLabel = branch switch
        {
            "then" => "条件成立", "else" => "否则", "body" => "子步骤",
            "cases" => "条件分派", null => "", _ => branch
        };
        var label = branch is null ? node.Name : "〔" + branchLabel + "〕 " + node.Name;
        return new(index, label, state, children, OperatorActionSummary(node, mappings))
        {
            AuthoredStepId = node.Id
        };
    }

    private static string OperatorActionSummary(
        StepNode node, IReadOnlyList<TestTargetMapping> mappings)
    {
        string Prop(string key) => node.Properties.GetValueOrDefault(key, "");
        string TargetDescription()
        {
            var mapping = mappings.FirstOrDefault(x =>
                string.Equals(x.Name, Prop("target"), StringComparison.OrdinalIgnoreCase));
            return mapping?.Kind == TestTargetKind.Signal && !string.IsNullOrWhiteSpace(mapping.SignalName)
                ? mapping.SignalName + " · 0x" + mapping.CanId.ToString("X")
                : Prop("target");
        }
        // Production must show what each case intends to do, not just its
        // name and an unreadable expanded tree. This is authoring metadata,
        // never a claim that the DUT returned a value or passed a condition.
        switch (node.Type)
        {
            case "If":
                return "IF " + Prop("variable") + " " +
                    node.Properties.GetValueOrDefault("operator", "==") + " " +
                    Prop("equals") + " · THEN 或 ELSE";
            case "Read":
                return "读取 " + TargetDescription() + " → " + Prop("variable") +
                    (Prop("mode") == "Latest" ? "（已有有效值）" : "（等待新数据）");
            case "WaitUntil":
                return "等待 " + TargetDescription() + " " + Prop("operator") +
                    " " + Prop("expected") + " · 最多 " + Prop("timeoutMs") + " ms";
            case "AssertSignal":
                return "信号判定 " + TargetDescription() + " " + Prop("operator") +
                    " " + Prop("expected") + " · 最多 " + Prop("timeoutMs") + " ms";
            case "Expect":
                return "测试判定 " + Prop("left") + " " + Prop("operator") +
                    " " + Prop("right");
            case "Loop":
                return "循环 " + Prop("count") + " 次";
            case "SetVariable":
                return "变量 " + Prop("variable") + " = " + Prop("value");
            case "Calculate":
                return "计算 " + Prop("variable") + " = " + Prop("left") +
                    " " + Prop("operator") + " " + Prop("right");
            case "WaveCompare":
                return "波形判定 · " + (Prop("profile").Length == 0
                    ? Prop("reference") : Prop("profile"));
            case "Flash":
                return "烧录操作 · 运行时仍须通过正式资源及 APP 后验检查";
        }
        if (node.Type == "Wait")
            return "纯等待 " + node.Properties.GetValueOrDefault("milliseconds", "0") +
                " ms（不改变 KEY 状态）";
        if (node.Type != "Write") return "";
        var target = node.Properties.GetValueOrDefault("target", "");
        var binding = mappings.FirstOrDefault(x => x.Name == target);
        if (binding?.Kind != TestTargetKind.RawKey) return "";
        var value = node.Properties.GetValueOrDefault("value", "");
        try
        {
            var action = RawKeyAction.Parse(value);
            if (action.Action == RawKeyOperation.Wait)
                return "纯等待 " + action.DurationMs +
                    " ms · 不发送 KEY CAN";
            if (action.Action == RawKeyOperation.Release)
                return "明确全部断开 KEY";
            var names = action.Mask == 0 ? "全部断开" :
                string.Join(" + ", Enumerable.Range(1, 20)
                    .Where(i => (action.Mask & (1u << (i - 1))) != 0)
                    .Select(i => "K" + i.ToString("00")));
            return action.Action == RawKeyOperation.Pulse
                ? "短按 " + names + " · " + action.DurationMs + "ms 后自动释放"
                : "保持请求 " + names + " · " + action.DurationMs +
                    "ms 后不自动释放";
        }
        catch (FormatException)
        {
            return "KEY 配置需核对：" + value;
        }
    }
}
