using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Model;
using Microsoft.Data.Sqlite;

namespace AutoTest.Records.Storage;

/// <summary>File-backed, append-only SQLite store. All SQL/provider details stay inside Records.</summary>
public sealed class RecordDatabase
{
    public const int CurrentSchemaVersion = 1;
    public string DatabasePath { get; }

    public RecordDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (databasePath == ":memory:")
            throw new ArgumentException("Historical records require a persistent file", nameof(databasePath));
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        // The schema migration and user_version are committed as one transaction.
        await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 10
        }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, "PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, "PRAGMA busy_timeout=10000;", ct).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, "PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, "PRAGMA synchronous=FULL;", ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await versionCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (version > CurrentSchemaVersion)
            throw new InvalidDataException($"Record schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        if (version == CurrentSchemaVersion) return;
        if (version != 0) throw new InvalidDataException($"No non-destructive migration from schema {version}.");

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var create = connection.CreateCommand();
        create.Transaction = (SqliteTransaction)transaction;
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS run_headers (
              run_id TEXT PRIMARY KEY NOT NULL,
              dut_id TEXT NOT NULL,
              identity_rule TEXT NOT NULL,
              identity_source TEXT NOT NULL,
              run_mode TEXT NOT NULL CHECK(run_mode IN ('Production','Development','Diagnostic')),
              started_utc TEXT NOT NULL,
              project_id TEXT NOT NULL,
              station_id TEXT NOT NULL,
              plan_hash TEXT NOT NULL,
              snapshot_hash TEXT NOT NULL,
              snapshot_json TEXT NOT NULL,
              persist_state TEXT NOT NULL CHECK(persist_state IN ('Writing','Committed','Interrupted')),
              terminal_state TEXT NULL,
              verdict TEXT NULL,
              completed_utc TEXT NULL,
              failure_reason TEXT NULL,
              last_sequence INTEGER NOT NULL DEFAULT 0 CHECK(last_sequence >= 0)
            );
            CREATE INDEX IF NOT EXISTS idx_run_dut_time ON run_headers(dut_id, started_utc);
            CREATE INDEX IF NOT EXISTS idx_run_mode_state_time ON run_headers(run_mode,persist_state,started_utc);
            CREATE TABLE IF NOT EXISTS run_facts (
              run_id TEXT NOT NULL REFERENCES run_headers(run_id),
              sequence INTEGER NOT NULL CHECK(sequence > 0),
              event_id TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              payload_hash TEXT NOT NULL,
              PRIMARY KEY(run_id,sequence),
              UNIQUE(run_id,event_id)
            );
            CREATE TABLE IF NOT EXISTS run_step_facts (
              run_id TEXT NOT NULL REFERENCES run_headers(run_id),
              sequence INTEGER NOT NULL CHECK(sequence > 0),
              event_id TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              payload_hash TEXT NOT NULL,
              PRIMARY KEY(run_id,sequence),
              UNIQUE(run_id,event_id)
            );
            CREATE TABLE IF NOT EXISTS artifact_links (
              run_id TEXT NOT NULL REFERENCES run_headers(run_id),
              artifact_id TEXT NOT NULL,
              relative_path TEXT NOT NULL,
              hash_hex TEXT NOT NULL,
              byte_length INTEGER NOT NULL CHECK(byte_length >= 0),
              kind TEXT NOT NULL,
              media_type TEXT NOT NULL,
              provenance TEXT NOT NULL,
              PRIMARY KEY(run_id,artifact_id)
            );
            CREATE TRIGGER IF NOT EXISTS run_headers_no_delete
            BEFORE DELETE ON run_headers BEGIN SELECT RAISE(ABORT,'run history is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_facts_no_delete
            BEFORE DELETE ON run_facts BEGIN SELECT RAISE(ABORT,'run facts are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_facts_no_update
            BEFORE UPDATE ON run_facts BEGIN SELECT RAISE(ABORT,'run facts are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_step_facts_no_delete
            BEFORE DELETE ON run_step_facts BEGIN SELECT RAISE(ABORT,'step history is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_step_facts_no_update
            BEFORE UPDATE ON run_step_facts BEGIN SELECT RAISE(ABORT,'step history is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_step_facts_only_while_writing
            BEFORE INSERT ON run_step_facts WHEN
              (SELECT persist_state FROM run_headers WHERE run_id=NEW.run_id) != 'Writing'
            BEGIN SELECT RAISE(ABORT,'run is finalized'); END;
            CREATE TRIGGER IF NOT EXISTS artifact_links_no_delete
            BEFORE DELETE ON artifact_links BEGIN SELECT RAISE(ABORT,'artifact links are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS artifact_links_no_update
            BEFORE UPDATE ON artifact_links BEGIN SELECT RAISE(ABORT,'artifact links are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_headers_no_identity_rewrite
            BEFORE UPDATE ON run_headers WHEN
              OLD.run_id != NEW.run_id OR OLD.dut_id != NEW.dut_id OR
              OLD.identity_rule != NEW.identity_rule OR OLD.identity_source != NEW.identity_source OR
              OLD.run_mode != NEW.run_mode OR OLD.started_utc != NEW.started_utc OR
              OLD.project_id != NEW.project_id OR OLD.station_id != NEW.station_id OR
              OLD.plan_hash != NEW.plan_hash OR OLD.snapshot_hash != NEW.snapshot_hash OR
              OLD.snapshot_json != NEW.snapshot_json OR OLD.persist_state != 'Writing'
            BEGIN SELECT RAISE(ABORT,'run header is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS run_facts_only_while_writing
            BEFORE INSERT ON run_facts WHEN
              (SELECT persist_state FROM run_headers WHERE run_id=NEW.run_id) != 'Writing'
            BEGIN SELECT RAISE(ABORT,'run is finalized'); END;
            CREATE TRIGGER IF NOT EXISTS artifact_only_while_writing
            BEFORE INSERT ON artifact_links WHEN
              (SELECT persist_state FROM run_headers WHERE run_id=NEW.run_id) != 'Writing'
            BEGIN SELECT RAISE(ABORT,'run is finalized'); END;
            PRAGMA user_version=1;
            """;
        await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Call once at exclusive process startup, before any new run is begun.</summary>
    public async Task<int> RecoverInterruptedRunsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            UPDATE run_headers SET persist_state='Interrupted',terminal_state='Unknown',
                failure_reason='Process terminated before commit; existing facts preserved'
            WHERE persist_state='Writing';
            """;
        var count = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return count;
    }

    public async Task BeginAsync(RunRecord run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Validate();
        var json = run.CanonicalSnapshotJson();
        var hash = Hash(json);
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO run_headers(run_id,dut_id,identity_rule,identity_source,run_mode,
                started_utc,project_id,station_id,plan_hash,snapshot_hash,snapshot_json,persist_state)
            VALUES($run,$dut,$rule,$source,$mode,$started,$project,$station,$plan,$hash,$json,'Writing')
            ON CONFLICT(run_id) DO NOTHING;
            """;
        Add(cmd, "$run", run.RunId.ToString());
        Add(cmd, "$dut", run.Identity.DutId);
        Add(cmd, "$rule", run.Identity.IdentityRuleFingerprint);
        Add(cmd, "$source", run.Identity.Source);
        Add(cmd, "$mode", run.Mode.ToString());
        Add(cmd, "$started", run.StartedUtc.ToString("O"));
        Add(cmd, "$project", run.Snapshot.Project.Id.ToString());
        Add(cmd, "$station", run.Snapshot.Station.Station.ToString());
        Add(cmd, "$plan", run.Snapshot.PlanFingerprint.Sha256);
        Add(cmd, "$hash", hash);
        Add(cmd, "$json", json);
        var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (inserted == 0)
        {
            await using var exists = conn.CreateCommand();
            exists.Transaction = (SqliteTransaction)tx;
            exists.CommandText = "SELECT snapshot_hash,persist_state FROM run_headers WHERE run_id=$run;";
            Add(exists, "$run", run.RunId.ToString());
            await using var reader = await exists.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
                reader.GetString(0) != hash || reader.GetString(1) != "Writing")
                throw new InvalidOperationException("RunId already exists with a different or finalized snapshot.");
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct = default)
    {
        RunFactTimeline.Validate(fact);
        var json = JsonSerializer.Serialize(FactPayload.FromFact(fact));
        var hash = Hash(json);
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var status = conn.CreateCommand();
        status.Transaction = (SqliteTransaction)tx;
        status.CommandText = "SELECT persist_state,last_sequence FROM run_headers WHERE run_id=$run;";
        Add(status, "$run", fact.Run.ToString());
        string? persistence;
        long seq;
        await using (var reader = await status.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("BeginAsync required before AppendAsync.");
            persistence = reader.GetString(0);
            seq = reader.GetInt64(1);
        }
        // Retry with the same EventId and identical payload is idempotent, even after final commit.
        await using var match = conn.CreateCommand();
        match.Transaction = (SqliteTransaction)tx;
        match.CommandText = "SELECT payload_hash FROM run_facts WHERE run_id=$run AND event_id=$event;";
        Add(match, "$run", fact.Run.ToString());
        Add(match, "$event", fact.EventId.ToString("D"));
        var found = (string?)await match.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (found is not null)
        {
            if (found != hash) throw new InvalidOperationException("EventId collision with different fact.");
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new RecordWriteReceipt(fact.Run, fact.EventId, true, "AlreadyDurable");
        }
        if (persistence != "Writing") throw new InvalidOperationException("Run is no longer writable.");
        await using var insert = conn.CreateCommand();
        insert.Transaction = (SqliteTransaction)tx;
        insert.CommandText = """
            INSERT INTO run_facts(run_id,sequence,event_id,payload_json,payload_hash)
            VALUES($run,$seq,$event,$payload,$hash);
            UPDATE run_headers SET last_sequence=$seq WHERE run_id=$run;
            """;
        Add(insert, "$run", fact.Run.ToString());
        Add(insert, "$seq", checked(seq + 1));
        Add(insert, "$event", fact.EventId.ToString("D"));
        Add(insert, "$payload", json);
        Add(insert, "$hash", hash);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new RecordWriteReceipt(fact.Run, fact.EventId, true, "AppendedDurably");
    }

    public async Task<RecordCommitReceipt> CommitAsync(RunCompletion completion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var read = conn.CreateCommand();
        read.Transaction = (SqliteTransaction)tx;
        read.CommandText = "SELECT started_utc,persist_state,terminal_state,verdict,completed_utc,failure_reason FROM run_headers WHERE run_id=$run;";
        Add(read, "$run", completion.Run.ToString());
        string state;
        await using (var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Cannot commit missing RunId.");
            var started = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            completion.Validate(started);
            state = reader.GetString(1);
            if (state == "Committed")
            {
                if (reader.GetString(2) != completion.TerminalState.ToString() ||
                    (reader.IsDBNull(3) ? null : reader.GetString(3)) != completion.Verdict?.ToString() ||
                    (reader.IsDBNull(4) ? null : reader.GetString(4)) != completion.CompletedUtc.ToString("O") ||
                    (reader.IsDBNull(5) ? null : reader.GetString(5)) != completion.FailureReason)
                    throw new InvalidOperationException("Finalized RunId already has a different completion.");
            }
        }
        if (state == "Committed")
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new RecordCommitReceipt(completion.Run, true, "AlreadyCommitted");
        }
        if (state != "Writing") throw new InvalidOperationException("Interrupted run cannot be rewritten as finalized.");
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            UPDATE run_headers SET persist_state='Committed',terminal_state=$terminal,verdict=$verdict,
            completed_utc=$completed,failure_reason=$reason
            WHERE run_id=$run AND persist_state='Writing';
            """;
        Add(cmd, "$run", completion.Run.ToString());
        Add(cmd, "$terminal", completion.TerminalState.ToString());
        Add(cmd, "$verdict", (object?)completion.Verdict?.ToString() ?? DBNull.Value);
        Add(cmd, "$completed", completion.CompletedUtc.ToString("O"));
        Add(cmd, "$reason", (object?)completion.FailureReason ?? DBNull.Value);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new IOException("Run was not committed.");
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new RecordCommitReceipt(completion.Run, true, "Committed");
    }

    public async Task<ImmutableArray<RunFact>> ReadFactsAsync(RecordQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Run.Value == Guid.Empty || query.MaxCount is <= 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(query));
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json,payload_hash FROM run_facts WHERE run_id=$run ORDER BY sequence LIMIT $limit;";
        Add(cmd, "$run", query.Run.ToString());
        Add(cmd, "$limit", query.MaxCount);
        var facts = ImmutableArray.CreateBuilder<RunFact>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            if (Hash(json) != reader.GetString(1))
                throw new InvalidDataException("Stored fact hash mismatch.");
            var payload = JsonSerializer.Deserialize<FactPayload>(json)
                ?? throw new InvalidDataException("Stored fact JSON missing.");
            var fact = payload.ToFact();
            RunFactTimeline.Validate(fact);
            if (fact.Run != query.Run) throw new InvalidDataException("Fact RunId mismatch.");
            facts.Add(fact);
        }
        return facts.ToImmutable();
    }

    public async Task AppendStepAsync(RunStepFact fact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        fact.Validate();
        var json = JsonSerializer.Serialize(StepPayload.FromFact(fact));
        var hash = Hash(json);
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var status = conn.CreateCommand();
        status.Transaction = (SqliteTransaction)tx;
        status.CommandText = "SELECT persist_state FROM run_headers WHERE run_id=$run;";
        Add(status, "$run", fact.Run.ToString());
        var persistence = (string?)await status.ExecuteScalarAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("BeginAsync required before AppendStepAsync.");
        await using var match = conn.CreateCommand();
        match.Transaction = (SqliteTransaction)tx;
        match.CommandText = "SELECT payload_hash FROM run_step_facts WHERE run_id=$run AND event_id=$event;";
        Add(match, "$run", fact.Run.ToString()); Add(match, "$event", fact.EventId.ToString("D"));
        var existing = (string?)await match.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing != hash) throw new InvalidOperationException("Step fact EventId collision.");
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        if (persistence != "Writing") throw new InvalidOperationException("Run is finalized or interrupted.");
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO run_step_facts(run_id,sequence,event_id,payload_json,payload_hash)
            SELECT $run,COALESCE(MAX(sequence),0)+1,$event,$json,$hash
            FROM run_step_facts WHERE run_id=$run;
            """;
        Add(cmd, "$run", fact.Run.ToString()); Add(cmd, "$event", fact.EventId.ToString("D"));
        Add(cmd, "$json", json); Add(cmd, "$hash", hash);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ImmutableArray<RunStepFact>> ReadStepFactsAsync(RunId run, int limit = 100000,
        CancellationToken ct = default)
    {
        if (run.Value == Guid.Empty || limit is <= 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(run));
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json,payload_hash FROM run_step_facts WHERE run_id=$run ORDER BY sequence LIMIT $limit;";
        Add(cmd, "$run", run.ToString()); Add(cmd, "$limit", limit);
        var facts = ImmutableArray.CreateBuilder<RunStepFact>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            if (Hash(json) != reader.GetString(1)) throw new InvalidDataException("Step fact content corrupted.");
            var payload = JsonSerializer.Deserialize<StepPayload>(json)
                ?? throw new InvalidDataException("Invalid step fact JSON.");
            var fact = payload.ToFact(); fact.Validate();
            if (fact.Run != run) throw new InvalidDataException("StepFact RunId mismatch.");
            facts.Add(fact);
        }
        return facts.ToImmutable();
    }

    public async Task AttachArtifactAsync(RunId run, ArtifactDescriptor artifact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO artifact_links(run_id,artifact_id,relative_path,hash_hex,byte_length,kind,media_type,provenance)
            SELECT $run,$id,$path,$hash,$len,$kind,$media,$source
            WHERE EXISTS(SELECT 1 FROM run_headers WHERE run_id=$run AND persist_state='Writing')
            ON CONFLICT(run_id,artifact_id) DO NOTHING;
            """;
        Add(cmd, "$run", run.ToString()); Add(cmd, "$id", artifact.ArtifactId.ToString("D"));
        Add(cmd, "$path", artifact.RelativePath); Add(cmd, "$hash", artifact.Sha256);
        Add(cmd, "$len", artifact.ByteLength); Add(cmd, "$kind", artifact.Kind);
        Add(cmd, "$media", artifact.MediaType); Add(cmd, "$source", artifact.Provenance);
        var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (inserted == 0)
        {
            await using var same = conn.CreateCommand();
            same.Transaction = (SqliteTransaction)tx;
            same.CommandText = "SELECT relative_path,hash_hex,byte_length,kind,media_type,provenance FROM artifact_links WHERE run_id=$run AND artifact_id=$id;";
            Add(same, "$run", run.ToString()); Add(same, "$id", artifact.ArtifactId.ToString("D"));
            await using var reader = await same.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
                reader.GetString(0) != artifact.RelativePath || reader.GetString(1) != artifact.Sha256 ||
                reader.GetInt64(2) != artifact.ByteLength || reader.GetString(3) != artifact.Kind ||
                reader.GetString(4) != artifact.MediaType || reader.GetString(5) != artifact.Provenance)
                throw new InvalidOperationException("Artifact is missing or its identity conflicts.");
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ImmutableArray<ArtifactDescriptor>> ReadArtifactsAsync(RunId run, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT artifact_id,relative_path,hash_hex,byte_length,kind,media_type,provenance FROM artifact_links WHERE run_id=$run ORDER BY artifact_id;";
        Add(cmd, "$run", run.ToString());
        var builder = ImmutableArray.CreateBuilder<ArtifactDescriptor>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = new ArtifactDescriptor(Guid.Parse(reader.GetString(0)), reader.GetString(4),
                reader.GetString(5), reader.GetInt64(3), reader.GetString(2), reader.GetString(1), reader.GetString(6));
            item.Validate(); builder.Add(item);
        }
        return builder.ToImmutable();
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static void Add(SqliteCommand cmd, string key, object value) => cmd.Parameters.AddWithValue(key, value);
    private static async Task ExecuteSqlAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand(); cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record StepPayload(string Run, Guid EventId, Guid Step, int Attempt,
        string Disposition, string? Expected, string? Actual, string Utc, long Ticks,
        string Source, string TimeQuality, string Quality, Guid[] Evidence)
    {
        public static StepPayload FromFact(RunStepFact fact) => new(
            fact.Run.ToString(), fact.EventId, fact.Step.Value, fact.Attempt,
            fact.Disposition, fact.Expected, fact.Actual, fact.Time.Utc.ToString("O"),
            fact.Time.MonotonicTicks, fact.Time.Source.ToString(),
            fact.Time.Quality.ToString(), fact.Quality.ToString(),
            fact.Evidence.Select(e => e.Value).ToArray());
        public RunStepFact ToFact() => new(RunId.Create(Guid.Parse(Run)), EventId,
            StepId.Create(Step), Attempt, Disposition, Expected, Actual,
            new TimePoint(DateTimeOffset.Parse(Utc, CultureInfo.InvariantCulture), Ticks,
                Enum.Parse<TimeSource>(Source), Enum.Parse<TimeQuality>(TimeQuality)),
            Enum.Parse<EvidenceQuality>(Quality),
            Evidence.Select(e => new EvidenceRef(e)).ToImmutableArray());
    }

    private sealed record FactPayload(string Run, Guid EventId, Guid OperationId, string Kind,
        string Description, string Utc, long MonotonicTicks, string TimeSource, string TimeQuality,
        string Quality, Guid[] Evidence)
    {
        public static FactPayload FromFact(RunFact fact) => new(
            fact.Run.ToString(), fact.EventId, fact.OperationId, fact.Kind.ToString(),
            fact.Description, fact.Time.Utc.ToString("O"), fact.Time.MonotonicTicks,
            fact.Time.Source.ToString(), fact.Time.Quality.ToString(),
            fact.Quality.ToString(), fact.Evidence.Select(x => x.Value).ToArray());

        public RunFact ToFact() => new(RunId.Create(Guid.Parse(Run)), EventId, OperationId,
            Enum.Parse<RunFactKind>(Kind), Description,
            new TimePoint(DateTimeOffset.Parse(Utc, CultureInfo.InvariantCulture), MonotonicTicks,
                Enum.Parse<TimeSource>(TimeSource), Enum.Parse<TimeQuality>(TimeQuality)),
            Enum.Parse<EvidenceQuality>(Quality),
            Evidence.Select(id => new EvidenceRef(id)).ToImmutableArray());
    }
}
