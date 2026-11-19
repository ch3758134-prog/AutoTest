using System.IO;
using System.Text;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;

namespace AutoTest.G4Workbench;

/// <summary>
/// Real filesystem validation against the canonical ProjectWorkspaceService,
/// StepNode, TestDocument, StepEditTransaction and TestProjectDocument.
/// No DemoStep, UI conversion bridge, device simulation, or ECU write.
/// </summary>
public static class G4WorkspaceSmoke
{
    public static async Task<string> RunAsync()
    {
        var scratch = Path.Combine(Path.GetTempPath(),
            "AutoTest-G4-Formal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("G4_Formal_Test", scratch);
            var manifest = workspace.Current?.ManifestPath
                ?? throw new InvalidDataException("No real project manifest.");
            if (!File.Exists(manifest))
                throw new InvalidDataException("Project manifest not written.");

            var testAsset = await workspace.CreateTestProgramAsync("G4_Test_01");
            var opened = await workspace.OpenTestEditorAsync(testAsset.Id);
            var tx = new StepEditTransaction(opened.Document);
            var wait = StepUiCatalog.NewNode("Wait").WithProperty("milliseconds", "125");
            tx.Insert(TestPhase.Test, null, null, 0, wait);
            if (StepTreeValidator.ValidateTree(tx.Current, StepDescriptorCatalog.Builtins()).Length != 0)
                throw new InvalidDataException("Canonical TEST validation failed.");

            var saved = await workspace.SaveTestEditorAsync(opened, tx.Current);
            if (saved.Document.Get(TestPhase.Test).Single().Id != wait.Id)
                throw new InvalidDataException("Saved formal TEST step lost its ID.");

            var projectAsset = await workspace.CreateTestProjectAsync("G4_Project_01");
            var openedProject = await workspace.OpenTestProjectEditorAsync(projectAsset.Id);
            var pinned = workspace.Current!.Definition.Assets.Single(a => a.Id == testAsset.Id);
            var item = new TestProjectEntry(Guid.NewGuid(), pinned.Id.Value,
                pinned.Revision, pinned.Sha256, pinned.Name, true);
            var plan = openedProject.Document with
            {
                Packages = new List<TestProjectEntry> { item }
            };
            var savedPlan = await workspace.SaveTestProjectEditorAsync(openedProject, plan);
            if (savedPlan.Document.Packages.Single().PackageAssetId != pinned.Id.Value)
                throw new InvalidDataException("Project package reference was not saved.");

            // Import a real, deliberately minimal on-disk DBC via the existing parser.
            var dbcPath = Path.Combine(scratch, "g4_input.dbc");
            var dbc = "VERSION \"\"\n\nNS_ :\n\nBS_:\n\nBU_: TEST\n\n" +
                "BO_ 291 G4_Status: 8 TEST\n" +
                " SG_ Enable : 0|8@1+ (1,0) [0|1] \"\" TEST\n";
            await File.WriteAllTextAsync(dbcPath, dbc, new UTF8Encoding(false));
            var dbcAsset = await workspace.ImportDbcAsync(dbcPath);
            var dbcSession = await workspace.OpenDbcEditorAsync(dbcAsset.Id);
            var message = dbcSession.Document.Model.Messages.Single(m => m.Name == "G4_Status");
            if (message.ArbitrationId != 291u || message.Signals.Single().Name != "Enable")
                throw new InvalidDataException("Formal DBC parse was inconsistent.");

            await workspace.OpenAsync(manifest);
            var persisted = await workspace.OpenTestEditorAsync(testAsset.Id);
            var persistedPlan = await workspace.OpenTestProjectEditorAsync(projectAsset.Id);
            if (persisted.Document.Get(TestPhase.Test).Single().Id != wait.Id ||
                persisted.Document.Get(TestPhase.Test).Single()
                    .Properties.GetValueOrDefault("milliseconds") != "125" ||
                persistedPlan.Document.Packages.Single().PackageSha256 != pinned.Sha256)
                throw new InvalidDataException("TEST or test-project failed after real reopen.");

            return "G4_WORKSPACE_VERIFY_PASS" + Environment.NewLine +
                "Canonical TEST StepNode transaction/save/reopen: PASS" + Environment.NewLine +
                "Real test project package revision pin/save/reopen: PASS" + Environment.NewLine +
                "Real DBC import/parse: PASS" + Environment.NewLine +
                "DemoStep conversions: NONE" + Environment.NewLine +
                "Device, CAN transmission, UDS and ECU execution: NOT INVOKED";
        }
        finally
        {
            try { Directory.Delete(scratch, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
