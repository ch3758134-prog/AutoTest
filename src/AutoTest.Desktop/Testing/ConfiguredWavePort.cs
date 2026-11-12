using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Wave;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Wave recording is a data acquisition operation, not a test verdict.
/// Both Read and Wave use the same verified live DBC signal source.
/// A comparison result is returned as data for an explicit TEST Expect step.
/// </summary>
public sealed class ConfiguredWavePort : ITestWavePort
{
    private readonly ProjectWorkspaceService _projects;
    private readonly LiveDbcSignalService _signals;
    private readonly WaveCaptureService _capture;
    private readonly WaveReferenceCatalog _catalog;
    private readonly Dictionary<string, WaveCaptureId> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WaveEvidenceRef> _completed = new(StringComparer.OrdinalIgnoreCase);

    public ConfiguredWavePort(ProjectWorkspaceService projects, LiveDbcSignalService signals)
    {
        _projects = projects;
        _signals = signals;
        var context = _projects.Current ?? throw new InvalidOperationException("An open project is required for wave evidence.");
        var root = Path.Combine(context.RootDirectory, "wave-records");
        _catalog = new WaveReferenceCatalog(Path.Combine(root, "references"));
        _capture = new WaveCaptureService(_signals, new WaveEvidenceStore(Path.Combine(root, "evidence")),
            new WaveReferenceRepository(Path.Combine(root, "references", "assets")));
    }

    public async Task StartAsync(TestTargetMapping target, string captureKey, int durationMs,
        CancellationToken ct)
    {
        if (target.Kind != TestTargetKind.Signal)
            throw new ArgumentException("Wave capture requires a mapped DBC signal.");
        if (string.IsNullOrWhiteSpace(captureKey))
            throw new ArgumentException("Wave capture identifier required.");
        if (_active.ContainsKey(captureKey) || _completed.ContainsKey(captureKey))
            throw new InvalidOperationException("Wave capture key already used in this run.");
        if (durationMs <= 0 || durationMs > 3600000)
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        var signal = _signals.ResolveSignal(target);
        var id = await _capture.StartAsync(signal,
            new WaveCaptureConfig(TimeSpan.FromMilliseconds(durationMs), 100000), ct);
        _active.Add(captureKey, id);
    }

    public async Task<TestWaveResult> StopAsync(string captureKey, CancellationToken ct)
    {
        if (!_active.Remove(captureKey, out var id))
            return new(false, "CAPTURE_MISSING", null, null, "Wave capture has not started.");
        try
        {
            var evidence = await _capture.StopAsync(id, ct);
            _completed[captureKey] = evidence;
            var recorded = _capture.ReadEvidence(evidence.Evidence);
            return new(true, "CAPTURED", recorded.Actual.Samples.Length.ToString(),
                evidence.Evidence.ToString(), "Actual DBC-decoded samples recorded; no judgement yet.");
        }
        catch (WaveCaptureException ex)
        {
            return new(false, ex.Code, null, null, ex.Message);
        }
    }

    public async Task<TestWaveResult> AnalyzeAsync(string captureKey, string referenceKey,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_completed.TryGetValue(captureKey, out var evidence))
            return new(false, "CAPTURE_NOT_AVAILABLE", null, null, "Complete capture before analysis.");
        var actual = _capture.ReadEvidence(evidence.Evidence).Actual;
        var matches = _catalog.List().Where(e => e.Name == referenceKey &&
            e.Signal == actual.Signal).ToArray();
        if (matches.Length != 1)
            return new(false, "REFERENCE_NOT_UNIQUE", null, evidence.Evidence.ToString(),
                "Choose one existing named reference for this exact DBC signal revision.");
        var evaluated = await _capture.CompareAsync(evidence, matches[0].Reference,
            new CompareProfile(0.05, 0.95));
        var available = evaluated.Verdict is Verdict.Pass or Verdict.Fail or Verdict.Inconclusive;
        return new(available, evaluated.Verdict.ToString(), evaluated.Verdict.ToString(),
            string.Join(",", evaluated.Evidence.Select(x => x.ToString())), evaluated.Reason);
    }

    /// <summary>Explicit user action only; never silently bless a failed DUT trace as a reference.</summary>
    public WaveReference PromoteCapturedReference(string captureKey, string name)
    {
        if (!_completed.TryGetValue(captureKey, out var evidence))
            throw new InvalidOperationException("No completed capture.");
        var series = _capture.ReadEvidence(evidence.Evidence).Actual;
        return _catalog.Save(name, series);
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        foreach (var key in _active.Keys.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            await StopAsync(key, ct);
        }
    }
}
