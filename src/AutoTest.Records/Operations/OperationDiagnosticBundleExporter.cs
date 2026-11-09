using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoTest.Records.Operations;

/// <summary>Local, evidence-preserving ZIP export. Does not grant hardware permissions or certify a verdict.</summary>
public sealed record DiagnosticBundleReceipt(
    string Destination, int EventCount, int VerifiedTraceCount, int UnavailableTraceCount,
    int EventsWithoutTrace, string AuditJsonlSha256)
{
    // "Complete" means the filtered diagnostic has raw evidence for every event,
    // not merely that the trace pointers which happened to exist were readable.
    public bool Complete => EventCount > 0 && UnavailableTraceCount == 0 && EventsWithoutTrace == 0;
}

/// <summary>Optional provenance supplied by the HOST composition root; omitted values remain UNKNOWN.</summary>
public sealed record DiagnosticBundleContext(
    string? GitCommit = null, string? ProjectId = null, string? ProfileId = null,
    string? ResourceRevision = null, string? Scenario = null);

public interface IOperationDiagnosticBundleExporter
{
    Task<DiagnosticBundleReceipt> ExportAsync(OperationAuditQuery query, string destination,
        CancellationToken ct = default);
}

/// <summary>
/// Includes the full filtered audit timeline and each referenced, hash-verified trace in one portable ZIP.
/// Missing or corrupt trace evidence is reported in the index, never fabricated or omitted silently.
/// Sensitive free-text fields are minimized, and trace payloads must already be redacted at ingestion.
/// </summary>
public sealed class OperationDiagnosticBundleExporter : IOperationDiagnosticBundleExporter
{
    private readonly IOperationAuditStore _store;
    private readonly IOperationTraceReader _traces;
    private readonly DiagnosticBundleContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex SecretLabel = new(
        @"(?i)\b(seed|key|password|token|secret|credential|securityaccess)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public OperationDiagnosticBundleExporter(IOperationAuditStore store, IOperationTraceReader traces,
        DiagnosticBundleContext? context = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _traces = traces ?? throw new ArgumentNullException(nameof(traces));
        _context = context ?? new DiagnosticBundleContext();
    }

