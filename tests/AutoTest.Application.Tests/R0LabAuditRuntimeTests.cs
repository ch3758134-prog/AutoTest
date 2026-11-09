using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Records.Operations;
using Xunit;

namespace AutoTest.Application.Tests;

public sealed class R0LabAuditRuntimeTests
{
    private static string TempRoot() => Path.Combine(Path.GetTempPath(),
        "AutoTest-R0-INT-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ToolNavigationUsesSameHostAuditAndPersistsAcrossRestart()
    {
        var root = TempRoot();
        try
        {
            Guid eventId;
            using (var host = new G4IntegrationHost(new V1Workspace(), root))
            {
                Assert.Same(host.OperationAudit.Store, host.OperationAudit.Store);
                var receipt = await host.OperationAudit.RecordToolOpenedAsync("wave", "Wave / CAN Replay");
                Assert.True(receipt.Durable);
                Assert.False(receipt.AlreadyPresent);
                eventId = receipt.EventId;
                var first = await host.OperationAudit.Store.FindByIdAsync(eventId);
                Assert.NotNull(first);
                Assert.Equal("R0.ToolNavigation", first.Kind);
                Assert.Equal("UI_NAVIGATION_ONLY", first.DataQuality);
                Assert.Equal(AuditOrigin.Unknown, first.Origin);
                Assert.Equal(AuditOutcome.Succeeded, first.Outcome);
                Assert.Equal(0, first.SessionGeneration);
                Assert.Null(first.Trace);
                Assert.Null(first.DeviceId);
                Assert.Null(first.RunId);
            }

            using var reopened = new G4IntegrationHost(new V1Workspace(), root);
            var stored = await reopened.OperationAudit.Store.FindByIdAsync(eventId);
            Assert.NotNull(stored);
            Assert.Equal("R0.ToolNavigation", stored.Kind);
            var export = Path.Combine(root, "audit-export.jsonl");
            var exported = await reopened.OperationAudit.Exporter.ExportJsonlAsync(
                new OperationAuditQuery { Kind = "R0.ToolNavigation" }, export);
            Assert.Equal(1, exported);
            Assert.Contains(eventId.ToString("D"), await File.ReadAllTextAsync(export));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DisallowedPhysicalToolCannotBecomeAuditSuccess()
    {
        var root = TempRoot();
        try
        {
            var runtime = new R0LabAuditRuntime(root);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                runtime.RecordToolOpenedAsync("physical-tx", "Physical CAN TX"));
            var page = await runtime.Store.QueryAsync(new OperationAuditQuery());
            Assert.Empty(page.Events);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FailedToolConstructionIsDistinctFromTransportFailure()
    {
        var root = TempRoot();
        try
        {
            var audit = new R0LabAuditRuntime(root);
            var receipt = await audit.RecordToolActivationFailureAsync(
                "dbc", new InvalidOperationException("Widget construction rejected"));
            Assert.True(receipt.Durable);
            var entry = await audit.Store.FindByIdAsync(receipt.EventId);
            Assert.NotNull(entry);
            Assert.Equal(AuditOutcome.Failed, entry.Outcome);
            Assert.Equal(AuditLevel.Error, entry.Level);
            Assert.Equal(AuditOrigin.Unknown, entry.Origin);
            Assert.Equal("Desktop.ToolActivation", entry.FailureStage);
            Assert.Null(entry.Trace);
            Assert.Contains("InvalidOperationException", entry.DetailJson);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SqliteFailurePropagatesInsteadOfReportingSaved()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        var blockedDirectory = Path.Combine(root, "blocked");
        await File.WriteAllTextAsync(blockedDirectory, "Not a directory");
        try
        {
            var runtime = new R0LabAuditRuntime(blockedDirectory);
            await Assert.ThrowsAnyAsync<IOException>(() =>
                runtime.RecordToolOpenedAsync("uds", "UDS Mock"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
