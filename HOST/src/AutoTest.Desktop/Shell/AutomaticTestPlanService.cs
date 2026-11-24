using System.IO;
using System.Security.Cryptography;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only operator projection of the SAME pinned TEST definition. A visual
/// step is never a second executable program and never supplies a verdict.
/// </summary>
public sealed record AutomaticTestStep(string Index, string Name, string State,
    IReadOnlyList<AutomaticTestStep> Children)
{
    public bool IsExpanded => false;
    public string Detail => State;
}

/// <summary>An ordered invocation from the unique canonical ECU TEST project.</summary>
public sealed record AutomaticTestCase(string Index, string Name, TestDocumentId DocumentId,
    string State = "未执行")
{
    public IReadOnlyList<AutomaticTestStep> Children { get; init; } = [];
    public bool IsExpanded => true;
    public string Detail => State;
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

    public Task RestoreAsync(CancellationToken ct = default) => RestoreAsync(null, ct);

    public async Task RestoreAsync(OpenProjectContext? opened, CancellationToken ct = default)
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
                definition = await ProjectWorkspaceService.ReadCurrentResourceMetadataAsync(
                    definition, Path.GetDirectoryName(full)!, ct);
                var after = await File.ReadAllBytesAsync(full, ct);
                if (!before.SequenceEqual(after))
                    throw new IOException("读取期间工程文件发生改变，请重新打开。");
                var project = new OpenProjectContext(definition, full, Convert.ToHexString(SHA256.HashData(after)));
                var test = await AutomaticTestResourceContract.ResolveAsync(project, ct);
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

    private static IReadOnlyList<AutomaticTestStep> BuildCaseSteps(TestDocument document)
    {
        // Legacy TEST retains three internal phases. The operator sees one
        // continuous, ordered program without mandatory empty phase headings.
        // The original source and StepId are not modified by this projection.
        var steps = new List<AutomaticTestStep>();
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            foreach (var node in document.Get(phase))
                steps.Add(ProjectStep(node, (steps.Count + 1).ToString("D2")));
        }
        return steps;
    }

    private static AutomaticTestStep ProjectStep(StepNode node, string index,
        string? branch = null)
    {
        var children = new List<AutomaticTestStep>();
        // Name the IF/ELSE/WHEN/SWITCH/LOOP slot explicitly. Otherwise a
        // read-only operator would see a flat list and mistake alternative
        // branches for sequential unconditional physical actions.
        foreach (var slot in node.Slots.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            for (var i = 0; i < slot.Value.Length; i++)
            {
                var child = slot.Value[i];
                children.Add(ProjectStep(child, (i + 1).ToString("D2"),
                    slot.Key));
            }
        }
        var state = StepExecutionPolicy.IsEnabled(node)
            ? "未执行" : "步骤已禁用（不执行）";
        var label = branch is null ? node.Name : "〔" + branch + "〕 " + node.Name;
        return new(index, label, state, children);
    }
}
