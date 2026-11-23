using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Shell;

/// <summary>An ordered entry from the same canonical ECU TEST project.</summary>
public sealed record AutomaticTestCase(string Index, string Name, TestDocumentId DocumentId,
    string State = "未执行");

public sealed record AutomaticTestPlan(OpenProjectContext Project,
    IReadOnlyList<AutomaticTestCase> Cases)
{
    public IReadOnlyList<string> ResourceIssues { get; init; } = [];
    public string DeploymentFingerprint { get; init; } = "";
    public bool ResourceReady => Cases.Count > 0 && ResourceIssues.Count == 0 &&
        DeploymentFingerprint.Length == 64;
}

/// <summary>
/// One station deployment of the engineer's canonical ECU project. No separate
/// operator recipe, test-scope selection or silent adoption of editor drafts.
/// A station deployment pins the exact manifest, TEST, FLASH and fixture
/// procedure. It is configuration deployment, NOT physical readiness or PASS.
/// </summary>
public sealed class AutomaticTestPlanService
{
    public AutomaticTestPlan? Current { get; private set; }
    public string Issue { get; private set; } = "本工位尚未部署 ECU 检测工程";
    public string? ConfiguredPath { get; private set; }
    public event EventHandler? Changed;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        try
        {
            // Changed is consumed by WPF ObservableCollection bindings.
            // Preserve the UI SynchronizationContext at this presentation edge.
            var verified = await StationDeploymentStore.RestoreAsync(ct);
            if (verified is null)
            {
                Current = null;
                ConfiguredPath = null;
                Issue = "尚未部署正式 ECU 工程。请在开发与调试中核对并部署当前工程。";
            }
            else
            {
                Current = Map(verified);
                ConfiguredPath = verified.Project.ManifestPath;
                Issue = "";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException or
            System.Text.Json.JsonException or FormatException or ProjectDataException)
        {
            // A former deployment remains on disk for explicit repair; never
            // fall back to the old operator-selected plan or an IDE draft.
            Current = null;
            Issue = "已部署工程不可执行：" + error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeployAsync(OpenProjectContext engineerProject,
        CancellationToken ct = default)
    {
        // A failed validation must leave the previous valid deployment intact.
        var verified = await StationDeploymentStore.DeployAsync(engineerProject, ct);
        Current = Map(verified);
        ConfiguredPath = verified.Project.ManifestPath;
        Issue = "";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AutomaticTestPlan Map(StationDeploymentStore.Verified verified)
    {
        var cases = verified.Test.Cases.Select((item, i) =>
            new AutomaticTestCase((i + 1).ToString("D2"),
                item.Entry.DisplayName, item.Program.Id)).ToArray();
        return new(verified.Project, cases)
        {
            DeploymentFingerprint = verified.Pin.ManifestSha256,
            ResourceIssues = verified.Test.Issues
        };
    }
}