    public async Task<DiagnosticBundleReceipt> ExportAsync(OperationAuditQuery query, string destination,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        query.Validate();
        destination = Path.GetFullPath(destination);
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Diagnostic evidence destination must end in .zip.", nameof(destination));
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        // Creating the temporary file only. We never overwrite an existing export, including on failure.
        var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            DiagnosticBundleReceipt receipt;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 32768, options: FileOptions.Asynchronous))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var traceLinks = new Dictionary<AuditTraceReference, TraceLink>();
                    // Stable within this ZIP, unlinkable to the original DUT/Run without the private database.
                    // The randomly generated salt is intentionally not included in the public archive.
                    var identitySalt = RandomNumberGenerator.GetBytes(32);
                    string? Pseudonym(string? value) => string.IsNullOrEmpty(value) ? value :
                        "anon-" + Convert.ToHexString(SHA256.HashData(
                            identitySalt.Concat(Encoding.UTF8.GetBytes(value)).ToArray()))[..20];
                    int eventCount = 0;
                    int eventsWithoutTrace = 0;
                    using var eventDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var timeline = zip.CreateEntry("audit/events.jsonl", CompressionLevel.Optimal);
                    await using (var timelineStream = timeline.Open())
                    {
                        AuditCursor? cursor = query.Before;
                        do
                        {
                            ct.ThrowIfCancellationRequested();
                            var page = await _store.QueryAsync(query with { PageSize = 500, Before = cursor }, ct)
                                .ConfigureAwait(false);
                            foreach (var original in page.Events)
                            {
                                original.Validate();
                                // Avoid exporting operator personal data, arbitrary DetailJson, or labelled credentials.
                                var safe = original with
                                {
                                    Actor = null,
                                    DetailJson = null,
                                    Summary = Redact(original.Summary),
                                    FailureStage = Redact(original.FailureStage),
                                    DeviceId = Pseudonym(original.DeviceId),
                                    RunId = Pseudonym(original.RunId)
                                };
                                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(safe) + "\n");
                                eventDigest.AppendData(bytes);
                                await timelineStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
                                eventCount++;
                                if (original.Trace is null) eventsWithoutTrace++;
                                else
                                {
                                    original.Trace.Validate();
                                    if (!traceLinks.TryGetValue(original.Trace, out var link))
                                        traceLinks.Add(original.Trace, link = new TraceLink(original.Trace));
                                    link.Events.Add(original.EventId);
                                    link.Operations.Add(original.OperationId);
                                }
                            }
                            cursor = page.NextCursor;
                        } while (cursor is not null);
                    }

                    var indexed = new List<object>();
                    int verified = 0;
                    int unavailable = 0;
                    foreach (var link in traceLinks.Values)
                    {
                        ct.ThrowIfCancellationRequested();
                        string status;
                        string? entryPath = null;
                        string? exportHash = null;
                        try
                        {
                            var trace = await _traces.ReadAsync(link.Reference, ct).ConfigureAwait(false);
                            trace.Validate();
                            if (link.Operations.Count != 1 || !link.Operations.Contains(trace.OperationId))
                                throw new InvalidDataException("Trace operation identity mismatch.");
                            var redactedTrace = trace with { Message = Redact(trace.Message) };
                            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(redactedTrace, JsonOptions));
                            exportHash = Convert.ToHexString(SHA256.HashData(payload));
                            entryPath = "trace/" + verified.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".json";
                            await WriteEntryAsync(zip, entryPath, payload, ct).ConfigureAwait(false);
                            status = "Verified";
                            verified++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (FileNotFoundException) { status = "Missing"; unavailable++; }
                        catch (DirectoryNotFoundException) { status = "Missing"; unavailable++; }
                        catch (InvalidDataException) { status = "CorruptOrMismatched"; unavailable++; }
                        catch (JsonException) { status = "CorruptOrMismatched"; unavailable++; }
                        catch (UnauthorizedAccessException) { status = "Unavailable"; unavailable++; }
                        catch (IOException) { status = "Unavailable"; unavailable++; }
                        indexed.Add(new
                        {
                            link.Reference.RelativeFile, link.Reference.Offset, link.Reference.ByteLength,
                            expectedSha256 = link.Reference.Sha256,
                            eventIds = link.Events,
                            status, entryPath, exportedSha256 = exportHash
                        });
                    }
                    var auditHash = Convert.ToHexString(eventDigest.GetHashAndReset());
                    await WriteEntryAsync(zip, "audit/evidence-index.json",
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(indexed, JsonOptions)), ct).ConfigureAwait(false);
                    await WriteEntryAsync(zip, "manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        schema = "autotest.r0.operation-diagnostic-bundle.v1",
                        exportedUtc = DateTimeOffset.UtcNow,
                        source = "AutoTest.Records.OperationAudit",
                        softwareGitCommit = Redact(_context.GitCommit) ?? "UNKNOWN_NOT_SUPPLIED",
                        softwareAssemblyVersion = typeof(OperationDiagnosticBundleExporter).Assembly.GetName().Version?.ToString() ?? "UNKNOWN",
                        projectId = Pseudonym(_context.ProjectId) ?? "UNKNOWN_NOT_SUPPLIED",
                        profileId = Pseudonym(_context.ProfileId) ?? "UNKNOWN_NOT_SUPPLIED",
                        resourceRevision = Redact(_context.ResourceRevision) ?? "UNKNOWN_NOT_SUPPLIED",
                        scenario = Redact(_context.Scenario) ?? "UNKNOWN_NOT_SUPPLIED",
                        originPolicy = "Each event has its own origin; Mock/Replay never prove a physical run or shipment.",
                        redaction = "Actor and DetailJson omitted; labelled credentials in free text masked; " +
                            "PayloadHexRedacted is already redacted by the producer; never export Seed/Key.",
                        // Filtering is reproduced without exposing DUT identifiers or arbitrary search text.
                        filters = new { searchTextOmitted = query.Search is not null, query.OperationId,
                            runId = Pseudonym(query.RunId), deviceId = Pseudonym(query.DeviceId), query.Kind,
                            query.Origin, query.Level, query.FailuresOnly, query.WithTraceOnly,
                            query.FromUtc, query.ToUtc },
                        events = eventCount,
                        eventsWithoutTrace,
                        verifiedTraces = verified,
                        unavailableTraces = unavailable,
                        // This is about evidence coverage, not physical pass or released shipment.
                        evidenceComplete = eventCount > 0 && unavailable == 0 && eventsWithoutTrace == 0,
                        allReferencedTracesVerified = unavailable == 0,
                        redactedEventDetailFieldsOmitted = true,
                        deviceAndRunIdentifiersPseudonymized = true,
                        sanitizationIsBestEffort = true,
                        physicalObservationVerified = false,
                        auditJsonlSha256 = auditHash
                    }, JsonOptions)), ct).ConfigureAwait(false);
                    receipt = new DiagnosticBundleReceipt(destination, eventCount, verified, unavailable,
                        eventsWithoutTrace, auditHash);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            return receipt;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
    }

    private static string? Redact(string? text)
    {
        if (text is null) return null;
        // This is a guardrail, not a proof that arbitrary free text is safe for public redistribution.
        return SecretLabel.Replace(text, m => m.Groups[1].Value + "=[REDACTED]");
    }

    private sealed class TraceLink(AuditTraceReference reference)
    {
        public AuditTraceReference Reference { get; } = reference;
        public List<Guid> Events { get; } = new();
        public HashSet<Guid> Operations { get; } = new();
    }
}
