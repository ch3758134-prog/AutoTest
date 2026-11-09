using AutoTest.Records.Model;
using AutoTest.Records.Storage;

namespace AutoTest.Records.Queries;

/// <summary>Never equate distinct successful DUTs with physical shipment without a release fact.</summary>
public sealed record ProductionSummary(long RunCount, long DistinctDutCount,
    long CommittedRuns, long PassedRuns, long FailedRuns, long InconclusiveRuns,
    long InterruptedRuns, long WritingRuns);

public sealed class ProductionStatistics
{
    private readonly RecordDatabase _db;
    public ProductionStatistics(RecordDatabase db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<ProductionSummary> GetSummaryAsync(RecordRunMode mode = RecordRunMode.Production,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken ct = default)
    {
        if (fromUtc?.Offset != TimeSpan.Zero && fromUtc is not null ||
            toUtc?.Offset != TimeSpan.Zero && toUtc is not null || fromUtc > toUtc)
            throw new ArgumentException("Statistics requires an ordered UTC range.");
        await using var conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await RecordDatabase.EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), COUNT(DISTINCT dut_id),
              COALESCE(SUM(CASE WHEN persist_state='Committed' THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN persist_state='Committed' AND verdict='Pass' THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN persist_state='Committed' AND verdict='Fail' THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN persist_state='Committed' AND verdict IN ('Error','Inconclusive','Skipped') THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN persist_state='Interrupted' THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN persist_state='Writing' THEN 1 ELSE 0 END),0)
            FROM run_headers
            WHERE run_mode=$mode AND ($from IS NULL OR started_utc >= $from)
              AND ($to IS NULL OR started_utc <= $to);
            """;
        RecordDatabase.Add(cmd, "$mode", mode.ToString());
        RecordDatabase.Add(cmd, "$from", (object?)fromUtc?.ToString("O") ?? DBNull.Value);
        RecordDatabase.Add(cmd, "$to", (object?)toUtc?.ToString("O") ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) throw new IOException("Statistics query failed.");
        return new ProductionSummary(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7));
    }

    public async Task<long> CountAttemptsAsync(RecordRunMode mode = RecordRunMode.Production,
        CancellationToken ct = default) => (await GetSummaryAsync(mode, ct: ct).ConfigureAwait(false)).RunCount;

    public async Task<long> CountDistinctDutsAsync(RecordRunMode mode = RecordRunMode.Production,
        CancellationToken ct = default) => (await GetSummaryAsync(mode, ct: ct).ConfigureAwait(false)).DistinctDutCount;
}
