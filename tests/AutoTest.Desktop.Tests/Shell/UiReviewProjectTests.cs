using System.IO;
using AutoTest.Application.V1;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

/// <summary>
/// Protects the editable, physically safe sample project used to judge the real R1 IDE layout.
/// The test deliberately never connects to or operates a USB/CAN device.
/// </summary>
public sealed class UiReviewProjectTests
{
    [Fact]
    public async Task FullReviewProjectOpensWithHashVerifiedAssetsAndPopulatedEditor()
    {
        var sample = FindSampleProject();
        var workspace = new V1Workspace();
        await workspace.OpenProjectAsync(sample);

        Assert.NotNull(workspace.LabProgram);
        Assert.NotNull(workspace.Database);
        Assert.NotNull(workspace.LabProgramAssetId);
        Assert.NotNull(workspace.DbcAssetId);
        Assert.Equal(4, workspace.LabProgram!.Phases[LabPhase.Test].Count(x => x.Type == "TestCase"));
        Assert.True(workspace.LabProgram.Walk().Count() >= 40);
        Assert.True(new LabPlanCompiler().Prepare(workspace.LabProgram).Ready);

        var editor = new LabTestWorkspaceViewModel(workspace);
        Assert.Equal(3, editor.Tree.Count);
        Assert.NotEmpty(editor.Tree.Single(x => x.Phase == LabPhase.Test).Children);
        Assert.True(editor.Tree.SelectMany(x => x.Children).Count() >= 8);
        Assert.Contains("EPB ECU", editor.DocumentName);

        // A project being present is not a hardware pass or a production record.
        Assert.Empty(editor.Facts);
        Assert.Equal("NOT_RUN", editor.Verdict);
    }

    private static string FindSampleProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "samples", "ui-review-epb",
                "EPB-UI-review.autotest-project.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("The checked-in UI review project is missing.");
    }
}
