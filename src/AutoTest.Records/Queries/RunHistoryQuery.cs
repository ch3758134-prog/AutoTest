using System.Collections.Immutable;
using System.Globalization;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Storage;
using Microsoft.Data.Sqlite;

namespace AutoTest.Records.Queries;

public sealed record RunHistoryEntry(RunId Run, string DutId, RecordRunMode Mode,
    DateTimeOffset StartedUtc, DateTimeOffset? CompletedUtc,
    RecordPersistenceState Persistence, string? TerminalState, Verdict? Verdict,
    string SnapshotJson, string SnapshotSha256, long FactCount, long StepFactCount,
    string? FailureReason);

public sealed record RunHistoryFilter(string? DutId = null, RecordRunMode? Mode = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null,
    Verdict? Verdict = null, int Limit = 100);

public sealed class RunHistoryQuery
{
    private readonly RecordDatabase _db;
    public RunHistoryQuery(RecordDatabase db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<RunHistoryEntry?> FindByRunAsync(RunId run, CancellationToken ct = default)
    {
        if (run.Value == Guid.Empty) throw new ArgumentException("Missing RunId.", nameof(run));
        await using var conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await RecordDatabase.EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Columns + " WHERE h.run_id=$run;";
        RecordDatabase.Add(cmd, "$run", run.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    public Task<ImmutableArray<RunHistoryEntry>> FindByDutAsync(string dutId, int limit = 100,
        CancellationToken ct = default) => FilterAsync(new RunHistoryFilter(DutId: dutId, Limit: limit), ct);

    public async Task<ImmutableArray<RunHistoryEntry>> FilterAsync(RunHistoryFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit is <= 0 or > 10000 ||
            filter.FromUtc is { } from && from.Offset != TimeSpan.Zero ||
            filter.ToUtc is { } to && to.Offset != TimeSpan.Zero ||
            filter.FromUtc > filter.ToUtc)
            throw new ArgumentOutOfRangeException(nameof(filter));
        await using var conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await RecordDatabase.EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var clauses = new List<string>();
        if (filter.DutId is not null) { clauses.Add("h.dut_id=$dut"); RecordDatabase.Add(cmd, "$dut", filter.DutId); }
        if (filter.Mode is { } mode) { clauses.Add("h.run_mode=$mode"); RecordDatabase.Add(cmd, "$mode", mode.ToString()); }
        if (filter.Verdict is { } verdict) { clauses.Add("h.verdict=$verdict"); RecordDatabase.Add(cmd, "$verdict", verdict.ToString()); }
        if (filter.FromUtc is { } fromUtc) { clauses.Add("h.started_utc >= $from"); RecordDatabase.Add(cmd, "$from", fromUtc.ToString("O")); }
        if (filter.ToUtc is { } toUtc) { clauses.Add("h.started_utc <= $to"); RecordDatabase.Add(cmd, "$to", toUtc.ToString("O")); }
        cmd.CommandText = Columns + (clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses)) +
            " ORDER BY h.started_utc DESC,h.run_id ASC LIMIT $limit;";
        RecordDatabase.Add(cmd, "$limit", filter.Limit);
        var results = ImmutableArray.CreateBuilder<RunHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) results.Add(ReadEntry(reader));
        return results.ToImmutable();
    }

    private const string Columns = """
        SELECT h.run_id,h.dut_id,h.run_mode,h.started_utc,h.completed_utc,h.persist_state,
               h.terminal_state,h.verdict,h.snapshot_json,h.snapshot_hash,h.last_sequence,h.failure_reason,
               (SELECT COUNT(*) FROM run_step_facts sf WHERE sf.run_id=h.run_id)
        FROM run_headers h
        """;

    private static RunHistoryEntry ReadEntry(SqliteDataReader r)
    {
        var snapshot = r.GetString(8);
        var hash = r.GetString(9);
        if (RecordDatabase.Hash(snapshot) != hash)
            throw new InvalidDataException("RunSnapshot hash mismatch: stored history cannot be trusted.");
        return new RunHistoryEntry(RunId.Create(Guid.Parse(r.GetString(0))), r.GetString(1),
            Enum.Parse<RecordRunMode>(r.GetString(2)),
            DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture),
            r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture),
            Enum.Parse<RecordPersistenceState>(r.GetString(5)),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : Enum.Parse<Verdict>(r.GetString(7)),
            snapshot, hash, r.GetInt64(10), r.GetInt64(12),
            r.IsDBNull(11) ? null : r.GetString(11));
    }
}
