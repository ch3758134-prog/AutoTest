using AutoTest.Desktop.Tools.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class FormalR2WorkspaceTests
{
    private static TestDocument Sample()
    {
        var edit = new StepEditTransaction(TestDocument.Create("PRIVATE_PROGRAM"));
        edit.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("MockObserve").WithProperty("variable", "signal")
                .WithProperty("value", "PRIVATE_VALUE").WithProperty("quality", "Observed"));
        edit.Insert(TestPhase.Test, null, null, 1,
            StepNode.Create("Assert").WithProperty("variable", "signal")
                .WithProperty("equals", "PRIVATE_VALUE"));
        return edit.Current;
    }

    [Fact]
    public async Task FormalMockResultIsAutoSavedRedactedEvenWhenManualExportIsUnredacted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest_FormalR2", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new FormalTestWorkspaceViewModel(Sample(), diagnosticsDirectory: folder);
            vm.RedactValuesOnExport = false;
            await vm.RunMockAsync();
            Assert.Equal("PASS", vm.Verdict);
            Assert.NotEmpty(vm.LastEvidencePath);
            Assert.True(File.Exists(vm.LastEvidencePath));
            Assert.Contains("PRIVATE_VALUE", vm.ExportEvidenceJson());
            var saved = File.ReadAllText(vm.LastEvidencePath);
            Assert.DoesNotContain("PRIVATE_VALUE", saved);
            Assert.DoesNotContain("PRIVATE_PROGRAM", saved);
            Assert.Contains("[REDACTED]", saved);
            Assert.Equal("REDACTED_LOCAL_BEST_EFFORT_SAVED", vm.CreateEvidenceBundle().EvidenceCompleteness);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void PreflightBlockedProducesDefaultRedactedDiagnosticFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest_FormalR2", Guid.NewGuid().ToString("N"));
        try
        {
            var edit = new StepEditTransaction(TestDocument.Create("unsafe"));
            edit.Insert(TestPhase.Test, null, null, 0, StepNode.Create("Flash"));
            var vm = new FormalTestWorkspaceViewModel(edit.Current, diagnosticsDirectory: folder);
            Assert.Contains("BLOCKED", vm.Preflight());
            Assert.True(File.Exists(vm.LastEvidencePath));
            var json = File.ReadAllText(vm.LastEvidencePath);
            Assert.Contains("PREFLIGHT_BLOCKED", json);
            Assert.Contains("SOFTWARE", json.ToUpperInvariant());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task TraceWriteFailureIsVisibleAndCannotClaimDurableEvidence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest_FormalR2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var fileInPlaceOfFolder = Path.Combine(folder, "not-a-folder");
        File.WriteAllText(fileInPlaceOfFolder, "occupy");
        try
        {
            var vm = new FormalTestWorkspaceViewModel(Sample(), diagnosticsDirectory: fileInPlaceOfFolder);
            await vm.RunMockAsync();
            Assert.Equal("PASS", vm.Verdict); // A trace I/O failure does not rewrite the Mock business verdict.
            Assert.Empty(vm.LastEvidencePath);
            Assert.Contains("TRACE SAVE FAILED", vm.Status);
            Assert.Contains(vm.Timeline, e => e.EventKind == "TRACE_PERSIST_ERROR");
            Assert.Equal("IN_MEMORY_ONLY_NOT_DURABLE", vm.CreateEvidenceBundle().EvidenceCompleteness);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

public sealed class FormalR2AssetBindingTests
{
    private sealed class StubResolver(AutoTest.Core.AssetRef reference) : AutoTest.Contracts.IAssetResolver
    {
        public AutoTest.Core.OperationResult<AutoTest.Core.AssetRef> Resolve(
            AutoTest.Core.AssetId id, AutoTest.Core.AssetRevision revision) =>
            AutoTest.Core.OperationResult<AutoTest.Core.AssetRef>.Accepted(reference);
    }

    [Fact]
    public async Task ViewModelReceivesExistingTypedResourceResolverWithoutSharedContractChange()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest_FormalR2", Guid.NewGuid().ToString("N"));
        try
        {
            var id = AutoTest.Core.AssetId.New();
            var hash = new string('a', 64);
            var reference = new AutoTest.Core.AssetRef(id, AutoTest.Core.AssetRevision.Create(3),
                AutoTest.Core.RevisionFingerprint.FromSha256(hash));
            var edit = new StepEditTransaction(TestDocument.Create("typed-resource-test"));
            edit.Insert(TestPhase.Test, null, null, 0,
                StepNode.Create("MockObserve").WithProperty("variable", "signal")
                    .WithProperty("value", "ready").WithProperty("quality", "Observed")
                    .WithProperty("assetId", id.ToString()).WithProperty("assetRevision", "3")
                    .WithProperty("assetSha256", hash));
            edit.Insert(TestPhase.Test, null, null, 1,
                StepNode.Create("Assert").WithProperty("variable", "signal")
                    .WithProperty("equals", "ready"));
            var vm = new FormalTestWorkspaceViewModel(edit.Current,
                diagnosticsDirectory: folder, assetResolver: new StubResolver(reference));
            Assert.Contains("READY", vm.Preflight());
            await vm.RunMockAsync();
            Assert.Equal("PASS", vm.Verdict);
            Assert.True(File.Exists(vm.LastEvidencePath));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
