using System.Text;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using Xunit;

namespace AutoTest.Projects.Tests.Resources;

public sealed class R0ProjectAssetDiagnosticTests
{
    private const string Dbc = """
        VERSION ""
        NS_ :
        BS_:
        BU_: ECU Tester
        BO_ 256 Status: 1 ECU
         SG_ Counter : 0|8@1+ (1,0) [0|255] "" Tester
        """;

    [Fact]
    public async Task ImportedResource_UsesCanonicalIdentityAndCopyOnWriteRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "R0Resource_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifest = Path.Combine(root, "project.json");
            var source = Path.Combine(root, "source.dbc");
            await File.WriteAllTextAsync(source, Dbc);
            var ws = await R0ProjectAssetWorkspace.CreateAsync(manifest, "Offline");
            var initial = await ws.ImportAsync(source, "DBC", "Signals");
            Assert.True(initial.IsValid);
            Assert.Equal(1, initial.Asset.Revision);
            Assert.True(File.Exists(initial.FullPath));

            var next = await ws.PublishRevisionAsync(initial.Asset.Id, Encoding.UTF8.GetBytes(Dbc + Environment.NewLine));
            Assert.True(next.IsValid);
            Assert.Equal(initial.Asset.Id, next.Asset.Id);
            Assert.Equal(2, next.Asset.Revision);
            Assert.NotEqual(initial.FullPath, next.FullPath);
            Assert.True(File.Exists(initial.FullPath));
            Assert.Equal(3, ws.Definition.Revision);
            await Assert.ThrowsAsync<ProjectDataException>(() =>
                ws.VerifyPinnedAssetAsync(initial.Asset.Id, initial.Asset.Revision, initial.Asset.Sha256));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiagnosticJsonl_RecordsDroppedCountAndRedactsPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "R0Trace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = new R0ProjectDiagnosticJournal(1);
            journal.Record("DBC", "OPEN", "ACCEPTED", project: @"C:\secret\work.dbc");
            journal.Record("DBC", "REPLAY", "REJECTED", "DBC_REPLAY_DLC", rawHex: "0123",
                expected: @"D:\secret\expected", actual: @"C:\secret\actual");
            var target = Path.Combine(root, "trace.jsonl");
            await journal.ExportJsonLinesAsync(target, includeRawHex: false);
            var text = await File.ReadAllTextAsync(target);
            Assert.Equal(1, journal.DroppedCount);
            Assert.Contains("DBC_REPLAY_DLC", text);
            Assert.Contains("LostEvents", text);
            Assert.DoesNotContain("D:\\\\secret", text);
            Assert.DoesNotContain("C:\\\\secret", text);
            Assert.Contains("[OMITTED]", text);
        }
        finally { Directory.Delete(root, true); }
    }
}
