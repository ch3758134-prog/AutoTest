using AutoTest.Records.Operations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class OperationAuditTests
{
    private static OperationAuditEvent Event(string kind = "ConnectAttempt", AuditOrigin origin = AuditOrigin.Unknown) => new()
    {
        EventId = Guid.NewGuid(), OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
        OccurredUtc = DateTimeOffset.UtcNow, Kind = kind, Origin = origin, Level = AuditLevel.Information,
        Outcome = AuditOutcome.Started, SessionGeneration = 0,
        Summary = "VID 1183 / PID 5850 连接前检查"
    };

    [Fact]
    public async Task FailedWinUsbConnectionSurvivesRestartWithoutDevice()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var failure = Event("NativeFailure", AuditOrigin.Physical) with
        {
            SessionGeneration = 2, Level = AuditLevel.Error, Outcome = AuditOutcome.Failed,
            FailureStage = "WinUsb_Initialize", NativeErrorCode = 31,
            Summary = "连接失败，无 PONG/版本响应"
        };
        Assert.True((await store.AppendAsync(failure)).Durable);
        var reopened = new SqliteOperationAuditStore(temp.Db);
        var query = await reopened.QueryAsync(new OperationAuditQuery { FailuresOnly = true });
        var actual = Assert.Single(query.Events);
        Assert.Equal(31, actual.NativeErrorCode);
        Assert.Equal("WinUsb_Initialize", actual.FailureStage);
        Assert.Equal(AuditOrigin.Physical, actual.Origin);
        Assert.Equal(AuditOutcome.Failed, actual.Outcome);
        Assert.Null(actual.RunId);
    }

    [Fact]
    public async Task SameEventIdSamePayloadIsIdempotentAndConflictNeverOverwrites()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var evt = Event();
        Assert.False((await store.AppendAsync(evt)).AlreadyPresent);
        Assert.True((await store.AppendAsync(evt)).AlreadyPresent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(evt with { Summary = "changed" }));
        Assert.Equal(evt.Summary, (await store.FindByIdAsync(evt.EventId))!.Summary);
    }

    [Fact]
    public async Task ConcurrentAppendThenKeysetPagingNoMissingOrDuplicatedEvents()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var at = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 53).Select(i => Event("CanRx") with
        {
            OccurredUtc = at, Summary = "frame #" + i, Origin = AuditOrigin.Replay
        }).ToArray();
        await Task.WhenAll(events.Select(e => store.AppendAsync(e)));
        AuditCursor? cursor = null;
        var ids = new List<Guid>();
        do
        {
            var page = await store.QueryAsync(new OperationAuditQuery { Before = cursor, PageSize = 10 });
            ids.AddRange(page.Events.Select(e => e.EventId));
            cursor = page.NextCursor;
        } while (cursor != null);
        Assert.Equal(53, ids.Count);
        Assert.Equal(53, ids.Distinct().Count());
    }

    [Fact]
    public async Task FilterOriginFailureAndOperationIsExact()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var a = Event("USB");
        var b = Event("UDS", AuditOrigin.Mock) with { Outcome = AuditOutcome.TimedOut, Level = AuditLevel.Error };
        var c = Event("Flash", AuditOrigin.Replay) with { Outcome = AuditOutcome.Denied };
        await store.AppendAsync(a); await store.AppendAsync(b); await store.AppendAsync(c);
        Assert.Single((await store.QueryAsync(new OperationAuditQuery { Origin = AuditOrigin.Mock, FailuresOnly = true })).Events);
        Assert.Empty((await store.QueryAsync(new OperationAuditQuery { OperationId = a.OperationId, Origin = AuditOrigin.Mock })).Events);
        Assert.Equal(c.EventId, Assert.Single((await store.QueryAsync(new OperationAuditQuery { FailuresOnly = true, Origin = AuditOrigin.Replay })).Events).EventId);
    }

    [Fact]
    public async Task ExistingRowsCannotBeUpdatedOrDeletedThroughSqlite()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var evt = Event();
        await store.AppendAsync(evt);
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = temp.Db,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE operation_audit_events SET summary='rewritten' WHERE event_id=$id";
        command.Parameters.AddWithValue("$id", evt.EventId.ToString("D"));
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        command.CommandText = "DELETE FROM operation_audit_events WHERE event_id=$id";
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task InvalidAuditAndFailedDatabaseDoNotGiveDurableReceipt()
    {
        using var temp = new AuditTemp();
        var store = new SqliteOperationAuditStore(temp.Db);
        var invalid = Event() with { OccurredUtc = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)) };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync(invalid));
        var file = Path.Combine(temp.Root, "not-a-directory");
        await File.WriteAllTextAsync(file, "occupied");
        var broken = new SqliteOperationAuditStore(Path.Combine(file, "audit.db"));
        await Assert.ThrowsAnyAsync<Exception>(() => broken.AppendAsync(Event()));
    }

    [Fact]
    public void ManualVoltageIsFiveMeasuredChannelsNotInferredFromAck()
    {
        var record = new ManualVoltageEvidence("ctrl", "state", "revision-4", "board", "multimeter",
            DateTimeOffset.UtcNow, Enumerable.Range(1, 5)
                .Select(i => new VoltageMeasurement(i, i == 3 ? null : 1.2m, "J5-" + i)).ToArray());
        Assert.Contains("MeterId", record.ToDetailJson());
        Assert.Throws<InvalidDataException>(() => (record with { Channels = record.Channels.Take(4).ToArray() }).Validate());
    }
}

internal sealed class AuditTemp : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "autotest-audit-" + Guid.NewGuid().ToString("N"));
    public string Db => Path.Combine(Root, "operation-audit.sqlite");
    public AuditTemp() => Directory.CreateDirectory(Root);
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
