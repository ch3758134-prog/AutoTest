using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Projects.Dbc;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>
/// The formal R1 Wave page consumes the SAME controller/hub as Station and CAN.
/// No second WinUSB connection, no synthetic frames, and no physical CAN TX.
/// The pinned DBC asset, physical USB generation and host-arrival clock are
/// checked before and throughout the capture.
/// </summary>
public sealed class R1PhysicalWaveSignalSource : IWaveSignalSource
{
    private readonly V1Workspace _workspace;
    private readonly Func<HardwareBenchController?> _controller;

    public R1PhysicalWaveSignalSource(V1Workspace workspace,
        Func<HardwareBenchController?> controller)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        WaveTimeQuality.ValidateSignal(signal);
        var controller = _controller();
        if (controller is null || controller.State != UsbSessionState.Connected ||
            !controller.ProtocolVersionVerified || controller.Hub is null)
            throw new WaveCaptureException("PHYSICAL_CAN_NOT_CONNECTED",
                "Connect and verify the existing Station USB/CAN1 session before Live capture.");
        var generation = controller.Generation;
        if (generation <= 0)
            throw new WaveCaptureException("SESSION_GENERATION_UNAVAILABLE",
                "A valid physical device session generation is required.");
        var dbc = CurrentDbc(signal);
        var publisher = new DecodedSignalPublisher(dbc);
        // SignalRef currently has no frame-format bit. Ambiguous 11/29-bit
        // definitions must fail closed rather than silently decode the wrong bus frame.
        var extendedId = dbc.Database.FindMessage(signal.FrameId).IsExtended;
        // HostArrivalOnly is the actual 0x41 firmware evidence quality:
        // the controller does not provide synchronized ECU sampling timestamps.
        var origin = TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
        var spec = new CanSubscriptionSpec(signal.Role, ImmutableArray.Create(signal.FrameId),
            CanBackpressurePolicy.DropWithLossFact, 4096);
        return ValueTask.FromResult(new WaveSignalSubscription(origin,
            ReadAsync(controller, generation, signal, extendedId, publisher, spec, ct)));
    }

    private AssetEntryWithDbc CurrentDbc(SignalRef signal)
    {
        if (_workspace.Database is null || _workspace.DbcAssetId is null)
            throw new WaveCaptureException("DBC_NOT_IMPORTED",
                "Import a revision-pinned project DBC before real waveform capture.");
        var asset = _workspace.Assets.SingleOrDefault(x => x.Id == _workspace.DbcAssetId.Value);
        if (asset is null)
            throw new WaveCaptureException("DBC_ASSET_MISSING", "Current DBC asset is not available.");
        var pinned = new AssetEntryWithDbc(asset, _workspace.Database);
        _ = DbcSignalResolver.Resolve(signal, pinned);
        return pinned;
    }

    private async IAsyncEnumerable<SignalObservation> ReadAsync(
        HardwareBenchController controller, long generation, SignalRef signal,
        bool extendedId, DecodedSignalPublisher publisher, CanSubscriptionSpec spec,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // A physical frame may never be accepted from a prior USB generation.
        await foreach (var observation in controller.Hub!.SubscribeAsync(spec, ct)
            .ConfigureAwait(false))
        {
            if (!ReferenceEquals(_controller(), controller) ||
                controller.State != UsbSessionState.Connected ||
                controller.Generation != generation)
                throw new WaveCaptureException("SESSION_GENERATION_CHANGED",
                    "Physical controller disconnected or reconnected during waveform capture.");
            _ = CurrentDbc(signal);
            if (observation.SessionGeneration != generation)
                throw new WaveCaptureException("CAN_GENERATION_MISMATCH",
                    "A CAN frame from another USB session was received.");
            if (observation.Frame.Role != signal.Role ||
                observation.Frame.Id != signal.FrameId ||
                observation.Frame.ExtendedId != extendedId)
                continue;
            if (observation.Time.Source != TimeSource.HostArrival ||
                observation.Time.Quality != TimeQuality.HostArrivalOnly)
                throw new WaveCaptureException("CAN_TIME_PROVENANCE_INVALID",
                    "Only physical host-arrival CAN frames may enter Live Wave.");
            yield return publisher.ObserveSignal(signal, observation);
        }
        if (!ct.IsCancellationRequested &&
            (controller.State != UsbSessionState.Connected || controller.Generation != generation))
            throw new WaveCaptureException("SESSION_GENERATION_CHANGED",
                "The shared CAN feed stopped after the controller disconnected.");
    }
}
