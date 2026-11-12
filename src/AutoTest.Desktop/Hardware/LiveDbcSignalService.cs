using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Hardware;

/// <summary>
/// Single shared live CAN-to-DBC interpretation service. Subscribes to the existing
/// station receive feed, never opens a second USB session. All consumers see the
/// same DBC revision, sample time and loss evidence.
/// </summary>
public sealed class LiveDbcSignalService : ITestSignalPort, IWaveSignalSource, IDisposable
{
    private sealed record Bound(TestTargetMapping Mapping, SignalRef Ref,
        DbcLiveSignalDecoder Decoder);
    private sealed class Sink
    {
        public required string Name;
        public required Channel<TestSignalResult> Channel;
        public long Dropped;
    }

    private readonly HardwareConnectionService _hardware;
    private readonly ProjectWorkspaceService _projects;
    private readonly object _gate = new();
    private readonly Dictionary<string, Bound> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TestSignalResult> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Sink> _sinks = new();
    private bool _disposed;
    private long _generation;
    public event Action<string, TestSignalResult>? SignalReceived;

    public LiveDbcSignalService(HardwareConnectionService hardware,
        ProjectWorkspaceService projects)
    {
        _hardware = hardware;
        _projects = projects;
        _hardware.FrameObserved += OnFrame;
        _projects.CurrentChanged += OnProjectChanged;
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _bindings.Clear();
            _latest.Clear();
            foreach (var sink in _sinks) sink.Channel.Writer.TryComplete();
            _sinks.Clear();
        }
    }

    public async Task BindAsync(IEnumerable<TestTargetMapping> mappings, CancellationToken ct = default)
    {
        var project = _projects.Current ?? throw new InvalidOperationException("请先加载工程。");
        var next = new Dictionary<string, Bound>(StringComparer.OrdinalIgnoreCase);
        var dbcs = new Dictionary<AssetId, (AssetEntry Asset, DbcDatabase Dbc)>();
        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();
            if (mapping.Kind != TestTargetKind.Signal) continue;
            mapping.Validate();
            var asset = project.Definition.Assets.SingleOrDefault(a => a.Id == mapping.Asset)
                ?? throw new ProjectDataException("MAPPING_ASSET_MISSING", mapping.Name);
            if (asset.Kind != ProjectWorkspaceService.DbcKind ||
                asset.Revision != mapping.AssetRevision ||
                !string.Equals(asset.Sha256, mapping.AssetSha256, StringComparison.OrdinalIgnoreCase))
                throw new ProjectDataException("MAPPING_DBC_STALE", mapping.Name);
            if (!dbcs.TryGetValue(asset.Id, out var entry))
            {
                var file = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
                var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
                if (!asset.VerifyHash(bytes))
                    throw new ProjectDataException("MAPPING_DBC_CHANGED", asset.Name);
                entry = (asset, DbcSource.Load(bytes).Parse(asset.Name));
                dbcs.Add(asset.Id, entry);
            }
            var message = entry.Dbc.FindMessage(mapping.CanId);
            if (message.IsExtended != mapping.Extended ||
                !message.Signals.Any(s => s.Name == mapping.SignalName && s.Unit == mapping.Unit))
                throw new ProjectDataException("MAPPING_DBC_SIGNAL", mapping.Name);
            var reference = new SignalRef(asset.ToReference(), mapping.Role,
                mapping.CanId, mapping.SignalName, mapping.Unit);
            if (!next.TryAdd(mapping.Name, new Bound(mapping, reference,
                    new DbcLiveSignalDecoder(asset, entry.Dbc))))
                throw new ProjectDataException("MAPPING_DUPLICATE", mapping.Name);
        }
        if (_projects.Current?.ManifestPath != project.ManifestPath)
            throw new ProjectDataException("PROJECT_CHANGED", "工程已切换，放弃信号绑定。");
        lock (_gate)
        {
            _bindings.Clear();
            foreach (var item in next) _bindings.Add(item.Key, item.Value);
            _latest.Clear();
            foreach (var sink in _sinks) sink.Channel.Writer.TryComplete();
            _sinks.Clear();
            _generation = 0;
        }
    }

    public SignalRef ResolveSignal(TestTargetMapping mapping)
    {
        lock (_gate)
        {
            if (!_bindings.TryGetValue(mapping.Name, out var bound) ||
                bound.Mapping != mapping)
                throw new InvalidOperationException("DBC mapping is not bound to the active project.");
            return bound.Ref;
        }
    }

    private void OnFrame(CanObservation frame)
    {
        List<(string Name, TestSignalResult Value)> ready = [];
        lock (_gate)
        {
            if (_disposed) return;
            if (_generation != frame.SessionGeneration)
            {
                _generation = frame.SessionGeneration;
                _latest.Clear();
            }
            foreach (var item in _bindings)
            {
                var b = item.Value;
                if (b.Ref.Role != frame.Frame.Role || b.Ref.FrameId != frame.Frame.Id) continue;
                var observation = b.Decoder.Decode(b.Ref, frame);
                var state = frame.LostFrames > 0 ? TestSignalStatus.LostFrames :
                    observation.Value.HasValue && !observation.HasGap
                        ? TestSignalStatus.Valid : TestSignalStatus.DecodeError;
                var result = new TestSignalResult(state, observation, state.ToString());
                _latest[item.Key] = result;
                ready.Add((item.Key, result));
                foreach (var sink in _sinks)
                {
                    if (sink.Name != item.Key) continue;
                    if (sink.Dropped != 0)
                    {
                        var loss = new TestSignalResult(TestSignalStatus.LostFrames,
                            observation with { HasGap = true }, "Signal subscriber overflow");
                        if (sink.Channel.Writer.TryWrite(loss)) sink.Dropped = 0;
                    }
                    if (!sink.Channel.Writer.TryWrite(result)) sink.Dropped++;
                }
            }
        }
        foreach (var item in ready)
            try { SignalReceived?.Invoke(item.Name, item.Value); }
            catch { /* Monitoring UI must not interrupt the receive bridge. */ }
    }

    public TestSignalResult Latest(TestTargetMapping target, TimeSpan maxAge)
    {
        if (!_hardware.IsConnected)
            return new(TestSignalStatus.Disconnected, null, "Main controller disconnected");
        lock (_gate)
        {
            if (!_bindings.TryGetValue(target.Name, out var b) || b.Mapping != target)
                return new(TestSignalStatus.DecodeError, null, "Unbound or changed DBC mapping");
            if (!_latest.TryGetValue(target.Name, out var found))
                return new(TestSignalStatus.NoFrame, null, "No target frame observed");
            if (found.Observation is { } signal && DateTimeOffset.UtcNow - signal.Time.Utc > maxAge)
                return new(TestSignalStatus.Stale, signal, "CAN signal has not been refreshed");
            return found;
        }
    }

    public async Task<TestSignalResult> ReadAsync(TestTargetMapping target, TestReadMode mode,
        TimeSpan timeout, TimeSpan maxAge, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero || maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (mode == TestReadMode.Latest) return Latest(target, maxAge);
        if (!_hardware.IsConnected)
            return new(TestSignalStatus.Disconnected, null, "Main controller disconnected");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(timeout);
        try
        {
            await foreach (var sample in ObserveAsync(target, wait.Token).WithCancellation(wait.Token))
                return sample;
            return new(TestSignalStatus.Disconnected, null, "Signal stream ended");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(TestSignalStatus.Timeout, null, "No next signal frame within timeout");
        }
        throw new InvalidOperationException("Unreachable observation state");
    }

    public async IAsyncEnumerable<TestSignalResult> ObserveAsync(TestTargetMapping target,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var sink = new Sink
        {
            Name = target.Name,
            Channel = Channel.CreateBounded<TestSignalResult>(new BoundedChannelOptions(1024)
                { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait })
        };
        lock (_gate)
        {
            if (!_bindings.TryGetValue(target.Name, out var b) || b.Mapping != target)
                throw new InvalidOperationException("DBC mapping unavailable or stale.");
            if (_disposed) throw new ObjectDisposedException(nameof(LiveDbcSignalService));
            _sinks.Add(sink);
        }
        try
        {
            await foreach (var sample in sink.Channel.Reader.ReadAllAsync(ct))
                yield return sample;
        }
        finally
        {
            lock (_gate) { _sinks.Remove(sink); sink.Channel.Writer.TryComplete(); }
        }
    }

    public ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct)
    {
        TestTargetMapping mapping;
        lock (_gate)
        {
            mapping = _bindings.Values.SingleOrDefault(b => b.Ref == signal)?.Mapping
                ?? throw new WaveCaptureException("SIGNAL_SOURCE_UNAVAILABLE", "Signal not bound to a TEST mapping.");
        }
        var origin = TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
        return ValueTask.FromResult(new WaveSignalSubscription(origin, StreamWave(mapping, ct)));
    }

    private async IAsyncEnumerable<SignalObservation> StreamWave(TestTargetMapping mapping,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var sample in ObserveAsync(mapping, ct).WithCancellation(ct))
            if (sample.Observation is { } observation) yield return observation;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var sink in _sinks) sink.Channel.Writer.TryComplete();
            _sinks.Clear();
        }
        _hardware.FrameObserved -= OnFrame;
        _projects.CurrentChanged -= OnProjectChanged;
    }
}
