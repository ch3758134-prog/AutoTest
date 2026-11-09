using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Projects.Dbc;
using AutoTest.Specialists.Wave;

namespace AutoTest.Application.G4;

/// <summary>Only shared ICanFrameHub data are decoded; source identity is checked against live DBC asset revision.</summary>
public sealed class HubDbcSignalSource : IWaveSignalSource
{
    private readonly ICanFrameHub _hub;
    private readonly V1Workspace _workspace;
    private TimePoint _origin;
    public HubDbcSignalSource(ICanFrameHub hub, V1Workspace workspace)
    { _hub = hub; _workspace = workspace; _origin = TimePoint.FromReplay(DateTimeOffset.UtcNow, 0); }
    public void SetReplayOrigin(TimePoint origin)
    {
        if (origin.Source != TimeSource.Replay || origin.Quality != TimeQuality.ReplayDerived)
            throw new ArgumentException("Only explicit offline replay is enabled");
        _origin = origin;
    }
    public ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct)
    {
        if (_workspace.Database is null || _workspace.DbcAssetId is null)
            throw new WaveCaptureException("DBC_NOT_IMPORTED", "Import a revision-pinned DBC first.");
        var asset = _workspace.Assets.Single(x => x.Id == _workspace.DbcAssetId.Value);
        var database = new AssetEntryWithDbc(asset, _workspace.Database);
        _ = DbcSignalResolver.Resolve(signal, database); // fail closed on stale revision
        return ValueTask.FromResult(new WaveSignalSubscription(_origin, Read(signal, database, ct)));
    }
    private async IAsyncEnumerable<SignalObservation> Read(SignalRef signal,
        AssetEntryWithDbc database, [EnumeratorCancellation] CancellationToken ct)
    {
        var publisher = new DecodedSignalPublisher(database);
        var spec = new CanSubscriptionSpec(signal.Role, ImmutableArray.Create(signal.FrameId),
            CanBackpressurePolicy.DropWithLossFact, 1024);
        await foreach (var frame in _hub.SubscribeAsync(spec, ct).ConfigureAwait(false))
        {
            if (frame.Time.Source != TimeSource.Replay || frame.Time.Quality != TimeQuality.ReplayDerived)
                throw new WaveCaptureException("NON_REPLAY_CAN_FRAME", "Physical or untrusted CAN frame rejected.");
            yield return publisher.ObserveSignal(signal, frame);
        }
    }
}

public sealed record WaveReplayResult(WaveEvidenceRef Evidence, WaveSeries Series,
    SignalRef Signal, string SourceLabel);

/// <summary>In-process CAN replay → shared hub → DBC decode → WaveCaptureService → immutable evidence.</summary>
public sealed class HubDbcWaveReplay
{
    private readonly V1Workspace _workspace;
    private readonly CanFrameHub _hub;
    private readonly HubDbcSignalSource _source;
    private readonly WaveEvidenceStore _evidence;
    private readonly WaveReferenceRepository _references;
    private readonly WaveCaptureService _capture;
    private WaveReplayResult? _last;
    private WaveReference? _reference;

    public HubDbcWaveReplay(V1Workspace workspace, CanFrameHub hub, string evidenceRoot)
    {
        _workspace = workspace; _hub = hub;
        _source = new HubDbcSignalSource(hub, workspace);
        _evidence = new WaveEvidenceStore(Path.Combine(evidenceRoot, "captures"));
        _references = new WaveReferenceRepository(Path.Combine(evidenceRoot, "references"));
        _capture = new WaveCaptureService(_source, _evidence, _references);
    }
    public WaveReplayResult? Latest => _last;
    public WaveReference? Reference => _reference;
    public int SubscriberCount => _hub.SubscriberCount;

