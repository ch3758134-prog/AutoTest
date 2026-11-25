using System.IO;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Record actual bus frames, decoded observations and physical device traces in
/// one bounded run evidence file, in addition to typed TEST step facts.
/// Overflow is explicit, never silently represented as a complete recording.
/// </summary>
public sealed class RealRunEvidenceRecorder : IDisposable
{
    private sealed record Line(DateTimeOffset Utc, string Kind, string Description,
        string? Evidence = null, string? FlashEvidence = null);
    private readonly HardwareConnectionService _hardware;
    private readonly LiveDbcSignalService _signals;
    private readonly object _gate = new();
    private readonly List<Line> _records = new();
    private long _dropped;
    private long _sourceLost;
    private long _observedFrames;
    private EvidenceRef? _activeFlash;
    private const int MaximumEvents = 100000;

    public RealRunEvidenceRecorder(HardwareConnectionService hardware, LiveDbcSignalService signals)
    {
        _hardware = hardware; _signals = signals;
        _hardware.FrameObserved += OnFrame;
        _hardware.EvidenceRecorded += OnHardware;
        _signals.SignalReceived += OnSignal;
    }
    private void Append(Line line)
    {
        lock (_gate)
        {
            if (_records.Count == MaximumEvents) { _dropped++; return; }
            if (_activeFlash is { } flash && (line.Kind is "DEVICE" or "CAN_RX"))
                line = line with { FlashEvidence = flash.ToString() };
            _records.Add(line);
        }
    }
    private void OnFrame(CanObservation observed)
    {
        Interlocked.Increment(ref _observedFrames);
        if (observed.LostFrames != 0) Interlocked.Exchange(ref _sourceLost, observed.LostFrames);
        var f = observed.Frame;
        Append(new Line(observed.Time.Utc, "CAN_RX",
            f.Role + " " + (f.ExtendedId ? "EXT" : "STD") +
            " 0x" + f.Id.ToString("X") +
            " DLC=" + f.Dlc + " DATA=" + Convert.ToHexString(f.Data.ToArray()) +
            " LOST=" + observed.LostFrames, observed.Evidence.ToString()));
    }
    private void OnSignal(string name, TestSignalResult value)
    {
        if (_hardware.IsFlashSecuritySensitive) return; // Never archive security-related values.
        Append(new Line(value.Observation?.Time.Utc ?? DateTimeOffset.UtcNow,
            "DBC_SIGNAL", name + " status=" + value.Status +
            " actual=" + (value.Observation?.Value?.ToString("R") ?? "NULL") +
            " unit=" + (value.Observation?.Unit ?? ""),
            value.Observation?.Evidence.ToString()));
    }
    private void OnHardware(string eventText) =>
        Append(new Line(DateTimeOffset.UtcNow, "DEVICE", eventText));

    /// <summary>Begin a real physical FLASH observation window, not a fabricated
    /// success fact. Only the actual USB/CAN/UDS/device events in this window
    /// can later back a FLASH StepOccurrence artifact.</summary>
    public EvidenceRef BeginFlashCapture()
    {
        lock (_gate)
        {
            if (_activeFlash is not null)
                throw new InvalidOperationException("FLASH evidence window already active.");
            _activeFlash = EvidenceRef.New();
            Append(new Line(DateTimeOffset.UtcNow, "FLASH_CAPTURE", "FLASH_CAPTURE_START"));
            return _activeFlash.Value;
        }
    }

    public void EndFlashCapture(EvidenceRef reference)
    {
        lock (_gate)
        {
            if (_activeFlash != reference)
                throw new InvalidOperationException("FLASH evidence window mismatch.");
            Append(new Line(DateTimeOffset.UtcNow, "FLASH_CAPTURE", "FLASH_CAPTURE_END"));
            _activeFlash = null;
        }
    }

    public byte[]? FindFlashEvidence(EvidenceRef evidence)
    {
        if (evidence.Value == Guid.Empty) return null;
        Line[] observed;
        lock (_gate)
            observed = _records.Where(l => l.FlashEvidence == evidence.ToString()).ToArray();
        // A capture window alone is not evidence that programming actually started.
        if (!observed.Any(l => l.Kind == "DEVICE" &&
            l.Description.StartsWith("FLASH", StringComparison.Ordinal))) return null;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            EvidenceId = evidence.Value,
            Source = "R1_REAL_FLASH_DEVICE_AND_CAN_TRACE",
            Observations = observed
        });
    }

    public (long DroppedEvents, long ObservedFrames) CaptureHealth()
    {
        lock (_gate) return (_dropped + Interlocked.Read(ref _sourceLost),
            Interlocked.Read(ref _observedFrames));
    }

    public byte[]? FindRawFrameEvidence(EvidenceRef evidence)
    {
        if (evidence.Value == Guid.Empty) return null;
        Line[] found;
        lock (_gate) found = _records.Where(l => l.Evidence == evidence.ToString()).ToArray();
        if (!found.Any(l => l.Kind == "CAN_RX")) return null;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            EvidenceId = evidence.Value, Source = "REAL_USB_CAN_RX",
            Observations = found
        });
    }

    public async Task<string> SaveAsync(string root, RealRunResult report, CancellationToken ct)
    {
        Line[] lines;
        long dropped;
        lock (_gate) { lines = _records.ToArray(); dropped = _dropped; }
        var folder = Path.Combine(Path.GetFullPath(root), "test-records");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, report.Run.Value.ToString("N") + ".json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1,
                RunId = report.Run.Value,
                report.PlanFingerprint,
                Verdict = report.Verdict.ToString(),
                CreatedUtc = DateTimeOffset.UtcNow,
                report.Facts,
                Variables = report.Variables,
                EventCount = lines.Length,
                DroppedEvents = dropped,
                Events = lines
            }, new JsonSerializerOptions { WriteIndented = true });
            await using (var stream = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            File.Move(temp, path);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Same physical subscriptions as case evidence, active across
    /// inter-case gaps. This is a supplementary REAL project timeline, not a
    /// TEST verdict or a new recording engine.</summary>
    public async Task<string> SaveProjectTimelineAsync(string root, Guid projectRunId,
        CancellationToken ct)
    {
        if (projectRunId == Guid.Empty)
            throw new ArgumentException("Missing project RunId.");
        Line[] events;
        long dropped, observed;
        lock (_gate)
        {
            events = _records.ToArray();
            dropped = _dropped + Interlocked.Read(ref _sourceLost);
            observed = Interlocked.Read(ref _observedFrames);
        }
        var folder = Path.Combine(Path.GetFullPath(root),
            "test-records", "project-runs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, projectRunId.ToString("N") + ".timeline.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1, ProjectRunId = projectRunId,
                Source = "ACTUAL_USB_CAN_RX_AND_DEVICE_OBSERVATIONS",
                ContinuousAcrossCaseBoundaries = true,
                ObservedFrames = observed, DroppedEvents = dropped,
                EventCount = events.Length, Events = events,
                SavedUtc = DateTimeOffset.UtcNow
            });
            await using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            if (!System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(path)).AsSpan().SequenceEqual(
                    System.Security.Cryptography.SHA256.HashData(bytes)))
                throw new IOException("整组 CAN 时间线归档哈希回读不一致。");
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        _hardware.FrameObserved -= OnFrame;
        _hardware.EvidenceRecorded -= OnHardware;
        _signals.SignalReceived -= OnSignal;
    }
}
