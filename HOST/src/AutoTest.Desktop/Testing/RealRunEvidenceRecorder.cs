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
        string? Evidence = null);
    private readonly HardwareConnectionService _hardware;
    private readonly LiveDbcSignalService _signals;
    private readonly object _gate = new();
    private readonly List<Line> _records = new();
    private long _dropped;
    private long _sourceLost;
    private long _observedFrames;
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

    public void Dispose()
    {
        _hardware.FrameObserved -= OnFrame;
        _hardware.EvidenceRecorded -= OnHardware;
        _signals.SignalReceived -= OnSignal;
    }
}
