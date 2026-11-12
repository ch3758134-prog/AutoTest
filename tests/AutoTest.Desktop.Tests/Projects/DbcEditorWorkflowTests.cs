using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

/// <summary>Real persisted project → imported DBC → edit → file/manifest save → reopen.</summary>
public sealed class DbcEditorWorkflowTests
{
    private const string Basic = """
        VERSION ""

        NS_ :

        BS_:

        BU_: ECU Tester

        BO_ 291 Status: 8 ECU
         SG_ Speed : 0|16@1+ (0.1,0) [0|6553.5] "km/h" Tester
         SG_ Temp : 23|16@0- (0.5,-40) [-16424|16343.5] "degC" Tester
        """;

    [Fact]
    public async Task InvalidDbc_RejectsImportWithoutCreatingFakeAsset()
    {
        var parent = NewParent();
        try
        {
            var source = Path.Combine(parent, "invalid.dbc");
            await File.WriteAllTextAsync(source, "NOT A VALID DBC FILE");
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => workspace.ImportDbcAsync(source));
            Assert.Equal("DBC_PARSE", error.Code);
            Assert.Empty(workspace.Current!.Definition.Assets);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    private static string NewParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "autotest-dbc-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return parent;
    }

    [Fact]
    public async Task Import_EditFactor_Save_Reopen_PreservesModelAndAssetHash()
    {
        var parent = NewParent();
        try
        {
            var source = Path.Combine(parent, "test.dbc");
            await File.WriteAllTextAsync(source, Basic);
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("ECU", parent);
            var asset = await workspace.ImportDbcAsync(source);
            var opened = await workspace.OpenDbcEditorAsync(asset.Id);

            Assert.True(opened.Document.CanEdit);
            Assert.False(opened.Document.IsDirty);
            Assert.Equal(2, opened.Document.Model.FindMessage(291).Signals.Length);

            var message = opened.Document.Model.FindMessage(291);
            opened.Document.UpdateMessage(291, message with
            {
                Name = "StatusEdited",
                Transmitter = "ECU2"
            });
            var speed = opened.Document.Model.FindSignal(291, "Speed");
            opened.Document.UpdateSignal(291, "Speed", speed with { Factor = 0.25 });
            Assert.True(opened.Document.IsDirty);
            var saved = await workspace.SaveDbcEditorAsync(opened);
            Assert.False(saved.Document.IsDirty);
            Assert.Equal(2, saved.AssetRevision);
            Assert.Equal(0.25, saved.Document.Model.FindSignal(291, "Speed").Factor);

            var current = await ProjectRepository.OpenDefinitionAsync(project.ManifestPath);
            Assert.Equal(3, current.Revision);
            var updatedAsset = Assert.Single(current.Assets);
            Assert.Equal(2, updatedAsset.Revision);
            Assert.Equal(saved.FileReceipt.Sha256, updatedAsset.Sha256);

            var differentInstance = new ProjectWorkspaceService();
            await differentInstance.OpenAsync(project.ManifestPath);
            var reloaded = await differentInstance.OpenDbcEditorAsync(asset.Id);
            Assert.Equal(0.25, reloaded.Document.Model.FindSignal(291, "Speed").Factor);
            Assert.Equal("StatusEdited", reloaded.Document.Model.FindMessage(291).Name);
            Assert.Equal("ECU2", reloaded.Document.Model.FindMessage(291).Transmitter);
            Assert.Equal(updatedAsset.Sha256, reloaded.FileReceipt.Sha256);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task UnknownDirective_IsReadOnly_AndOriginalTextIsUntouched()
    {
        var parent = NewParent();
        try
        {
            var source = Path.Combine(parent, "readonly.dbc");
            var original = Basic + "\nCM_ SG_ 291 Speed \"notes\";\n";
            await File.WriteAllTextAsync(source, original);
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            var asset = await workspace.ImportDbcAsync(source);
            var opened = await workspace.OpenDbcEditorAsync(asset.Id);
            Assert.False(opened.Document.CanEdit);
            Assert.Contains("CM_", opened.Document.UnsupportedDirectives);
            Assert.Equal(original, opened.Document.ExportText());
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => workspace.SaveDbcEditorAsync(opened));
            Assert.Equal("DBC_UNSUPPORTED_READONLY", error.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(opened.FileReceipt.Path));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task ExternalDbcChange_MustNotBeOverwrittenOrAdvanceProjectRevision()
    {
        var parent = NewParent();
        try
        {
            var source = Path.Combine(parent, "conflict.dbc");
            await File.WriteAllTextAsync(source, Basic);
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            var asset = await workspace.ImportDbcAsync(source);
            var opened = await workspace.OpenDbcEditorAsync(asset.Id);
            opened.Document.UpdateSignal(291, "Speed",
                opened.Document.Model.FindSignal(291, "Speed") with { Factor = 0.75 });
            await File.AppendAllTextAsync(opened.FileReceipt.Path, "\nCM_ \"external\";\n");
            var manifestRevision = workspace.Current!.Definition.Revision;
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => workspace.SaveDbcEditorAsync(opened));
            Assert.Equal("DBC_FILE_CONFLICT", error.Code);
            Assert.Equal(manifestRevision, workspace.Current!.Definition.Revision);
            Assert.Contains("external", await File.ReadAllTextAsync(opened.FileReceipt.Path));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task ImportLargerThanWorkBenchLimit_DoesNotRegisterUnopenableAsset()
    {
        var parent = NewParent();
        try
        {
            var source = Path.Combine(parent, "oversize.dbc");
            await using (var stream = new FileStream(source, FileMode.CreateNew))
                stream.SetLength(4L * 1024 * 1024 + 1);
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            await Assert.ThrowsAsync<InvalidDataException>(() => workspace.ImportDbcAsync(source));
            Assert.Empty(workspace.Current!.Definition.Assets);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
