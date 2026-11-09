using AutoTest.Application.G4;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>
/// The archived-frame path uses the shared CAN Hub and DBC decoder; the legacy generated
/// G4 example is exposed ONLY as Mock (never as an archived Replay). Live acquisition requires
/// a shared decoded-signal capture provider after device/session validation.
/// </summary>
public sealed class G4WaveReplayBackend : IWaveWorkbenchBackend, IWaveArchiveSource, IWaveMockGenerator, IWaveArchiveDiagnosticSource
{
    private readonly G4IntegrationHost _host;
    private readonly WaveReferenceCatalog _catalog;
    private readonly WaveEvidenceStore _evidence;
    private readonly WaveCaptureOriginLedger _origins;
    private readonly WaveSharedLiveProvider? _live;
    private readonly RecordedCanWaveReplayProvider? _recorded;

    public G4WaveReplayBackend(G4IntegrationHost host, string waveRoot,
        WaveSharedLiveProvider? live = null, RecordedCanWaveReplayProvider? recorded = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentException.ThrowIfNullOrWhiteSpace(waveRoot);
        _catalog = new WaveReferenceCatalog(Path.Combine(waveRoot, "reference-catalog"));
        _evidence = new WaveEvidenceStore(Path.Combine(waveRoot, "evidence"));
        _origins = new WaveCaptureOriginLedger(Path.Combine(waveRoot, "provenance"));
        _live = live;
        _recorded = recorded;
    }

    public bool CanCaptureLive => _live is not null;
    public WaveArchiveReport ReadArchiveReport(int maxRows, string? filter = null)
    {
        if (_recorded is null)
            throw new WaveCaptureException("RECORDED_CAN_FEED_NOT_WIRED", "The shared archive reader is not connected.");
        return _recorded.ReadArchiveReport(maxRows, filter);
    }

    public byte[] ReadVerifiedOriginalArchive()
    {
        if (_recorded is null)
            throw new WaveCaptureException("RECORDED_CAN_FEED_NOT_WIRED", "The shared archive reader is not connected.");
        return _recorded.ReadVerifiedOriginalArchive();
    }

    public bool HasRecordedArchive => _recorded?.HasArchive == true;
    public void OpenArchiveFile(string path)
    {
        if (_recorded is null) throw new WaveCaptureException("RECORDED_CAN_FEED_NOT_WIRED",
            "INTEGRATION has not attached the shared CAN Replay-mode publisher.");
        _recorded.LoadArchiveFile(path);
    }

    public IReadOnlyList<WaveSignalChoice> ListSignals()
    {
        var workspace = _host.Workspace;
        if (workspace.Database is null || workspace.DbcAssetId is null)
            return Array.Empty<WaveSignalChoice>();
        var dbc = workspace.Assets.Single(x => x.Id == workspace.DbcAssetId.Value);
        return workspace.Database.Messages.SelectMany(message => message.Signals
            .Where(s => !string.IsNullOrWhiteSpace(s.Unit))
            .Select(s => new WaveSignalChoice(
                new SignalRef(dbc.ToReference(), BusRole.DutCan, message.Id, s.Name, s.Unit),
                $"0x{message.Id:X} / {s.Name} ({s.Unit})")))
            .OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToArray();
    }

