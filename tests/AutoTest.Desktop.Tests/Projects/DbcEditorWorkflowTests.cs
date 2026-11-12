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
            var original = Basic + "\nSG_MUL_VAL_ 291 Speed Other 1-2;\n";
            await File.WriteAllTextAsync(source, original);
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("ECU", parent);
            var asset = await workspace.ImportDbcAsync(source);
            var opened = await workspace.OpenDbcEditorAsync(asset.Id);
            Assert.False(opened.Document.CanEdit);
            Assert.Contains("SG_MUL_VAL_", opened.Document.UnsupportedDirectives);
            Assert.Equal(original, opened.Document.ExportText());
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => workspace.SaveDbcEditorAsync(opened));
            Assert.Equal("DBC_UNSUPPORTED_READONLY", error.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(opened.FileReceipt.Path));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task VectorExtendedFrame_WithCommentsAttributesAndEnums_ParameterSavePreservesOriginalLines()
    {
        // Representative subset of the real 绿通 communication matrix, not the private whole file.
        const string rich = """
            VERSION "绿通通信矩阵_来源整理_未实机校验_v1"
            NS_ :
                CM_
                BA_DEF_
                BA_
                VAL_
            BS_:
            BU_: VCU EPB AutoTest

            BO_ 2566869031 VCU_EPB_Command1: 8 VCU
             SG_ VCU_EPB_Check : 0|8@1+ (1,0) [0|255] "" EPB
             SG_ VCU_EPB_HeartBit : 8|4@1+ (1,0) [0|15] "" EPB
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 10000;
            BA_ "GenMsgCycleTime" BO_ 2566869031 65;
            CM_ BO_ 2566869031 "VCU-1; FrameId=0x18FF5027";
            VAL_ 2566869031 VCU_EPB_HeartBit 0 "Idle" 1 "Active";
            """;
        var parent = NewParent();
        try
        {
            var path = Path.Combine(parent, "LvTong.dbc");
            await File.WriteAllTextAsync(path, rich);
            var service = new ProjectWorkspaceService();
            var project = await service.CreateAsync("ECU", parent);
            var asset = await service.ImportDbcAsync(path);
            var opened = await service.OpenDbcEditorAsync(asset.Id);
            Assert.True(opened.Document.CanEdit);
            Assert.True(opened.Document.IsSourcePreserving);
            var msg = opened.Document.Model.FindMessage(2566869031u);
            Assert.True(msg.IsExtended);
            Assert.Equal(0x18FF5027u, msg.ArbitrationId);
            Assert.Equal("VCU_EPB_Command1", opened.Document.Model.FindMessage(0x18FF5027u).Name);
            var signal = opened.Document.Model.FindSignal(msg.Id, "VCU_EPB_Check");
            opened.Document.UpdateSignal(msg.Id, signal.Name,
                signal with { Factor = 0.5, Offset = 1 });
            var saved = await service.SaveDbcEditorAsync(opened);
            var newSource = await File.ReadAllTextAsync(saved.FileReceipt.Path);
            Assert.Contains("BA_ \"GenMsgCycleTime\" BO_ 2566869031 65;", newSource);
            Assert.Contains("CM_ BO_ 2566869031 \"VCU-1; FrameId=0x18FF5027\";", newSource);
            Assert.Contains("VAL_ 2566869031 VCU_EPB_HeartBit 0 \"Idle\" 1 \"Active\";", newSource);
            Assert.Contains("BO_ 2566869031 VCU_EPB_Command1: 8 VCU", newSource);
            var reopenedService = new ProjectWorkspaceService();
            await reopenedService.OpenAsync(project.ManifestPath);
            var reopened = await reopenedService.OpenDbcEditorAsync(asset.Id);
            Assert.Equal(0.5, reopened.Document.Model.FindSignal(2566869031u, "VCU_EPB_Check").Factor);
            Assert.Equal(1, reopened.Document.Model.FindSignal(2566869031u, "VCU_EPB_Check").Offset);
            Assert.Equal(2, reopened.AssetRevision);
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
