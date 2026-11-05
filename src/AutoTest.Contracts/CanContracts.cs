using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public enum BusRole { DutCan, KeypadCan }
public enum CanBackpressurePolicy { DropWithLossFact, BlockProducer }
public enum CanDirection { Rx, Tx }
public sealed record BusRoleMap(RevisionFingerprint Revision, int DutPort, int KeypadPort);
public sealed record StationProfile(StationId Station, BusRoleMap Roles,
    string VoltageProfile, RevisionFingerprint ProfileRevision);

// ImmutableArray avoids exposing mutable byte[]. DLC refers to classic CAN payload length.
public sealed record CanFrame(BusRole Role, bool ExtendedId, uint Id, byte Dlc,
    ImmutableArray<byte> Data)
{
    public static CanFrame Create(BusRole role, bool extended, uint id, ReadOnlySpan<byte> payload)
    {
        if (id > (extended ? 0x1FFFFFFFu : 0x7FFu) || payload.Length > 8)
            throw new ArgumentOutOfRangeException(nameof(id), "Invalid classic CAN ID or DLC.");
        return new(role, extended, id, (byte)payload.Length, ImmutableArray.CreateRange(payload.ToArray()));
    }
}
public sealed record CanObservation(CanFrame Frame, TimePoint Time, EvidenceQuality EvidenceQuality,
    long SessionGeneration, long LostFrames, EvidenceRef Evidence);
public sealed record CanSubscriptionSpec(BusRole Role, ImmutableArray<uint> AllowedIds,
    CanBackpressurePolicy Backpressure, int Capacity);
public readonly record struct LeaseToken(Guid Value, string Owner, long SessionGeneration,
    DateTimeOffset ExpiresUtc);
public sealed record CanTxRequest(CanFrame Frame, Guid CorrelationId);
public sealed record CanTxReceipt(Guid OperationId, AckKind Ack, TimePoint Time,
    EvidenceRef Evidence);
public interface ICanFrameHub
{
    IAsyncEnumerable<CanObservation> SubscribeAsync(CanSubscriptionSpec spec, CancellationToken ct);
    ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(CanTxRequest tx, LeaseToken lease,
        CancellationToken ct);
}
public interface ITransmitLeaseService
{
    ValueTask<OperationResult<LeaseToken>> AcquireAsync(BusRole role, string owner,
        TimeSpan duration, CancellationToken ct);
    ValueTask<OperationResult<Unit>> ReleaseAsync(LeaseToken lease, CancellationToken ct);
}
