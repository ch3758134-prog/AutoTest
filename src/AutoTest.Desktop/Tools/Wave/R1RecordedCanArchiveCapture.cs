using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>
/// Bounded read-only recorder for actual CAN1 frames from the existing receiver.
/// It cannot transmit, fabricate frames or silently truncate its RX archive.
/// The caller owns the physical session and verifies its generation/DBC revision.
/// </summary>
public static class R1RecordedCanArchiveCapture
{
    public static async Task<RecordedCanArchive> CaptureAsync(
        ICanFrameHub hub, Func<bool> sessionAndDbcStillCurrent,
        AssetRef dbc, long generation, TimeSpan duration, int maxFrames,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(sessionAndDbcStillCurrent);
        if (dbc.Id.Value == Guid.Empty || dbc.Revision.Value < 1 ||
            string.IsNullOrWhiteSpace(dbc.Fingerprint.Sha256) || generation <= 0)
            throw new ArgumentException("Physical session and revision-pinned DBC are required.");
        if (duration < TimeSpan.FromMilliseconds(100) || duration > TimeSpan.FromMinutes(1) ||
            maxFrames is < 2 or > 200_000)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (!sessionAndDbcStillCurrent())
            throw new WaveCaptureException("PHYSICAL_RX_ARCHIVE_NOT_READY",
                "Shared controller or project DBC is no longer current.");

        var start = DateTimeOffset.UtcNow;
        var rows = new List<RecordedCanRxRow>(Math.Min(maxFrames, 4096));
        long lastOffset = -1;
        long previousDrops = 0;
        var spec = new CanSubscriptionSpec(BusRole.DutCan, ImmutableArray<uint>.Empty,
            CanBackpressurePolicy.DropWithLossFact, 8192);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(duration);
        try
        {
            await foreach (var observed in hub.SubscribeAsync(spec, window.Token))
            {
                if (!sessionAndDbcStillCurrent() || observed.SessionGeneration != generation)
                    throw new WaveCaptureException("RX_ARCHIVE_GENERATION_CHANGED",
                        "Controller session or DBC revision changed during recording.");
                if (observed.Frame.Role != BusRole.DutCan ||
                    observed.Time.Source != TimeSource.HostArrival ||
                    observed.Time.Quality != TimeQuality.HostArrivalOnly ||
                    observed.Evidence.Value == Guid.Empty ||
                    observed.EvidenceQuality != EvidenceQuality.HostObserved ||
                    observed.Frame.Dlc != observed.Frame.Data.Length)
                    throw new WaveCaptureException("RX_ARCHIVE_PROVENANCE_INVALID",
                        "Only original CAN1 RX observations with valid physical provenance may be archived.");
                if (observed.LostFrames < previousDrops)
                    throw new WaveCaptureException("RX_ARCHIVE_LOSS_COUNTER_RESET",
                        "CAN loss counter regressed during recording.");
                previousDrops = observed.LostFrames;
                var offset = (observed.Time.Utc - start).Ticks / 10;
                if (offset <= lastOffset)
                    throw new WaveCaptureException("RX_ARCHIVE_NON_MONOTONIC",
                        "Host-arrival times are not strictly increasing at microsecond resolution.");
                if (offset > 3_600_000_000L)
                    throw new WaveCaptureException("RX_ARCHIVE_TIME_RANGE",
                        "Recording exceeds the supported time window.");
                if (rows.Count >= maxFrames)
                    throw new WaveCaptureException("RX_ARCHIVE_LIMIT",
                        "Frame budget exhausted; never export a silently truncated archive.");
                rows.Add(new RecordedCanRxRow(offset, observed.Frame.Role,
                    observed.Frame.Id, observed.Frame.ExtendedId,
                    Convert.ToHexString(observed.Frame.Data.AsSpan()),
                    observed.LostFrames, observed.Evidence.Value));
                lastOffset = offset;
            }
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Normal duration elapsed. External user cancellation never exports partial data.
        }
        ct.ThrowIfCancellationRequested();
        if (!sessionAndDbcStillCurrent())
            throw new WaveCaptureException("RX_ARCHIVE_SESSION_CHANGED",
                "Recording ended after controller/DBC became stale.");
        if (rows.Count < 2)
            throw new WaveCaptureException("RX_ARCHIVE_INSUFFICIENT_FRAMES",
                "At least two valid CAN1 RX frames are required for a replay archive.");
        return new RecordedCanArchive(1, Guid.NewGuid(), dbc.Id.Value,
            dbc.Revision.Value, dbc.Fingerprint.Sha256, start, rows.ToArray());
    }
}
