using System.IO;
using System.Runtime.CompilerServices;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Desktop.Shell.R1;

/// <summary>
/// INTEGRATION-only non-owning binding for the single DEVICE-owned CAN Hub.
/// It never constructs or disposes USB sessions, CAN hubs, or diagnostic TX leases.
/// The physical transmit side is deliberately NOT exposed here.
/// </summary>
public sealed class R1SharedCanSource
{
    private readonly object _gate = new();
    private ICanFrameHub? _hub;
    private Func<bool>? _connected;
    private Func<long>? _generation;
    private Func<bool>? _traceHealthy;

    public bool PhysicalUdsEnabled => false;

    /// <summary>DEVICE provides the existing source after its owner-approved port is available.</summary>
    public void BindNonOwning(ICanFrameHub hub, Func<bool> connected,
        Func<long> generation, Func<bool> traceHealthy)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(traceHealthy);
        lock (_gate)
        {
            if (_hub is not null && !ReferenceEquals(_hub, hub))
                throw new InvalidOperationException("R1_SHARED_CAN_ALREADY_BOUND: another Hub is already attached.");
            _hub = hub;
            _connected = connected;
            _generation = generation;
            _traceHealthy = traceHealthy;
        }
    }

    /// <summary>Detach the binding; ownership and disposal remain with DEVICE.</summary>
    public void Unbind(ICanFrameHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        lock (_gate)
        {
            if (!ReferenceEquals(_hub, hub))
                throw new InvalidOperationException("R1_SHARED_CAN_MISMATCH: cannot detach another device's Hub.");
            _hub = null;
            _connected = null;
            _generation = null;
            _traceHealthy = null;
        }
    }

    public bool TryGetDutReceiveOnly(out ICanFrameHub? receiveOnly, out string reason)
    {
        receiveOnly = null;
        lock (_gate)
        {
            if (_hub is null || _connected is null || _generation is null || _traceHealthy is null)
            {
                reason = "NO_SHARED_DEVICE_HUB";
                return false;
            }
            if (!_connected())
            {
                reason = "SHARED_DEVICE_DISCONNECTED";
                return false;
            }
            if (!_traceHealthy())
            {
                reason = "SHARED_DEVICE_TRACE_UNHEALTHY";
                return false;
            }
            long generation = _generation();
            if (generation <= 0)
            {
                reason = "SHARED_DEVICE_GENERATION_UNKNOWN";
                return false;
            }
            receiveOnly = new DutCanReceiveOnlyFacade(this, _hub, generation);
            reason = "RECEIVE_ONLY_PHYSICAL_TX_DENIED";
            return true;
        }
    }

    private bool IsCurrent(ICanFrameHub hub, long generation)
    {
        lock (_gate)
        {
            return ReferenceEquals(_hub, hub) &&
                _connected?.Invoke() == true &&
                _traceHealthy?.Invoke() == true &&
                _generation?.Invoke() == generation;
        }
    }

    private sealed class DutCanReceiveOnlyFacade(
        R1SharedCanSource source, ICanFrameHub existingHub, long generation) : ICanFrameHub
    {
        public async IAsyncEnumerable<CanObservation> SubscribeAsync(
            CanSubscriptionSpec spec, [EnumeratorCancellation] CancellationToken ct)
        {
            if (spec.Role != BusRole.DutCan)
                throw new InvalidOperationException("R1_DUT_CAN_ONLY: KeySim cannot be subscribed via DUT diagnostic port.");
            if (!source.IsCurrent(existingHub, generation))
                throw new IOException("R1_SHARED_SESSION_STALE_BEFORE_SUBSCRIBE");

            await foreach (var observation in existingHub.SubscribeAsync(spec, ct).WithCancellation(ct))
            {
                if (!source.IsCurrent(existingHub, generation) ||
                    observation.SessionGeneration != generation)
                    throw new IOException("R1_SHARED_SESSION_GENERATION_CHANGED");
                if (observation.LostFrames != 0)
                    throw new IOException("R1_CAN_RX_LOSS_UNVERIFIED");
                yield return observation;
            }
        }

        public ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
            CanTxRequest tx, LeaseToken lease, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected(
                "R1_PHYSICAL_TX_DENIED", "This INTEGRATION receive-only facade never transmits CAN frames."));
        }
    }
}
