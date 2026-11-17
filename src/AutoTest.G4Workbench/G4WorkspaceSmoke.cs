using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
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

            // The real DBC importer and parser supply the report/signal catalog.
            var dbcPath = Path.Combine(root, "g4_sample.dbc");
            var dbcText = "VERSION \"\"\n\nNS_ :\n\nBS_:\n\nBU_: TEST\n\n" +
                "BO_ 291 G4_Status: 8 TEST\n" +
                " SG_ Enable : 0|8@1+ (1,0) [0|1] \"\" TEST\n";
            await File.WriteAllTextAsync(dbcPath, dbcText, new UTF8Encoding(false));
            await gateway.ImportDbcAsync(dbcPath);
            var catalogs = await gateway.LoadDbcCatalogAsync();
            var frame = catalogs.SelectMany(c => c.Messages).Single(m => m.Name == "G4_Status");
            var signal = frame.Signals.Single(s => s.Name == "Enable");
            if (frame.ArbitrationId != 291u || signal.Maximum != 1)
                throw new InvalidDataException("Imported DBC model does not match source.");

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

            var send = gateway.CreateStep("CanSend");
            send.SetParam("dbcMessage", frame.Alias);
            send.SetParam("signals", JsonSerializer.Serialize(new Dictionary<string, string>
            { ["Enable"] = "2" }));
            send.EditorMetadata["editor.dbcAssetId"] = frame.AssetId;
            send.EditorMetadata["editor.dbcRevision"] = frame.AssetRevision.ToString(CultureInfo.InvariantCulture);
            send.EditorMetadata["editor.dbcSha256"] = frame.AssetSha256;
            send.EditorMetadata["editor.dbcEncodedId"] = frame.EncodedId.ToString(CultureInfo.InvariantCulture);
            loaded.Phases.Single(p => p.Key == "test").Steps.Add(send);
            if (gateway.Validate(asset.Id, loaded.Phases).Count == 0)
                throw new InvalidDataException("Out-of-range DBC signal value was not rejected.");
            send.SetParam("signals", "{\"Enable\":\"1\"}");
            var stale = send.Copy();
            stale.EditorMetadata["editor.dbcSha256"] = "stale-fingerprint";
            if (CanDbcBinding.Validate(stale, catalogs).Count == 0)
                throw new InvalidDataException("Stale DBC asset reference was not rejected.");

            var errors = gateway.Validate(asset.Id, loaded.Phases);
            if (errors.Count != 0)
                throw new InvalidDataException("Formal document invalid: " + string.Join("; ", errors));

            await gateway.SaveTestAsync(asset.Id, loaded.Phases);
            await gateway.OpenProjectAsync(project.ManifestPath); // prove file persistence after session reset
            var reopened = await gateway.OpenTestAsync(asset.Id);
            var nodes = reopened.Phases.Single(p => p.Key == "test").Steps;
            var actual = nodes.Single(n => n.Kind == "Wait");
            var persistedCan = nodes.Single(n => n.Kind == "CanSend");
            var rereadCatalog = await gateway.LoadDbcCatalogAsync();
            if (CanDbcBinding.Validate(persistedCan, rereadCatalog).Count != 0 ||
                persistedCan.Id != send.Id || persistedCan.Param("signals") != "{\"Enable\":\"1\"}")
                throw new InvalidDataException("DBC signal binding did not survive TEST save/reopen.");
            if (actual.Id != wait.Id || actual.Kind != "Wait" || actual.Param("milliseconds") != "125" ||
                actual.EditorMetadata.GetValueOrDefault("editor.note") != "roundtrip-preserved")
                throw new InvalidDataException("TEST step or its identity did not survive reopen.");
            return "G4_WORKSPACE_VERIFY_PASS" + Environment.NewLine +
                "Real project/test creation, edit, canonical save and reopen: PASS" + Environment.NewLine +
                "DBC import, message/signal data, physical range, pinned revision, TEST roundtrip: PASS" +
                Environment.NewLine + "DUT, USB, CAN and runtime execution: NOT INVOKED";
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
