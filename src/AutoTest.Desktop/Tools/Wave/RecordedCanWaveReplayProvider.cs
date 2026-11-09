using System.Globalization;
using System.IO;
using AutoTest.Projects.Dbc;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

/// <summary>Saved, received CAN frames only. The file format deliberately excludes Tx commands.</summary>
public sealed record RecordedCanRxRow(long OffsetMicroseconds, BusRole Role, uint FrameId,
    bool Extended, string PayloadHex, long LostFrames, Guid TraceEvidence);
public sealed record RecordedCanArchive(int SchemaVersion, Guid RecordId,
    Guid DbcId, int DbcRevision, string DbcSha256, DateTimeOffset StartUtc,
    RecordedCanRxRow[] Frames);

/// <summary>
/// Inject the EXISTING shared CAN hub and its authorized replay-only publisher; never open an
/// independent receiver or USB link. Integration must gate replay mode against physical input.
/// </summary>
public sealed class RecordedCanWaveReplayProvider
{
    private readonly ICanFrameHub _sharedHub;
    private readonly Action<CanObservation> _publishReplay;
    private readonly Func<bool> _replayModeOnly;
    private readonly V1Workspace _workspace;
    private readonly WaveEvidenceStore _evidence;
    private readonly WaveReferenceRepository _references;
    private RecordedCanArchive? _archive;
    private string? _archivePath;
    public string? ArchiveSha256 { get; private set; }
    public bool HasArchive => _archive is not null;

    public RecordedCanWaveReplayProvider(ICanFrameHub sharedHub,
        Action<CanObservation> publishReplay, Func<bool> replayModeOnly,
        V1Workspace workspace, string evidenceRoot)
    {
        _sharedHub = sharedHub ?? throw new ArgumentNullException(nameof(sharedHub));
        _publishReplay = publishReplay ?? throw new ArgumentNullException(nameof(publishReplay));
        _replayModeOnly = replayModeOnly ?? throw new ArgumentNullException(nameof(replayModeOnly));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _evidence = new WaveEvidenceStore(Path.Combine(evidenceRoot, "recorded-captures"));
        _references = new WaveReferenceRepository(Path.Combine(evidenceRoot, "recorded-references"));
    }

