using System.Text.Json;

namespace AutoTest.Records.Operations;

/// <summary>Application/DEVICE instrumentation entry: one audit route, no USB or RunRecord dependency.</summary>
public sealed record OperationAuditRequest
{
    public Guid EventId { get; init; }
    public Guid OperationId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public string Kind { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? DeviceId { get; init; }
    public long SessionGeneration { get; init; }
    public string? RunId { get; init; }
    public string? Actor { get; init; }
    public string? CanRole { get; init; }
    public string? DbcRevision { get; init; }
    public string? DataQuality { get; init; }
    public AuditOrigin Origin { get; init; } = AuditOrigin.Unknown;
    public AuditLevel Level { get; init; } = AuditLevel.Information;
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Unknown;
    public string? FailureStage { get; init; }
    public int? NativeErrorCode { get; init; }
    public long? DurationMilliseconds { get; init; }
    public string? DetailJson { get; init; }
    public OperationRawTrace? RawTrace { get; init; }
}

public sealed class OperationAuditRecorder
{
    private readonly IOperationAuditStore _events;
    private readonly IOperationTraceWriter _traces;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OperationAuditRecorder(IOperationAuditStore events, IOperationTraceWriter traces)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _traces = traces ?? throw new ArgumentNullException(nameof(traces));
    }

    public async Task<AuditAppendReceipt> RecordAsync(OperationAuditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RawTrace != null && request.RawTrace.OperationId != request.OperationId)
            throw new InvalidDataException("Raw Trace OperationId differs from the audit operation.");

        if (request.EventId == Guid.Empty || request.OperationId == Guid.Empty || request.CorrelationId == Guid.Empty)
            throw new InvalidDataException("A stable EventId, OperationId and CorrelationId are required.");
        // Validate before writing raw bytes, so an invalid request cannot leave an orphan Trace.
        var prototype = new OperationAuditEvent
        {
            EventId = request.EventId, OperationId = request.OperationId, CorrelationId = request.CorrelationId,
            OccurredUtc = request.OccurredUtc, Kind = request.Kind, Summary = request.Summary,
            DeviceId = request.DeviceId, SessionGeneration = request.SessionGeneration, RunId = request.RunId,
            Actor = request.Actor, CanRole = request.CanRole, DbcRevision = request.DbcRevision,
            DataQuality = request.DataQuality, Origin = request.Origin, Level = request.Level,
            Outcome = request.Outcome, FailureStage = request.FailureStage,
            NativeErrorCode = request.NativeErrorCode, DurationMilliseconds = request.DurationMilliseconds,
            DetailJson = request.DetailJson
        };
        prototype.Validate();
        request.RawTrace?.Validate();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await _events.FindByIdAsync(request.EventId, ct).ConfigureAwait(false);
            if (existing != null && request.RawTrace != null)
            {
                if (existing.Trace == null || _traces is not IOperationTraceReader reader)
                    throw new InvalidOperationException("Cannot verify idempotent raw trace retry.");
                var oldTrace = await reader.ReadAsync(existing.Trace, ct).ConfigureAwait(false);
                if (JsonSerializer.Serialize(oldTrace) != JsonSerializer.Serialize(request.RawTrace))
                    throw new InvalidOperationException("EventId conflict: raw trace bytes changed.");
            }
            var pointer = existing?.Trace ?? (request.RawTrace == null ? null :
                await _traces.AppendAsync(request.RawTrace, ct).ConfigureAwait(false));
            var audit = prototype with { Trace = pointer };
            if (existing != null && audit.CanonicalJson() != existing.CanonicalJson())
                throw new InvalidOperationException("EventId conflict: audit fact differs from stored history.");
            try { return await _events.AppendAsync(audit, ct).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Caller must surface this error and can still locate flushed raw bytes.
                throw new OperationAuditPersistenceException("Audit event was NOT committed to SQLite.", pointer, exception);
            }
        }
        finally { _gate.Release(); }
    }

    public Task<AuditAppendReceipt> RecordManualVoltageAsync(Guid eventId, Guid operationId, Guid correlationId,
        ManualVoltageEvidence measurement, string? operatorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        measurement.Validate();
        var request = new OperationAuditRequest
        {
            EventId = eventId, OperationId = operationId, CorrelationId = correlationId,
            OccurredUtc = measurement.MeasuredUtc, DeviceId = measurement.DeviceId,
            Kind = "ManualVoltageMeasurement", Summary = "Five-channel meter observation, not command ACK",
            Origin = AuditOrigin.Physical, Level = AuditLevel.Information, Outcome = AuditOutcome.Unknown,
            Actor = operatorId, DetailJson = measurement.ToDetailJson(), DataQuality = "ManuallyEnteredUnverified"
        };
        return RecordAsync(request, ct);
    }
}

/// <summary>When trace flush succeeded but SQLite failed, preserve its pointer for recovery.</summary>
public sealed class OperationAuditPersistenceException : IOException
{
    public AuditTraceReference? FlushedTrace { get; }
    public OperationAuditPersistenceException(string message, AuditTraceReference? trace, Exception inner)
        : base(message, inner) => FlushedTrace = trace;
}