    public async Task<WaveReplayResult> ReplayAsync(uint? frameId = null,
        string? signalName = null, int sampleCount = 12, CancellationToken ct = default)
    {
        if (sampleCount is < 3 or > 500) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (_workspace.Diagnostics().Any(d => d.StartsWith("REFERENCE_STALE", StringComparison.Ordinal)))
            throw new InvalidOperationException("REFERENCE_STALE");
        if (_workspace.Database is null || _workspace.DbcAssetId is null)
            throw new InvalidOperationException("DBC_REQUIRED");
        var message = frameId.HasValue ? _workspace.Database.FindMessage(frameId.Value) :
            _workspace.Database.Messages.FirstOrDefault() ?? throw new InvalidOperationException("DBC_NO_MESSAGES");
        var definition = signalName is null ? message.Signals.FirstOrDefault() :
            message.Signals.FirstOrDefault(s => s.Name == signalName);
        if (definition is null) throw new InvalidOperationException("DBC_SIGNAL_NOT_FOUND");
        if (string.IsNullOrWhiteSpace(definition.Unit)) throw new InvalidOperationException("WAVE_UNIT_REQUIRED");
        var dbcAsset = _workspace.Assets.Single(x => x.Id == _workspace.DbcAssetId.Value);
        var signal = new SignalRef(dbcAsset.ToReference(), BusRole.DutCan,
            message.Id, definition.Name, definition.Unit);
        var origin = TimePoint.FromReplay(DateTimeOffset.UtcNow, 0);
        _source.SetReplayOrigin(origin);
        var captureId = await _capture.StartAsync(signal,
            new WaveCaptureConfig(TimeSpan.FromSeconds(10), sampleCount + 8), ct).ConfigureAwait(false);
        try
        {
            // Subscribe is lazy. Never drop the first packet to a pre-subscription race.
            for (var tries = 0; _hub.SubscriberCount == 0 && tries < 2000; tries++)
            { ct.ThrowIfCancellationRequested(); await Task.Delay(1, ct).ConfigureAwait(false); }
            if (_hub.SubscriberCount == 0) throw new IOException("CAN_HUB_SUBSCRIPTION_TIMEOUT");
            for (var i = 0; i < sampleCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = new byte[message.Dlc];
                var period = 1 << Math.Min(definition.BitLength, 5);
                DbcSignalCodec.EncodeRaw(definition, (ulong)(i % period), bytes);
                var frame = CanFrame.Create(signal.Role, message.Id > 0x7FF,
                    message.Id, bytes);
                var sampleTime = TimePoint.FromReplay(origin.Utc.AddMilliseconds((i + 1) * 20), i + 1);
                _hub.Publish(new CanObservation(frame, sampleTime, EvidenceQuality.Derived,
                    1, 0, EvidenceRef.New()));
            }
            for (var tries = 0; _capture.Observe(captureId).Samples.Length < sampleCount && tries < 2000; tries++)
            { ct.ThrowIfCancellationRequested(); await Task.Delay(1, ct).ConfigureAwait(false); }
            var evidence = await _capture.StopAsync(captureId, ct).ConfigureAwait(false);
            var doc = _capture.ReadEvidence(evidence.Evidence);
            _last = new WaveReplayResult(evidence, doc.Actual, signal, "CAN-HUB/DBC/REPLAY");
            return _last;
        }
        catch
        {
            try { await _capture.StopAsync(captureId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { /* original failure has priority */ }
            throw;
        }
    }
    public WaveReference SaveReference(string name = "G4 replay baseline")
    {
        if (_last is null) throw new InvalidOperationException("WAVE_CAPTURE_REQUIRED");
        _reference = _references.SaveReference(AssetId.New(), AssetRevision.Create(1), name, _last.Series);
        return _reference;
    }
    public async Task<ComparisonResult> CompareAsync(double tolerance = 0.1,
        double minimumCoverage = 0.9)
    {
        if (_last is null || _reference is null) throw new InvalidOperationException("WAVE_CAPTURE_AND_REFERENCE_REQUIRED");
        return await _capture.CompareAsync(_last.Evidence, _reference.Reference,
            new CompareProfile(tolerance, minimumCoverage)).ConfigureAwait(false);
    }
}
