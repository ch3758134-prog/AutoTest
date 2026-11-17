using System.IO;
using AutoTest.UiPreview;

namespace AutoTest.G4Workbench;

/// <summary>
/// Formal filesystem integration check, no Mock devices and no external DUT.
/// Creates a real temporary project and TEST resource, persists through
/// ProjectWorkspaceService, reopens it and verifies durable step identity.
/// </summary>
public static class G4WorkspaceSmoke
{
    public static async Task<string> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoTest-G4-Workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var gateway = new RealEditorWorkspaceGateway();
            var project = await gateway.CreateProjectAsync("G4_File_Validation", root);
            if (!File.Exists(project.ManifestPath))
                throw new InvalidDataException("Formal manifest was not created.");

            var registered = await gateway.CreateTestAsync("G4_TEST_01");
            var asset = registered.TestPrograms.Single(a => a.Name == "G4_TEST_01");
            var loaded = await gateway.OpenTestAsync(asset.Id);
            var wait = gateway.CreateStep("Wait");
            wait.SetParam("milliseconds", "70000");
            wait.EditorMetadata["editor.note"] = "roundtrip-preserved";
            loaded.Phases.Single(p => p.Key == "test").Steps.Add(wait);
            if (gateway.Validate(asset.Id, loaded.Phases).Count == 0)
                throw new InvalidDataException("Out-of-range Wait was not rejected.");
            wait.SetParam("milliseconds", "125");

            var errors = gateway.Validate(asset.Id, loaded.Phases);
            if (errors.Count != 0)
                throw new InvalidDataException("Formal document invalid: " + string.Join("; ", errors));

            await gateway.SaveTestAsync(asset.Id, loaded.Phases);
            await gateway.OpenProjectAsync(project.ManifestPath); // prove file persistence after session reset
            var reopened = await gateway.OpenTestAsync(asset.Id);
            var actual = reopened.Phases.Single(p => p.Key == "test").Steps.Single();
            if (actual.Id != wait.Id || actual.Kind != "Wait" || actual.Param("milliseconds") != "125" ||
                actual.EditorMetadata.GetValueOrDefault("editor.note") != "roundtrip-preserved")
                throw new InvalidDataException("TEST step or its identity did not survive reopen.");
            return "G4_WORKSPACE_VERIFY_PASS" + Environment.NewLine +
                "Real project/test creation, edit, canonical save and reopen: PASS" + Environment.NewLine +
                "DUT, USB, CAN and runtime execution: NOT INVOKED";
        }
        finally
        {
            // Isolated scratch project; no user's project or resource can be touched.
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
