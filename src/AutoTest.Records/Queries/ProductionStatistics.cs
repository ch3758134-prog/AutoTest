using AutoTest.Records.Model;
using AutoTest.Records.Storage;

namespace AutoTest.Records.Queries;

/// <summary>Never equate distinct successful DUTs with physical shipment without a release fact.</summary>
public sealed record ProductionSummary(long RunCount, long DistinctDutCount,
    long CommittedRuns, long PassedRuns, long FailedRuns, long InconclusiveRuns,
    long InterruptedRuns, long WritingRuns);

/// <summary>
/// Historical repeat detection only. Does NOT certify a product retest, physical execution or shipment.
/// A repeat means a normalized DUT ID was seen in an earlier run of the SAME mode, even before
/// the requested reporting window. The identity-rule migration policy is owned by Production.
/// </summary>
public sealed record RepeatDetectionSummary(RecordRunMode Mode, long RunCount,
    long DistinctDutCount, long FirstSeenAttempts, long RepeatAttempts, long RepeatedDutCount)
{
    public string ReleaseAssessment => "NOT_EVALUATED_NO_RELEASE_CONTRACT";
}

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

    /// <summary>
    /// Count first-seen and repeated identifiers against the complete history of the given run mode,
    /// then filter the requested UTC window. Applying the date filter before ROW_NUMBER would falsely
    /// classify a repeat as an initial attempt at the start of a new reporting day.
    /// </summary>
    public async Task<RepeatDetectionSummary> GetRepeatDetectionSummaryAsync(
        RecordRunMode mode = RecordRunMode.Production, DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(mode) ||
            fromUtc is { } from && from.Offset != TimeSpan.Zero ||
            toUtc is { } to && to.Offset != TimeSpan.Zero ||
            fromUtc > toUtc)
            throw new ArgumentException("Repeat detection statistics require a valid mode and ordered UTC range.");

        await using var connection = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await RecordDatabase.EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH all_attempts AS (
                SELECT dut_id, run_id, started_utc,
                       ROW_NUMBER() OVER (
                           PARTITION BY dut_id ORDER BY started_utc, run_id
                       ) AS prior_ordinal
                  FROM run_headers
                 WHERE run_mode = $mode
            ), scoped_attempts AS (
                SELECT dut_id, prior_ordinal
                  FROM all_attempts
                 WHERE ($from IS NULL OR started_utc >= $from)
                   AND ($to IS NULL OR started_utc <= $to)
            )
            SELECT COUNT(*),
                   COUNT(DISTINCT dut_id),
                   COALESCE(SUM(CASE WHEN prior_ordinal = 1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN prior_ordinal > 1 THEN 1 ELSE 0 END), 0),
                   COUNT(DISTINCT CASE WHEN prior_ordinal > 1 THEN dut_id END)
              FROM scoped_attempts;
            """;
        RecordDatabase.Add(command, "$mode", mode.ToString());
        RecordDatabase.Add(command, "$from", (object?)fromUtc?.ToString("O") ?? DBNull.Value);
        RecordDatabase.Add(command, "$to", (object?)toUtc?.ToString("O") ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new IOException("Repeat detection statistics query failed.");
        return new RepeatDetectionSummary(mode, reader.GetInt64(0), reader.GetInt64(1),
            reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    /// <summary>Day is a UTC reporting day; historical repeat classification still spans prior days.</summary>
    public Task<RepeatDetectionSummary> GetDailyRepeatDetectionSummaryAsync(
        DateOnly utcDate, RecordRunMode mode = RecordRunMode.Production, CancellationToken ct = default)
    {
        var start = new DateTimeOffset(utcDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return GetRepeatDetectionSummaryAsync(mode, start, start.AddDays(1).AddTicks(-1), ct);
    }
}
