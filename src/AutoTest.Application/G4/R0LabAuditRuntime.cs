using AutoTest.Records.Operations;
using System.Text.Json;

namespace AutoTest.Application.G4;

/// <summary>
/// INTEGRATION-owned operation-audit composition. This is not a second RunRecord,
/// station session, device link, or transport trace generator. Its store and trace
/// archive live for the lifetime of one G4IntegrationHost.
/// </summary>
public sealed class R0LabAuditRuntime
{
    private static readonly HashSet<string> ToolKeys = new(StringComparer.Ordinal)
    {
        "controller-config", "dbc", "test", "production", "records",
        "operations", "uds", "flash", "wave", "bench"
    };

    public string DirectoryPath { get; }
    public IOperationAuditStore Store { get; }
    public OperationTraceArchive Traces { get; }
    public IOperationAuditExporter Exporter { get; }
    public OperationAuditRecorder Recorder { get; }

    public R0LabAuditRuntime(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = Path.GetFullPath(directoryPath);
        Store = new SqliteOperationAuditStore(Path.Combine(DirectoryPath, "operation-audit.sqlite"));
        Traces = new OperationTraceArchive(Path.Combine(DirectoryPath, "traces"));
        Exporter = new OperationAuditExporter(Store);
        Recorder = new OperationAuditRecorder(Store, Traces);
    }

    /// <summary>
    /// Records that the user reached a tool UI; never infers successful USB/CAN/UDS,
    /// physical observation, TEST execution, or production approval from a tab change.
    /// Any SQLite failure is intentionally propagated to the caller and shown by the UI.
    /// </summary>
    /// <summary>Records an actual desktop page-activation failure, not a device failure.</summary>
    public async Task<AuditAppendReceipt> RecordToolActivationFailureAsync(
        string toolKey, Exception error, CancellationToken ct = default)
    {
        if (toolKey is null || !ToolKeys.Contains(toolKey))
            throw new ArgumentException("Unregistered R0 specialist tool key.", nameof(toolKey));
        ArgumentNullException.ThrowIfNull(error);
        var id = Guid.NewGuid();
        var receipt = await Recorder.RecordAsync(new OperationAuditRequest
        {
            EventId = id,
            OperationId = id,
            CorrelationId = id,
            OccurredUtc = DateTimeOffset.UtcNow,
            Kind = "R0.ToolActivationFailure",
            Summary = $"Failed to construct specialist UI {toolKey}; no backend operation completed.",
            Origin = AuditOrigin.Unknown,
            Level = AuditLevel.Error,
            Outcome = AuditOutcome.Failed,
            FailureStage = "Desktop.ToolActivation",
            DataQuality = "UI_EXCEPTION_NO_PHYSICAL_EVIDENCE",
            DetailJson = JsonSerializer.Serialize(new
            {
                ExceptionType = error.GetType().FullName,
                Error = error.Message.Length > 2000 ? error.Message[..2000] : error.Message
            }),
            SessionGeneration = 0
        }, ct).ConfigureAwait(false);
        if (!receipt.Durable)
            throw new IOException("R0 tool failure audit was not durably persisted.");
        return receipt;
    }

    public async Task<AuditAppendReceipt> RecordToolOpenedAsync(
        string toolKey, string displayName, CancellationToken ct = default)
    {
        if (toolKey is null || !ToolKeys.Contains(toolKey))
            throw new ArgumentException("Unregistered R0 specialist tool key.", nameof(toolKey));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
            throw new ArgumentException("Invalid tool label.", nameof(displayName));

        var id = Guid.NewGuid();
        var receipt = await Recorder.RecordAsync(new OperationAuditRequest
        {
            EventId = id,
            OperationId = id,
            CorrelationId = id,
            OccurredUtc = DateTimeOffset.UtcNow,
            Kind = "R0.ToolNavigation",
            Summary = $"Opened {toolKey} ({displayName}); UI only; no device action or verified observation.",
            Origin = AuditOrigin.Unknown,
            Level = AuditLevel.Information,
            Outcome = AuditOutcome.Succeeded,
            DataQuality = "UI_NAVIGATION_ONLY",
            SessionGeneration = 0
        }, ct).ConfigureAwait(false);

        if (!receipt.Durable)
            throw new IOException("R0 tool navigation audit was not durably persisted.");
        return receipt;
    }
}
