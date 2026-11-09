using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTest.Records.Operations;

/// <summary>Origin is an evidence assertion, not an inferred verdict. Unknown is mandatory when no observation exists.</summary>
public enum AuditOrigin { Unknown, Physical, Replay, Mock }
public enum AuditLevel { Information, Warning, Error }
public enum AuditOutcome { Unknown, Started, Succeeded, Failed, Denied, Cancelled, TimedOut }

/// <summary>Stable pointer to a flushed JSONL line. Never replace with raw CAN frames in SQLite.</summary>
public sealed record AuditTraceReference(string RelativeFile, long Offset, int ByteLength, string Sha256)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RelativeFile) || RelativeFile != Path.GetFileName(RelativeFile) ||
            RelativeFile.Contains('\\') || RelativeFile.Contains('/') || !RelativeFile.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
            Offset < 0 || ByteLength < 1 || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid trace pointer.");
    }
}

/// <summary>Events are immutable. Connection attempts and failures do not need a RunId or a live device.</summary>
public sealed record OperationAuditEvent
{
    public Guid EventId { get; init; }
    public Guid OperationId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public string Kind { get; init; } = "";
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
    public string Summary { get; init; } = "";
    public string? DetailJson { get; init; }
    public AuditTraceReference? Trace { get; init; }

    public void Validate()
    {
        if (EventId == Guid.Empty || OperationId == Guid.Empty || CorrelationId == Guid.Empty ||
            OccurredUtc == default || OccurredUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(Origin) || !Enum.IsDefined(Level) || !Enum.IsDefined(Outcome) ||
            SessionGeneration < 0 || DurationMilliseconds < 0 ||
            !ValidText(Kind, 96, required: true) || !ValidText(Summary, 2048, required: true) ||
            !ValidText(DeviceId, 256) || !ValidText(RunId, 128) || !ValidText(Actor, 128) ||
            !ValidText(CanRole, 64) || !ValidText(DbcRevision, 128) ||
            !ValidText(DataQuality, 128) || !ValidText(FailureStage, 128) ||
            !ValidText(DetailJson, 32768))
            throw new InvalidDataException("Invalid operation audit event.");
        if (DetailJson != null)
        {
            using var _ = JsonDocument.Parse(DetailJson);
        }
        Trace?.Validate();
    }

    private static bool ValidText(string? value, int maxLength, bool required = false) =>
        (value != null || !required) && (value == null ||
        (value.Length <= maxLength && (!required || !string.IsNullOrWhiteSpace(value))));

    internal string CanonicalJson() => JsonSerializer.Serialize(this);
    internal string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson())));
}

public sealed record AuditCursor(DateTimeOffset OccurredUtc, Guid EventId);
public sealed record OperationAuditQuery
{
    public string? Search { get; init; }
    public Guid? OperationId { get; init; }
    public string? DeviceId { get; init; }
    public string? RunId { get; init; }
    public string? Kind { get; init; }
    public AuditOrigin? Origin { get; init; }
    public AuditLevel? Level { get; init; }
    public bool? FailuresOnly { get; init; }
    public bool? WithTraceOnly { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public AuditCursor? Before { get; init; }
    public int PageSize { get; init; } = 100;

    public void Validate()
    {
        if (PageSize < 1 || PageSize > 500 || Search?.Length > 256 || DeviceId?.Length > 256 ||
            RunId?.Length > 128 || Kind?.Length > 96 ||
            (FromUtc.HasValue && FromUtc.Value.Offset != TimeSpan.Zero) ||
            (ToUtc.HasValue && ToUtc.Value.Offset != TimeSpan.Zero) ||
            (Before != null && Before.OccurredUtc.Offset != TimeSpan.Zero) ||
            (FromUtc.HasValue && ToUtc.HasValue && FromUtc > ToUtc))
            throw new ArgumentException("Invalid audit query.");
    }
}
public sealed record AuditPage(IReadOnlyList<OperationAuditEvent> Events, AuditCursor? NextCursor);
public sealed record AuditAppendReceipt(Guid EventId, bool Durable, bool AlreadyPresent);

public interface IOperationAuditStore
{
    Task<AuditAppendReceipt> AppendAsync(OperationAuditEvent auditEvent, CancellationToken ct = default);
    Task<AuditPage> QueryAsync(OperationAuditQuery query, CancellationToken ct = default);
    Task<OperationAuditEvent?> FindByIdAsync(Guid eventId, CancellationToken ct = default);
    Task<IReadOnlyList<OperationAuditEvent>> FindByTraceAsync(AuditTraceReference trace, CancellationToken ct = default);
}

public sealed record OperationRawTrace
{
    public Guid OperationId { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public string Direction { get; init; } = "";
    public string Transport { get; init; } = "";
    public string? Channel { get; init; }
    public string? Message { get; init; }
    /// <summary>Do not include Seed/Key, tokens or other sensitive bytes. When in doubt, redact.</summary>
    public string? PayloadHexRedacted { get; init; }
    public bool ContainsSecrets { get; init; }

    public void Validate()
    {
        if (OperationId == Guid.Empty || OccurredUtc == default || OccurredUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(Direction) || Direction.Length > 32 ||
            string.IsNullOrWhiteSpace(Transport) || Transport.Length > 32 ||
            Message?.Length > 8192 || Channel?.Length > 128 ||
            PayloadHexRedacted?.Length > 65536 || ContainsSecrets)
            throw new InvalidDataException("Trace contains invalid or explicitly sensitive data; redact before logging.");
    }
}

/// <summary>Physical voltmeter entry is an observation; command ACK never supplies measured values.</summary>
public sealed record VoltageMeasurement(int Channel, decimal? Volts, string ProbePoint);
public sealed record ManualVoltageEvidence(
    string ControllerId, string StateId, string ConfigurationRevision,
    string DeviceId, string MeterId, DateTimeOffset MeasuredUtc,
    IReadOnlyList<VoltageMeasurement> Channels)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ControllerId) || string.IsNullOrWhiteSpace(StateId) ||
            string.IsNullOrWhiteSpace(ConfigurationRevision) || string.IsNullOrWhiteSpace(DeviceId) ||
            string.IsNullOrWhiteSpace(MeterId) || MeasuredUtc == default || MeasuredUtc.Offset != TimeSpan.Zero ||
            Channels == null || Channels.Count != 5 ||
            Channels.Select(c => c.Channel).OrderBy(x => x).Where((ch, ix) => ch != ix + 1).Any() ||
            Channels.Any(c => string.IsNullOrWhiteSpace(c.ProbePoint) || c.Volts is < -1000m or > 1000m))
            throw new InvalidDataException("Five-channel manually measured voltage evidence is required.");
    }

    public string ToDetailJson()
    {
        Validate();
        return JsonSerializer.Serialize(this);
    }
}
