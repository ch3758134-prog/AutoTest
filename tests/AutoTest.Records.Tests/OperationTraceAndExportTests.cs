using AutoTest.Records.Operations;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class OperationTraceAndExportTests
{
    private static OperationRawTrace Raw(Guid id) => new()
    {
        OperationId = id, OccurredUtc = DateTimeOffset.UtcNow,
        Transport = "USB", Direction = "RX", Message = "Native WinUSB error: no response",
        PayloadHexRedacted = "<redacted>"
    };
    private static OperationAuditEvent Audit(Guid id, AuditTraceReference? trace) => new()
    {
        EventId = Guid.NewGuid(), OperationId = id, CorrelationId = Guid.NewGuid(),
        OccurredUtc = DateTimeOffset.UtcNow, Kind = "NativeFailure", Origin = AuditOrigin.Physical,
        Outcome = AuditOutcome.Failed, Level = AuditLevel.Error, Summary = "=unsafe-csv-formula",
        FailureStage = "WinUsb_Initialize", Trace = trace
    };

    [Fact]
    public async Task ReferencedTraceReopensAndReverseFindsCorrelatedAudit()
    {
        using var temp = new AuditTemp();
        var writer = new OperationTraceArchive(Path.Combine(temp.Root,"trace"));
        var operation = Guid.NewGuid();
        var pointer = await writer.AppendAsync(Raw(operation));
        var store = new SqliteOperationAuditStore(temp.Db);
        var evt = Audit(operation, pointer);
        await store.AppendAsync(evt);
        var reopenedReader = new OperationTraceArchive(Path.Combine(temp.Root,"trace"));
        var reopenedStore = new SqliteOperationAuditStore(temp.Db);
        Assert.Equal(operation, (await reopenedReader.ReadAsync(pointer)).OperationId);
        Assert.Equal(evt.EventId, Assert.Single(await reopenedStore.FindByTraceAsync(pointer)).EventId);
    }

    [Fact]
    public async Task TamperedTraceIsRejectedRatherThanShownAsVerified()
    {
        using var temp = new AuditTemp();
        var archive = new OperationTraceArchive(Path.Combine(temp.Root,"trace"));
        var trace = await archive.AppendAsync(Raw(Guid.NewGuid()));
        var name = Path.Combine(temp.Root, "trace", trace.RelativeFile);
        var bytes = await File.ReadAllBytesAsync(name);
        bytes[(int)trace.Offset] ^= 1;
        await File.WriteAllBytesAsync(name,bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => archive.ReadAsync(trace));
    }

    [Fact]
    public async Task ExplicitSecretsCannotBePersistedAsRawTrace()
    {
        using var temp = new AuditTemp();
        var archive = new OperationTraceArchive(Path.Combine(temp.Root,"trace"));
        await Assert.ThrowsAsync<InvalidDataException>(() => archive.AppendAsync(Raw(Guid.NewGuid()) with
        {
            ContainsSecrets = true
        }));
    }

    [Fact]
    public async Task ExportCsvAndJsonlStreamAllRecordsAndProtectExistingFile()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        for (var i = 0; i < 37; ++i) await store.AppendAsync(Audit(Guid.NewGuid(), null));
        var exporter = new OperationAuditExporter(store);
        var csv = Path.Combine(temp.Root,"audit.csv");
        var jsonl = Path.Combine(temp.Root,"audit.jsonl");
        Assert.Equal(37, await exporter.ExportCsvAsync(new OperationAuditQuery { PageSize = 3 }, csv));
        Assert.Equal(37, await exporter.ExportJsonlAsync(new OperationAuditQuery { PageSize = 3 }, jsonl));
        Assert.Equal(38, (await File.ReadAllLinesAsync(csv)).Length);
        Assert.Equal(37, (await File.ReadAllLinesAsync(jsonl)).Length);
        Assert.Contains("'=unsafe-csv-formula", await File.ReadAllTextAsync(csv));
        await Assert.ThrowsAsync<IOException>(() => exporter.ExportCsvAsync(new OperationAuditQuery(), csv));
    }

    [Fact]
    public async Task RecorderRetriesSameEventWithoutDuplicatingTraceAndRejectsChangedPayload()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var archive = new OperationTraceArchive(Path.Combine(temp.Root, "trace"));
        var recorder = new OperationAuditRecorder(store, archive);
        var operationId = Guid.NewGuid();
        var entry = Raw(operationId);
        var request = new OperationAuditRequest
        {
            EventId = Guid.NewGuid(), OperationId = operationId, CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "USB", Summary = "USB response",
            RawTrace = entry
        };
        Assert.False((await recorder.RecordAsync(request)).AlreadyPresent);
        Assert.True((await recorder.RecordAsync(request)).AlreadyPresent);
        Assert.Single((await File.ReadAllLinesAsync(Path.Combine(temp.Root, "trace",
            "trace-" + entry.OccurredUtc.ToString("yyyyMMdd") + ".jsonl"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RecordAsync(request with
        { RawTrace = entry with { Message = "changed response" } }));
    }

    [Fact]
    public async Task TraceWriterCanAppendMultipleEntriesWithoutOverwriting()
    {
        using var temp = new AuditTemp();
        var archive = new OperationTraceArchive(Path.Combine(temp.Root,"trace"));
        var pointers = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => archive.AppendAsync(Raw(Guid.NewGuid()))));
        Assert.Equal(25, pointers.Select(x => x.Offset).Distinct().Count());
        foreach (var pointer in pointers) Assert.NotEqual(Guid.Empty, (await archive.ReadAsync(pointer)).OperationId);
    }
}
