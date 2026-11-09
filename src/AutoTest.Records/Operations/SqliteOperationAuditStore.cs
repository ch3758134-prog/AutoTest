using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AutoTest.Records.Operations;

/// <summary>One append-only operation history, independent of RunRecord and of a connected device.</summary>
public sealed class SqliteOperationAuditStore : IOperationAuditStore
{
    public const int SchemaVersion = 1;
    private readonly string _path;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public SqliteOperationAuditStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path == ":memory:") throw new ArgumentException("An audit requires a durable SQLite file.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var conn = await OpenUninitializedConnectionAsync(ct).ConfigureAwait(false);
            await using (var wal = conn.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                await wal.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS operation_audit_meta (name TEXT PRIMARY KEY, value TEXT NOT NULL);";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            cmd.CommandText = "SELECT value FROM operation_audit_meta WHERE name='schema_version';";
            var existingValue = (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (existingValue != null && (!int.TryParse(existingValue, CultureInfo.InvariantCulture, out var existingVersion) ||
                existingVersion > SchemaVersion || existingVersion < 1))
                throw new NotSupportedException("Unsupported operation-audit schema version: " + existingValue);
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS operation_audit_events (
                    event_id TEXT PRIMARY KEY NOT NULL,
                    operation_id TEXT NOT NULL,
                    correlation_id TEXT NOT NULL,
                    occurred_utc TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    device_id TEXT,
                    session_generation INTEGER NOT NULL,
                    run_id TEXT,
                    origin INTEGER NOT NULL,
                    level INTEGER NOT NULL,
                    outcome INTEGER NOT NULL,
                    native_code INTEGER,
                    failure_stage TEXT,
                    trace_file TEXT,
                    trace_offset INTEGER,
                    trace_hash TEXT,
                    summary TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    event_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_ops_time ON operation_audit_events(occurred_utc DESC, event_id DESC);
                CREATE INDEX IF NOT EXISTS ix_ops_operation ON operation_audit_events(operation_id, occurred_utc);
                CREATE INDEX IF NOT EXISTS ix_ops_run ON operation_audit_events(run_id, occurred_utc);
                CREATE INDEX IF NOT EXISTS ix_ops_device ON operation_audit_events(device_id, occurred_utc);
                CREATE INDEX IF NOT EXISTS ix_ops_trace ON operation_audit_events(trace_file, trace_offset, trace_hash);
                CREATE TRIGGER IF NOT EXISTS operation_events_block_update
                    BEFORE UPDATE ON operation_audit_events
                    BEGIN SELECT RAISE(ABORT, 'audit events are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS operation_events_block_delete
                    BEFORE DELETE ON operation_audit_events
                    BEGIN SELECT RAISE(ABORT, 'audit events are immutable'); END;
                INSERT INTO operation_audit_meta(name,value) VALUES('schema_version','1')
                    ON CONFLICT(name) DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally { _initializationGate.Release(); }
    }

    public async Task<AuditAppendReceipt> AppendAsync(OperationAuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        auditEvent.Validate();
        await InitializeAsync(ct).ConfigureAwait(false);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        await using var conn = await OpenUninitializedConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO operation_audit_events(event_id,operation_id,correlation_id,occurred_utc,kind,device_id,
                session_generation,run_id,origin,level,outcome,native_code,failure_stage,trace_file,
                trace_offset,trace_hash,summary,content_hash,event_json)
            VALUES($id,$operation,$correlation,$time,$kind,$device,$generation,$run,$origin,$level,
                $outcome,$native,$stage,$traceFile,$traceOffset,$traceHash,$summary,$hash,$json)
            ON CONFLICT(event_id) DO NOTHING;
            """;
        Add(cmd, "$id", auditEvent.EventId.ToString("D"));
        Add(cmd, "$operation", auditEvent.OperationId.ToString("D"));
        Add(cmd, "$correlation", auditEvent.CorrelationId.ToString("D"));
        Add(cmd, "$time", TimeKey(auditEvent.OccurredUtc));
        Add(cmd, "$kind", auditEvent.Kind);
        Add(cmd, "$device", auditEvent.DeviceId);
        Add(cmd, "$generation", auditEvent.SessionGeneration);
        Add(cmd, "$run", auditEvent.RunId);
        Add(cmd, "$origin", (int)auditEvent.Origin);
        Add(cmd, "$level", (int)auditEvent.Level);
        Add(cmd, "$outcome", (int)auditEvent.Outcome);
        Add(cmd, "$native", auditEvent.NativeErrorCode);
        Add(cmd, "$stage", auditEvent.FailureStage);
        Add(cmd, "$traceFile", auditEvent.Trace?.RelativeFile);
        Add(cmd, "$traceOffset", auditEvent.Trace?.Offset);
        Add(cmd, "$traceHash", auditEvent.Trace?.Sha256);
        Add(cmd, "$summary", auditEvent.Summary);
        Add(cmd, "$hash", auditEvent.Hash());
        Add(cmd, "$json", auditEvent.CanonicalJson());
        var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        if (!inserted)
        {
            await using var read = conn.CreateCommand();
            read.Transaction = tx;
            read.CommandText = "SELECT content_hash FROM operation_audit_events WHERE event_id=$id";
            Add(read, "$id", auditEvent.EventId.ToString("D"));
            var existing = (string?)await read.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (!string.Equals(existing, auditEvent.Hash(), StringComparison.Ordinal))
                throw new InvalidOperationException("EventId conflict: the existing audit fact differs; no overwrite permitted.");
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new AuditAppendReceipt(auditEvent.EventId, Durable: true, AlreadyPresent: !inserted);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<AuditPage> QueryAsync(OperationAuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = await OpenUninitializedConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var conditions = new List<string>();
        if (query.OperationId is Guid op) { conditions.Add("operation_id=$op"); Add(cmd,"$op",op.ToString("D")); }
        if (query.DeviceId != null) { conditions.Add("device_id=$device"); Add(cmd,"$device",query.DeviceId); }
        if (query.RunId != null) { conditions.Add("run_id=$run"); Add(cmd,"$run",query.RunId); }
        if (query.Kind != null) { conditions.Add("kind=$kind"); Add(cmd,"$kind",query.Kind); }
        if (query.Origin is AuditOrigin origin) { conditions.Add("origin=$origin"); Add(cmd,"$origin",(int)origin); }
        if (query.Level is AuditLevel level) { conditions.Add("level=$level"); Add(cmd,"$level",(int)level); }
        if (query.FailuresOnly == true)
            conditions.Add("(level=2 OR outcome IN (3,4,6))");
        if (query.WithTraceOnly == true) conditions.Add("trace_file IS NOT NULL");
        if (query.FromUtc is DateTimeOffset from) { conditions.Add("occurred_utc >= $from"); Add(cmd,"$from",TimeKey(from)); }
        if (query.ToUtc is DateTimeOffset to) { conditions.Add("occurred_utc <= $to"); Add(cmd,"$to",TimeKey(to)); }
        if (query.Before is AuditCursor cursor)
        {
            conditions.Add("(occurred_utc < $beforeTime OR (occurred_utc=$beforeTime AND event_id < $beforeId))");
            Add(cmd,"$beforeTime",TimeKey(cursor.OccurredUtc));
            Add(cmd,"$beforeId",cursor.EventId.ToString("D"));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(summary LIKE $search ESCAPE '\\' OR kind LIKE $search ESCAPE '\\' OR failure_stage LIKE $search ESCAPE '\\')");
            var literal = query.Search.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            Add(cmd,"$search","%"+literal+"%");
        }
        cmd.CommandText = "SELECT event_json FROM operation_audit_events" +
            (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions)) +
            " ORDER BY occurred_utc DESC, event_id DESC LIMIT $limit";
        Add(cmd,"$limit",query.PageSize + 1);
        var rows = new List<OperationAuditEvent>(query.PageSize + 1);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = JsonSerializer.Deserialize<OperationAuditEvent>(reader.GetString(0))
                ?? throw new InvalidDataException("Stored audit event cannot be deserialized.");
            rows.Add(item);
        }
        bool more = rows.Count > query.PageSize;
        if (more) rows.RemoveAt(rows.Count - 1);
        var last = more ? rows[^1] : null;
        return new AuditPage(rows, last == null ? null : new AuditCursor(last.OccurredUtc, last.EventId));
    }

    public async Task<OperationAuditEvent?> FindByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Nonempty EventId required", nameof(eventId));
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = await OpenUninitializedConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_json FROM operation_audit_events WHERE event_id=$id";
        Add(cmd, "$id", eventId.ToString("D"));
        var json = (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return json == null ? null : JsonSerializer.Deserialize<OperationAuditEvent>(json);
    }

    public async Task<IReadOnlyList<OperationAuditEvent>> FindByTraceAsync(AuditTraceReference trace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        trace.Validate();
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = await OpenUninitializedConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT event_json FROM operation_audit_events
            WHERE trace_file=$file AND trace_offset=$offset AND trace_hash=$hash
            ORDER BY occurred_utc DESC, event_id DESC LIMIT 500
            """;
        Add(cmd,"$file",trace.RelativeFile); Add(cmd,"$offset",trace.Offset); Add(cmd,"$hash",trace.Sha256);
        var results = new List<OperationAuditEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(JsonSerializer.Deserialize<OperationAuditEvent>(reader.GetString(0))!);
        return results;
    }

    private async Task<SqliteConnection> OpenUninitializedConnectionAsync(CancellationToken ct)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default, Pooling = false, DefaultTimeout = 10 }.ToString();
        var conn = new SqliteConnection(cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static string TimeKey(DateTimeOffset utc) => utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static void Add(SqliteCommand cmd, string key, object? value) =>
        cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
}
