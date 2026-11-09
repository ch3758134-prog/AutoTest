using System.IO.Compression;
using System.Text.Json;
using AutoTest.Records.Operations;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class OperationDiagnosticBundleTests
{
    [Fact]
    public async Task ExportReopensVerifiedRawTraceAndPreservesOriginWithoutLeakingOperatorOrDetail()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var archive = new OperationTraceArchive(Path.Combine(temp.Root, "traces"));
        var operation = Guid.NewGuid();
        var pointer = await archive.AppendAsync(new OperationRawTrace
        {
            OperationId = operation,
            OccurredUtc = DateTimeOffset.UtcNow,
            Direction = "RX", Transport = "USB", Channel = "DUT_CAN",
            Message = "WinUsb_Initialize failed; token=visible-in-source",
            PayloadHexRedacted = "<redacted>"
        });
        await store.AppendAsync(new OperationAuditEvent
        {
            EventId = Guid.NewGuid(), OperationId = operation, CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "USB", Origin = AuditOrigin.Physical,
            Outcome = AuditOutcome.Failed, Level = AuditLevel.Error, Summary = "password=unsafe-123",
            DetailJson = "{\"SensitiveOperatorNote\":\"private\"}", Actor = "person@example.com",
            DeviceId = "ECU-SENSITIVE-ALPHA", RunId = "RUN-SENSITIVE-BETA",
            FailureStage = "WinUsb_Initialize", NativeErrorCode = 31, Trace = pointer
        });
        var path = Path.Combine(temp.Root, "diag.zip");
        var result = await new OperationDiagnosticBundleExporter(store, archive)
            .ExportAsync(new OperationAuditQuery(), path);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.VerifiedTraceCount);
        Assert.True(result.Complete);

        using var zip = ZipFile.OpenRead(path);
        var lines = await ReadEntryAsync(zip, "audit/events.jsonl");
        using (var parsedEvent = JsonDocument.Parse(lines.Trim()))
            Assert.Equal((int)AuditOrigin.Physical, parsedEvent.RootElement.GetProperty("Origin").GetInt32());
        Assert.DoesNotContain("person@example.com", lines);
        Assert.DoesNotContain("ECU-SENSITIVE-ALPHA", lines);
        Assert.DoesNotContain("RUN-SENSITIVE-BETA", lines);
        Assert.Contains("anon-", lines);
        Assert.DoesNotContain("SensitiveOperatorNote", lines);
        Assert.DoesNotContain("unsafe-123", lines);
        Assert.Contains("[REDACTED]", lines);
        var raw = await ReadEntryAsync(zip, "trace/000000.json");
        Assert.Contains("WinUsb_Initialize", raw);
        Assert.DoesNotContain("visible-in-source", raw);
        Assert.Contains("[REDACTED]", raw);
        using var manifest = JsonDocument.Parse(await ReadEntryAsync(zip, "manifest.json"));
        Assert.True(manifest.RootElement.GetProperty("evidenceComplete").GetBoolean());
        Assert.Equal("UNKNOWN_NOT_SUPPLIED", manifest.RootElement.GetProperty("softwareGitCommit").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("verifiedTraces").GetInt32());
        Assert.Contains("Verified", await ReadEntryAsync(zip, "audit/evidence-index.json"));
    }

    [Fact]
    public async Task MissingTraceStaysExplicitlyIncompleteAndDoesNotBlockOtherFacts()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var archive = new OperationTraceArchive(Path.Combine(temp.Root, "traces"));
        var operation = Guid.NewGuid();
        var pointer = await archive.AppendAsync(new OperationRawTrace
        {
            OperationId = operation, OccurredUtc = DateTimeOffset.UtcNow,
            Transport = "CAN", Direction = "RX"
        });
        await store.AppendAsync(new OperationAuditEvent
        {
            EventId = Guid.NewGuid(), OperationId = operation, CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "CanRx", Summary = "stale evidence", Trace = pointer
        });
        File.Delete(Path.Combine(temp.Root, "traces", pointer.RelativeFile));
        var path = Path.Combine(temp.Root, "failed.zip");
        var receipt = await new OperationDiagnosticBundleExporter(store, archive)
            .ExportAsync(new OperationAuditQuery(), path);
        Assert.Equal(1, receipt.UnavailableTraceCount);
        Assert.False(receipt.Complete);
        using var zip = ZipFile.OpenRead(path);
        Assert.Contains("Missing", await ReadEntryAsync(zip, "audit/evidence-index.json"));
        Assert.False(JsonDocument.Parse(await ReadEntryAsync(zip, "manifest.json"))
            .RootElement.GetProperty("evidenceComplete").GetBoolean());
        Assert.Single(zip.Entries.Where(e => e.FullName == "audit/events.jsonl"));
    }

    [Fact]
    public async Task FilteredExportDoesNotMixMockAndPhysicalOrOverwritePreviousReport()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        foreach (var origin in new[] { AuditOrigin.Mock, AuditOrigin.Physical, AuditOrigin.Replay })
            await store.AppendAsync(new OperationAuditEvent
            {
                EventId = Guid.NewGuid(), OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
                OccurredUtc = DateTimeOffset.UtcNow, Kind = "TEST", Summary = origin.ToString(), Origin = origin
            });
        var path = Path.Combine(temp.Root, "mock-only.zip");
        var exporter = new OperationDiagnosticBundleExporter(store,
            new OperationTraceArchive(Path.Combine(temp.Root, "traces")));
        var receipt = await exporter.ExportAsync(new OperationAuditQuery { Origin = AuditOrigin.Mock }, path);
        Assert.Equal(1, receipt.EventCount);
        Assert.Equal(1, receipt.EventsWithoutTrace);
        Assert.False(receipt.Complete); // A summary-only audit is not a complete raw diagnostic.
        using (var zip = ZipFile.OpenRead(path))
        {
            var timeline = await ReadEntryAsync(zip, "audit/events.jsonl");
            Assert.Contains("Mock", timeline);
            Assert.DoesNotContain("Physical", timeline);
            Assert.DoesNotContain("Replay", timeline);
            using var manifest = JsonDocument.Parse(await ReadEntryAsync(zip, "manifest.json"));
            Assert.False(manifest.RootElement.GetProperty("evidenceComplete").GetBoolean());
            Assert.True(manifest.RootElement.GetProperty("allReferencedTracesVerified").GetBoolean());
        }
        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(new OperationAuditQuery(), path));
        Assert.Single(Directory.GetFiles(temp.Root, "*.zip"));
    }

    [Fact]
    public async Task TraceFromOtherOperationIsRejectedAsEvidenceNotExported()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var archive = new OperationTraceArchive(Path.Combine(temp.Root, "traces"));
        var pointer = await archive.AppendAsync(new OperationRawTrace
        {
            OperationId = Guid.NewGuid(), OccurredUtc = DateTimeOffset.UtcNow,
            Transport = "USB", Direction = "RX"
        });
        await store.AppendAsync(new OperationAuditEvent
        {
            EventId = Guid.NewGuid(), OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "USB", Summary = "wrong trace", Trace = pointer
        });
        var path = Path.Combine(temp.Root, "mismatch.zip");
        var receipt = await new OperationDiagnosticBundleExporter(store, archive)
            .ExportAsync(new OperationAuditQuery(), path);
        Assert.Equal(1, receipt.UnavailableTraceCount);
        using var zip = ZipFile.OpenRead(path);
        Assert.Contains("CorruptOrMismatched", await ReadEntryAsync(zip, "audit/evidence-index.json"));
        Assert.Empty(zip.Entries.Where(e => e.FullName.StartsWith("trace/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExportIncludesSuppliedRevisionsWithoutClaimingPhysicalObservation()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        await store.AppendAsync(new OperationAuditEvent
        {
            EventId = Guid.NewGuid(), OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "Calibration", Summary = "Config revision",
            Origin = AuditOrigin.Replay
        });
        var archive = new OperationTraceArchive(Path.Combine(temp.Root, "traces"));
        var exporter = new OperationDiagnosticBundleExporter(store, archive,
            new DiagnosticBundleContext("cdbf7d6", "DemoProject", "EPB-Example", "rev-test-42", "R0 Mock"));
        var destination = Path.Combine(temp.Root, "revision.zip");
        var receipt = await exporter.ExportAsync(new OperationAuditQuery(), destination);
        Assert.False(receipt.Complete);
        using var zip = ZipFile.OpenRead(destination);
        using var manifest = JsonDocument.Parse(await ReadEntryAsync(zip, "manifest.json"));
        Assert.Equal("cdbf7d6", manifest.RootElement.GetProperty("softwareGitCommit").GetString());
        Assert.Equal("rev-test-42", manifest.RootElement.GetProperty("resourceRevision").GetString());
        Assert.True(manifest.RootElement.GetProperty("deviceAndRunIdentifiersPseudonymized").GetBoolean());
        Assert.NotEqual("DemoProject", manifest.RootElement.GetProperty("projectId").GetString());
        Assert.False(manifest.RootElement.GetProperty("physicalObservationVerified").GetBoolean());
    }

    private static async Task<string> ReadEntryAsync(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException(name);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
