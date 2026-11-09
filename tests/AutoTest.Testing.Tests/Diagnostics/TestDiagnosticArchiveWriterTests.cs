using AutoTest.Testing.Authoring;
using AutoTest.Testing.Diagnostics;
using Xunit;

namespace AutoTest.Testing.Tests.Diagnostics;

public sealed class TestDiagnosticArchiveWriterTests
{
    [Fact]
    public void RedactedDiagnosticSnapshotCanBePersistedAndReadBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest_TestDiag", Guid.NewGuid().ToString("N"));
        try
        {
            var doc = TestDocument.Create("PRIVATE_TEST_PROGRAM");
            var events = new[] { TestDiagnosticArchive.Operation("", "PREFLIGHT_BLOCKED", "PRIVATE_ECU_ID") };
            var redacted = TestDiagnosticArchive.Build(doc, "preflight", null, events, redactValues: true)
                with { EvidenceCompleteness = "LOCAL_BEST_EFFORT_SNAPSHOT" };
            var path = TestDiagnosticArchiveWriter.Save(folder, redacted);
            Assert.True(File.Exists(path));
            Assert.EndsWith(".testdiag.json", path, StringComparison.Ordinal);
            var text = File.ReadAllText(path);
            Assert.Contains("PREFLIGHT_BLOCKED", text);
            Assert.Contains("[REDACTED]", text);
            Assert.DoesNotContain("PRIVATE_ECU_ID", text);
            Assert.DoesNotContain("PRIVATE_TEST_PROGRAM", text);
            var identified = TestDiagnosticArchive.Build(doc, "preflight", null, events,
                redactValues: true, projectId: "PRIVATE_PROJECT", dutProfile: "PRIVATE_ECU",
                stationId: "PRIVATE_STATION", sessionGeneration: "4");
            var identifiedJson = TestDiagnosticArchive.ToJson(identified);
            Assert.DoesNotContain("PRIVATE_PROJECT", identifiedJson);
            Assert.DoesNotContain("PRIVATE_ECU", identifiedJson);
            Assert.DoesNotContain("PRIVATE_STATION", identifiedJson);
            Assert.Equal("UNBOUND", redacted.ProjectId);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
            Assert.Throws<ArgumentException>(() => TestDiagnosticArchiveWriter.Save("relative-folder", redacted));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