    public async Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples,
        CancellationToken ct)
    {
        EnsureCurrent(signal);
        if (_recorded is null || !_recorded.HasArchive)
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_REQUIRED", "Recorded CAN RX frames not loaded.");
        var result = await _recorded.ReplayAsync(signal, samples, ct).ConfigureAwait(false);
        MirrorCapture(result, _recorded.ArchiveSha256);
        return result;
    }

    public async Task<WaveWorkbenchCapture> RunMockAsync(SignalRef signal, int samples,
        CancellationToken ct)
    {
        EnsureCurrent(signal);
        var replay = await _host.Wave.ReplayAsync(signal.FrameId, signal.SignalName, samples, ct)
            .ConfigureAwait(false);
        if (replay.Signal != signal || replay.Series.IsReplay is false)
            throw new WaveCaptureException("REPLAY_IDENTITY_MISMATCH", "DBC revision or replay provenance changed.");
        var result = new WaveWorkbenchCapture(replay.Evidence, replay.Series,
            "Mock / generated CAN frames (ReplayDerived clock)", WaveInputKind.Mock);
        MirrorCapture(result);
        return result;
    }

    private void MirrorCapture(WaveWorkbenchCapture result, string? archiveSha256 = null)
    {
        _evidence.Write(new WaveEvidenceDocument(result.Evidence.Evidence, "Capture", result.Series,
            null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow));
        _origins.Write(result, archiveSha256);
    }

    public async Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal, TimeSpan duration,
        int maxSamples, CancellationToken ct)
    {
        if (_live is null)
            throw new WaveCaptureException("LIVE_SOURCE_NOT_CONNECTED", "Shared physical CAN/DBC source not attached.");
        EnsureCurrent(signal);
        var result = await _live.CaptureAsync(signal, duration, maxSamples, ct).ConfigureAwait(false);
        EnsureCurrent(signal);
        _evidence.Write(_live.ReadCapture(result.Evidence.Evidence));
        _origins.Write(result);
        return result;
    }

    public WaveReference? ReadReference(SignalRef signal)
    {
        EnsureCurrent(signal);
        return _catalog.LatestFor(signal);
    }

    public WaveReference SaveReference(WaveSeries capture, string name)
    {
        EnsureCurrent(capture.Signal);
        return _catalog.Save(name, capture);
    }

    public Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
        WaveReference reference, WaveComparisonConfig config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureCurrent(capture.Series.Signal);
        ArgumentNullException.ThrowIfNull(reference);
        // Reload immutable asset: a changed JSON payload or invalid SHA cannot be trusted.
        var pinned = _catalog.Load(reference.Reference);
        var evaluation = WaveComparator.Compare(capture.Series, pinned.Series, config);
        var id = EvidenceRef.New();
        _evidence.Write(new WaveEvidenceDocument(id, "Comparison", capture.Series,
            pinned.Series, pinned.Reference, pinned.Name, capture.Evidence.Evidence, config,
            evaluation, null, null, DateTimeOffset.UtcNow));
        return Task.FromResult(new WaveWorkbenchComparison(evaluation, id));
    }

    private void EnsureCurrent(SignalRef signal)
    {
        if (!ListSignals().Any(x => x.Signal == signal))
            throw new WaveCaptureException("DBC_REVISION_STALE", "Selected DBC signal or revision is not current.");
    }
}

/// <summary>
/// Live-ready capture provider. Operation provenance must be supplied by the shared runtime;
/// Fake/Replay must never be labeled Physical. INTEGRATION owns its lifecycle and feed.
/// </summary>
public sealed class WaveSharedLiveProvider
{
    private readonly WaveCaptureService _capture;
    private readonly string _originLabel;

    public WaveSharedLiveProvider(WaveCaptureService capture, string originLabel)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        if (originLabel is not ("Physical" or "Mock"))
            throw new ArgumentException("Explicit Physical or Mock provenance required.", nameof(originLabel));
        _originLabel = originLabel;
    }

    public async Task<WaveWorkbenchCapture> CaptureAsync(SignalRef signal, TimeSpan duration,
        int maxSamples, CancellationToken ct)
    {
        var capture = await _capture.CaptureAsync(signal,
            new WaveCaptureConfig(duration, maxSamples), ct).ConfigureAwait(false);
        var doc = _capture.ReadEvidence(capture.Evidence);
        if (_originLabel == "Physical" && doc.Actual.Origin.Source is not
            (TimeSource.DeviceSample or TimeSource.HostArrival))
            throw new WaveCaptureException("LIVE_SOURCE_NOT_PHYSICAL", "Replay/Mock was passed as physical capture.");
        if (_originLabel == "Mock" && doc.Actual.Origin.Source is not TimeSource.Simulation)
            throw new WaveCaptureException("MOCK_PROVENANCE_MISMATCH", "Mock feed must use simulation provenance.");
        return new WaveWorkbenchCapture(capture, doc.Actual, _originLabel + " / shared CAN + DBC",
            _originLabel == "Physical" ? WaveInputKind.Physical : WaveInputKind.Mock);
    }

    public WaveEvidenceDocument ReadCapture(EvidenceRef evidence) => _capture.ReadEvidence(evidence);
}
