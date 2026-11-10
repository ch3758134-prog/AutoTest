using System.Collections.Immutable;
using System.Globalization;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Projects.Dbc;
using AutoTest.Testing.Lab;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// Authorised physical READ-ONLY test adapter. A step named Observe consumes an
/// actual CAN1 received frame decoded through the current project DBC. It never
/// sends CAN or changes ECU power, IGN, keypad, fixture or flash.
/// </summary>
public sealed class R2PhysicalCanReadOnlyLabStation : IAuthorizedReadOnlyPhysicalLabStation
{
    private readonly V1Workspace _workspace;
    private readonly HardwareBenchController _controller;
    private readonly CanFrameHub _hub;
    private readonly AssetRef _dbcAsset;
    private readonly IReadOnlyDictionary<string, (uint FrameId, bool Extended)> _signals;
    private readonly bool _approved;

    public R2PhysicalCanReadOnlyLabStation(V1Workspace workspace,
        HardwareBenchController controller, bool operatorApproved)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (!operatorApproved)
            throw new UnauthorizedAccessException("PHYSICAL_READ_ONLY_OPERATOR_APPROVAL_REQUIRED");
        if (controller.State != UsbSessionState.Connected ||
            !controller.ProtocolVersionVerified || controller.Hub is null ||
            controller.Generation <= 0)
            throw new IOException("PHYSICAL_STATION_NOT_CONNECTED");
        _hub = controller.Hub;
        SessionGeneration = controller.Generation;
        if (workspace.Database is null || workspace.DbcAssetId is null)
            throw new IOException("PHYSICAL_TEST_DBC_REQUIRED");
        var asset = workspace.Assets.Single(a => a.Id == workspace.DbcAssetId.Value);
        _dbcAsset = asset.ToReference();
        var all = workspace.Database.Messages.SelectMany(m => m.Signals.Select(s =>
            (s.Name, m.Id, m.IsExtended))).ToArray();
        if (all.GroupBy(x => x.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new IOException("PHYSICAL_TEST_SIGNAL_AMBIGUOUS: duplicate signal names across DBC messages.");
        _signals = all.ToDictionary(x => x.Name, x => (x.Id, x.IsExtended), StringComparer.Ordinal);
        _approved = true;
    }

    public long SessionGeneration { get; }
    public bool IsReadOnlyAuthorized => _approved && Current();

    private bool Current()
    {
        if (_controller.State != UsbSessionState.Connected ||
            !ReferenceEquals(_controller.Hub, _hub) ||
            _controller.Generation != SessionGeneration ||
            _workspace.DbcAssetId != _dbcAsset.Id ||
            _workspace.Database is null)
            return false;
        var asset = _workspace.Assets.SingleOrDefault(x => x.Id == _dbcAsset.Id);
        return asset is not null && asset.Revision == _dbcAsset.Revision.Value &&
            string.Equals(asset.Sha256, _dbcAsset.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<LabObservation> ObserveAsync(string name, Guid runId,
        LabSource source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (source != LabSource.Physical || !IsReadOnlyAuthorized)
            throw new UnauthorizedAccessException("PHYSICAL_READONLY_SESSION_STALE");
        if (!_signals.TryGetValue(name, out var def))
            return new LabObservation(name, null, LabQuality.Missing, LabSource.Physical,
                runId, SessionGeneration);
        var signal = new SignalRef(_dbcAsset, BusRole.DutCan, def.FrameId, name,
            _workspace.Database!.FindSignal(def.FrameId, name, def.Extended).Unit);
        var dbc = new AssetEntryWithDbc(
            _workspace.Assets.Single(x => x.Id == _dbcAsset.Id), _workspace.Database);
        var decoder = new DecodedSignalPublisher(dbc);
        var spec = new CanSubscriptionSpec(BusRole.DutCan, ImmutableArray.Create(def.FrameId),
            CanBackpressurePolicy.DropWithLossFact, 1024);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await foreach (var observed in _hub.SubscribeAsync(spec, timeout.Token)
                .ConfigureAwait(false))
            {
                if (!IsReadOnlyAuthorized || observed.SessionGeneration != SessionGeneration)
                    throw new IOException("PHYSICAL_READONLY_SESSION_CHANGED");
                if (observed.Frame.ExtendedId != def.Extended) continue;
                if (observed.Time.Source != TimeSource.HostArrival ||
                    observed.Time.Quality != TimeQuality.HostArrivalOnly ||
                    observed.EvidenceQuality != EvidenceQuality.HostObserved ||
                    observed.Evidence.Value == Guid.Empty ||
                    observed.LostFrames != 0)
                    return new LabObservation(name, null, LabQuality.Invalid,
                        LabSource.Physical, runId, SessionGeneration,
                        observed.Evidence.Value.ToString("D"));
                var decoded = decoder.ObserveSignal(signal, observed);
                if (decoded.Quality == EvidenceQuality.Unknown ||
                    !decoded.Value.HasValue || !double.IsFinite(decoded.Value.Value))
                    return new LabObservation(name, null, LabQuality.Invalid,
                        LabSource.Physical, runId, SessionGeneration,
                        observed.Evidence.Value.ToString("D"));
                return new LabObservation(name,
                    decoded.Value.Value.ToString("R", CultureInfo.InvariantCulture),
                    LabQuality.Observed, LabSource.Physical, runId, SessionGeneration,
                    observed.Evidence.Value.ToString("D"));
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested &&
            !cancellation.IsCancellationRequested)
        {
            // No received physical CAN frame in the explicit observation window.
        }
        cancellation.ThrowIfCancellationRequested();
        if (!IsReadOnlyAuthorized)
            throw new IOException("PHYSICAL_READONLY_SESSION_CHANGED");
        return new LabObservation(name, null, LabQuality.Missing, LabSource.Physical,
            runId, SessionGeneration);
    }

    public ValueTask<LabActionReceipt> SetControllerAsync(LabControllerRef binding,
        Guid runId, LabSource source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new LabActionReceipt(false,
            "PHYSICAL_ACTUATION_NOT_AUTHORIZED"));
    }
}