    public void LoadArchiveFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _archive = null;
        _archivePath = null;
        ArchiveSha256 = null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 32_000_000)
            throw new InvalidDataException("CAN_REPLAY_ARCHIVE_SIZE_INVALID");
        var bytes = File.ReadAllBytes(path);
        RecordedCanArchive archive;
        try { archive = JsonSerializer.Deserialize<RecordedCanArchive>(bytes)
            ?? throw new InvalidDataException("CAN_REPLAY_ARCHIVE_EMPTY"); }
        catch (JsonException ex) { throw new InvalidDataException("CAN_REPLAY_ARCHIVE_INVALID_JSON", ex); }
        if (archive.SchemaVersion != 1 || archive.RecordId == Guid.Empty ||
            archive.DbcId == Guid.Empty || archive.DbcRevision <= 0 ||
            archive.StartUtc == default || archive.Frames is null ||
            archive.Frames.Length is < 2 or > 200_000 ||
            archive.DbcSha256 is null || archive.DbcSha256.Length != 64 ||
            !archive.DbcSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("CAN_REPLAY_ARCHIVE_INVALID_SCHEMA");
        long previous = -1;
        foreach (var frame in archive.Frames)
        {
            if (frame is null || frame.OffsetMicroseconds <= previous || frame.OffsetMicroseconds < 0 ||
                frame.OffsetMicroseconds > 3_600_000_000L || frame.TraceEvidence == Guid.Empty ||
                frame.LostFrames < 0 || !Enum.IsDefined(frame.Role) ||
                frame.PayloadHex is null || frame.PayloadHex.Length > 16 ||
                frame.PayloadHex.Length % 2 != 0)
                throw new InvalidDataException("CAN_REPLAY_FRAME_INVALID_OR_UNORDERED");
            byte[] bytesFrame;
            try { bytesFrame = Convert.FromHexString(frame.PayloadHex); }
            catch (FormatException ex) { throw new InvalidDataException("CAN_REPLAY_BAD_HEX", ex); }
            _ = CanFrame.Create(frame.Role, frame.Extended, frame.FrameId, bytesFrame);
            previous = frame.OffsetMicroseconds;
        }
        _archive = archive;
        _archivePath = Path.GetFullPath(path);
        ArchiveSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Read-only frame evidence for the WAVE diagnostic workbench. It does not publish CAN.</summary>
    public WaveArchiveReport ReadArchiveReport(int maxRows, string? filter = null)
    {
        if (_archive is not { } archive || ArchiveSha256 is null)
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_REQUIRED", "Load a CAN RX archive before inspecting it.");
        _ = ReadVerifiedOriginalArchive(); // refuse previews of an externally replaced source
        if (maxRows is < 1 or > 200_000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        var dbAsset = _workspace.DbcAssetId is { } id
            ? _workspace.Assets.SingleOrDefault(x => x.Id == id) : null;
        var dbcMatches = dbAsset is not null && _workspace.Database is not null &&
            archive.DbcId == dbAsset.Id.Value && archive.DbcRevision == dbAsset.Revision &&
            string.Equals(archive.DbcSha256, dbAsset.Sha256, StringComparison.OrdinalIgnoreCase);
        var rows = new List<WaveRawFrameRow>(Math.Min(maxRows, archive.Frames.Length));
        var totalMatched = 0;
        for (var i = 0; i < archive.Frames.Length; i++)
        {
            var frame = archive.Frames[i];
            var message = dbcMatches
                ? _workspace.Database!.Messages.FirstOrDefault(x => x.Id == frame.FrameId)
                : null;
            string decoded, error;
            if (!dbcMatches) { decoded = "不可解码：当前工程 DBC 与帧档案版本不匹配"; error = "DBC_REVISION_STALE"; }
            else if (message is null) { decoded = "DBC 未定义此帧"; error = "DBC_MESSAGE_MISSING"; }
            else if (frame.LostFrames != 0) { decoded = "有丢帧报告，数据仅供观察"; error = "LOSS_REPORTED"; }
            else
            {
                var values = new List<string>();
                error = string.Empty;
                var payload = Convert.FromHexString(frame.PayloadHex);
                foreach (var signal in message.Signals)
                {
                    if (signal.Multiplexer is not null)
                    {
                        error = "MULTIPLEX_NOT_RESOLVED";
                        continue;
                    }
                    try
                    {
                        var value = DbcSignalCodec.DecodePhysical(signal, payload);
                        values.Add(signal.Name + "=" + value.ToString("G9", CultureInfo.InvariantCulture) + " " + signal.Unit);
                    }
                    catch (Exception ex) when (ex is AutoTest.Projects.Project.ProjectDataException or ArgumentException)
                    {
                        error = "DBC_DECODE_ERROR";
                    }
                }
                decoded = values.Count == 0 ? "无可直接解码的非复用信号" : string.Join("; ", values);
            }
            if (!string.IsNullOrWhiteSpace(filter) && !(
                frame.FrameId.ToString("X", CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                frame.PayloadHex.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                frame.TraceEvidence.ToString("D").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                decoded.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                error.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
            totalMatched++;
            if (rows.Count < maxRows)
                rows.Add(new WaveRawFrameRow(i + 1,
                    archive.StartUtc.AddTicks(checked(frame.OffsetMicroseconds * 10)),
                    frame.OffsetMicroseconds, frame.Role.ToString(),
                    "0x" + frame.FrameId.ToString("X", CultureInfo.InvariantCulture),
                    frame.Extended, frame.PayloadHex, frame.PayloadHex.Length / 2,
                    frame.LostFrames, frame.TraceEvidence.ToString("D"), decoded, error));
        }
        return new WaveArchiveReport(archive.RecordId, ArchiveSha256, archive.DbcSha256,
            archive.DbcRevision, archive.StartUtc, archive.Frames.Length,
            archive.Frames.Max(x => x.LostFrames), rows.Count, totalMatched > rows.Count,
            "ReplayDerived: archive offsets are microseconds; original device clock quality is not asserted",
            dbcMatches ? "OK" : "DBC_REVISION_STALE", rows);
    }

    public byte[] ReadVerifiedOriginalArchive()
    {
        if (_archivePath is null || ArchiveSha256 is null || _archive is null)
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_REQUIRED", "No original CAN archive loaded.");
        byte[] bytes;
        try { bytes = File.ReadAllBytes(_archivePath); }
        catch (IOException ex) { throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_UNAVAILABLE",
            "Original CAN RX archive is no longer readable: " + ex.Message); }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_CHANGED", "Original CAN RX archive changed after load.");
        return bytes;
    }

    public async Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int maxFrames,
        CancellationToken ct)
    {
        if (_archive is not { } archive)
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_REQUIRED", "Load a saved CAN RX frame archive first.");
        _ = ReadVerifiedOriginalArchive(); // immutable source proof before any CAN publication
        if (!_replayModeOnly())
            throw new WaveCaptureException("CAN_REPLAY_MODE_BLOCKED", "Physical channel is not isolated from Replay.");
        WaveTimeQuality.ValidateSignal(signal);
        if (archive.DbcId != signal.DbcAsset.Id.Value ||
            archive.DbcRevision != signal.DbcAsset.Revision.Value ||
            !string.Equals(archive.DbcSha256, signal.DbcAsset.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new WaveCaptureException("DBC_REVISION_STALE", "CAN trace and active DBC fingerprint differ.");
        if (maxFrames is < 2 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        var matching = archive.Frames.Where(x => x.Role == signal.Role && x.FrameId == signal.FrameId)
            .ToArray();
        if (matching.Length > maxFrames)
            throw new WaveCaptureException("REPLAY_FRAME_LIMIT", "Saved frame count exceeds the explicit capture budget; nothing was truncated silently.");
        if (matching.Length < 2) throw new WaveCaptureException("REPLAY_INSUFFICIENT_FRAMES", "Saved CAN trace has too few frames for the selected signal.");
        var source = new HubDbcSignalSource(_sharedHub, _workspace);
        var origin = TimePoint.FromReplay(archive.StartUtc, 0);
        source.SetReplayOrigin(origin);
        var capture = new WaveCaptureService(source, _evidence, _references);
        var duration = TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(1).Ticks,
            checked(matching[^1].OffsetMicroseconds * 10 + TimeSpan.TicksPerSecond)));
        if (_sharedHub is not CanFrameHub shared)
            throw new WaveCaptureException("REPLAY_SUBSCRIPTION_BARRIER_MISSING",
                "Shared hub cannot prove that the replay subscriber is ready.");
        var startingSubscribers = shared.SubscriberCount;
        var id = await capture.StartAsync(signal, new WaveCaptureConfig(duration, matching.Length + 8), ct)
            .ConfigureAwait(false);
        try
        {
            // Ensure the subscription is active before the first stored frame is published.
            // This only checks a shared-hub count; INTEGRATION guarantees replay-mode exclusivity.
            if (_sharedHub is CanFrameHub concrete)
            {
                for (var n = 0; concrete.SubscriberCount <= startingSubscribers && n < 2000; n++)
                    await Task.Delay(1, ct).ConfigureAwait(false);
                if (concrete.SubscriberCount <= startingSubscribers)
                    throw new WaveCaptureException("CAN_HUB_SUBSCRIPTION_TIMEOUT", "Wave decoder did not subscribe.");
            }
            foreach (var frame in matching)
            {
                ct.ThrowIfCancellationRequested();
                if (!_replayModeOnly())
                    throw new WaveCaptureException("CAN_REPLAY_MODE_BLOCKED", "Replay isolation was lost.");
                var payload = Convert.FromHexString(frame.PayloadHex);
                var observation = new CanObservation(
                    CanFrame.Create(frame.Role, frame.Extended, frame.FrameId, payload),
                    TimePoint.FromReplay(archive.StartUtc.AddTicks(frame.OffsetMicroseconds * 10),
                        frame.OffsetMicroseconds + 1),
                    EvidenceQuality.Derived, 0, frame.LostFrames,
                    new EvidenceRef(frame.TraceEvidence));
                _publishReplay(observation);
            }
            for (var n = 0; capture.Observe(id).ObservedEvents < matching.Length && n < 2000; n++)
                await Task.Delay(1, ct).ConfigureAwait(false);
            _ = ReadVerifiedOriginalArchive(); // fail if original changed during replay
            var evidence = await capture.StopAsync(id, ct).ConfigureAwait(false);
            var document = capture.ReadEvidence(evidence.Evidence);
            return new WaveWorkbenchCapture(evidence, document.Actual,
                "Replay / recorded CAN RX / SHA " + ArchiveSha256![..8], WaveInputKind.Replay);
        }
        catch
        {
            try { await capture.StopAsync(id, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { /* keep original failure reason */ }
            throw;
        }
    }
}
