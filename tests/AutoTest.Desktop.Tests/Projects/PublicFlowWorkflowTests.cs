using AutoTest.Desktop.Projects;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class PublicFlowWorkflowTests
{
    [Fact]
    public async Task CreateSaveReopenKeepsIdentityAndRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-public-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("FlowProject", root);
            var asset = await workspace.CreatePublicFlowAsync("动作组");
            Assert.Equal(ProjectWorkspaceService.PublicFlowKind, asset.Kind);
            var session = await workspace.OpenPublicFlowEditorAsync(asset.Id);
            Assert.Empty(session.Document.Body);
            var step = StepNode.Create("Wait", "等待")
                .WithProperty("milliseconds", "300");
            var edited = session.Document with { Body = [step] };
            var saved = await workspace.SavePublicFlowEditorAsync(session, edited);
            Assert.Equal(2, saved.AssetRevision);
            Assert.Equal(session.Document.FlowId, saved.Document.FlowId);
            var other = new ProjectWorkspaceService();
            await other.OpenAsync(project.ManifestPath);
            var reread = await other.OpenPublicFlowEditorAsync(asset.Id);
            Assert.Equal(step.Id, Assert.Single(reread.Document.Body).Id);
            Assert.Equal("300", reread.Document.Body[0].Properties["milliseconds"]);
            Assert.Equal(saved.FileHash, reread.FileHash, ignoreCase: true);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExternalFileChangeRejectsStaleSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-public-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("FlowProject", root);
            var asset = await workspace.CreatePublicFlowAsync("流程");
            var opened = await workspace.OpenPublicFlowEditorAsync(asset.Id);
            await File.AppendAllTextAsync(opened.FilePath, " ");
            var edited = opened.Document with { Name = "更名草稿" };
            var error = await Assert.ThrowsAsync<AutoTest.Projects.Project.ProjectDataException>(
                () => workspace.SavePublicFlowEditorAsync(opened, edited));
            Assert.Equal("FLOW_FILE_CONFLICT", error.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
