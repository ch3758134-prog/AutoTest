using System.IO;
using AutoTest.Application.V1;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests;

public sealed class R2ProjectProgramAssetTests
{
    [Fact]
    public async Task RichTestProgramBelongsToProjectAndSurvivesReopenWithRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoTest-R2-" + Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(root, "r2.autotest-project.json");
        try
        {
            var project = new V1Workspace();
            var program = LabDocument.New("Real TEST source model");
            program.Phases[LabPhase.Test].Add(LabNode.New("Sequence", "Run sequence") with
            {
                Slots = new Dictionary<string, List<LabNode>>(StringComparer.Ordinal)
                {
                    ["body"] = new List<LabNode> { LabNode.New("Wait", "Wait for response") with
                    {
                        Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["milliseconds"] = "5"
                        }
                    }}
                }
            });
            var registered = project.ImportLabProgram(LabDocumentCodec.Save(program));
            Assert.Equal(1, registered.Revision);
            Assert.NotNull(project.LabProgram);
            Assert.Single(project.LabProgram!.Phases[LabPhase.Test]);

            await project.SaveProjectAsync(projectPath);
            var reopened = new V1Workspace();
            await reopened.OpenProjectAsync(projectPath);
            Assert.Equal(program.Id, reopened.LabProgram!.Id);
            var initial = reopened.Assets.Single(x => x.Kind == "test-program");
            Assert.Equal(registered.Id, initial.Id);
            Assert.True(initial.VerifyHash(System.Text.Encoding.UTF8.GetBytes(
                LabDocumentCodec.Save(reopened.LabProgram))));

            var modified = reopened.LabProgram.Copy();
            modified.Phases[LabPhase.Cleanup].Add(LabNode.New("Log", "Capture outcome"));
            var updated = reopened.ReplaceLabProgram(LabDocumentCodec.Save(modified), initial.ToReference());
            Assert.Equal(2, updated.Revision);
            Assert.Equal(initial.Id, updated.Id);
            Assert.Throws<InvalidOperationException>(() =>
                reopened.ReplaceLabProgram(LabDocumentCodec.Save(modified), initial.ToReference()));
            await reopened.SaveProjectAsync(projectPath);
            var again = new V1Workspace();
            await again.OpenProjectAsync(projectPath);
            Assert.Single(again.LabProgram!.Phases[LabPhase.Cleanup]);
            Assert.Equal(2, again.Assets.Single(x => x.Kind == "test-program").Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidLabProgramDoesNotModifyProject()
    {
        var project = new V1Workspace();
        Assert.ThrowsAny<Exception>(() => project.ImportLabProgram("{broken json"));
        Assert.Null(project.LabProgramAssetId);
        Assert.Empty(project.Assets);
    }
    [Fact]
    public async Task FreshRealProjectDoesNotProduceDuplicateLegacyTestAssetOnSaveOrReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoTest-single-TEST-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "project.autotest-project.json");
        try
        {
            var project = new V1Workspace();
            await project.SaveProjectAsync(path);
            Assert.DoesNotContain(project.Assets, x => x.Kind == "test");
            Assert.DoesNotContain(project.Assets, x => x.Kind == "test-program");

            var opened = new V1Workspace();
            await opened.OpenProjectAsync(path);
            Assert.Null(opened.LabProgram);
            Assert.DoesNotContain(opened.Assets, x => x.Kind == "test");
            var program = LabDocument.New("唯一的真实工程 TEST");
            var inserted = opened.ImportLabProgram(LabDocumentCodec.Save(program));
            await opened.SaveProjectAsync(path);

            var reopened = new V1Workspace();
            await reopened.OpenProjectAsync(path);
            Assert.Equal(inserted.Id, reopened.LabProgramAssetId);
            Assert.Single(reopened.Assets.Where(x => x.Kind == "test-program"));
            Assert.DoesNotContain(reopened.Assets, x => x.Kind == "test");
            Assert.Equal(program.Id, reopened.LabProgram!.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
