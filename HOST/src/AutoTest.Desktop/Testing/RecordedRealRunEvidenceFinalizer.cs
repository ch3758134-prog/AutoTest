using System.IO;
using AutoTest.Application.TestRecord;
using AutoTest.Core;
using AutoTest.Records.Storage;
using AutoTest.Testing.Runtime;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Testing;

/// <summary>Links genuinely captured physical CAN/DBC evidence into existing
/// immutable RECORD artifacts; never synthesizes a missing sample.</summary>
public sealed class RecordedRealRunEvidenceFinalizer : IRealRunEvidenceFinalizer
{
    private readonly RealRunEvidenceRecorder _raw;
    private readonly RunRecordWriter _writer;
    private readonly ArtifactRepository _artifacts;
    private readonly string _root;
    private RealRunResult? _completed;
    public string? RawEvidencePath { get; private set; }

    public RecordedRealRunEvidenceFinalizer(RealRunEvidenceRecorder raw,
        RunRecordWriter writer, ArtifactRepository artifacts, string root)
    {
        _raw = raw; _writer = writer; _artifacts = artifacts; _root = root;
    }

    public void ObserveCompletedRun(RealRunResult completed)
    {
        if (_completed is not null)
            throw new InvalidOperationException("Evidence finalizer is single-use.");
        _completed = completed;
    }

    public async ValueTask<RunEvidenceCaptureResult> FinalizeEvidenceAsync(RunId run, CancellationToken ct)
    {
        if (_completed is not { } result || result.Run != run)
            return new(false, 0, "RESULT_MISSING_OR_CHANGED");
        RawEvidencePath = await _raw.SaveAsync(_root, result, ct);
        await using (var source = File.OpenRead(RawEvidencePath))
        {
            var archive = await _artifacts.PutAsync(source, "R1_REAL_CAN_DBC_TIMELINE",
                "application/json", "Physical WinUSB run " + run, ct: ct);
            await _writer.AttachArtifactAsync(run, archive, ct);
        }

        var referenced = result.Facts.Where(f => f.Observation is not null)
            .Select(f => f.Observation!.Evidence)
            .Where(e => e.Value != Guid.Empty).Distinct().ToArray();
        int missing = 0;
        foreach (var evidence in referenced)
        {
            var data = _raw.FindRawFrameEvidence(evidence);
            if (data is null) { missing++; continue; }
            await using var stream = new MemoryStream(data, writable: false);
            var linked = await _artifacts.PutAsync(stream, "ECU_CAN_RX_DBC_SIGNAL",
                "application/json", "Real frame/DBC provenance " + run,
                evidence, ct);
            if (linked.ArtifactId != evidence.Value)
                throw new IOException("EvidenceRef mismatch.");
            await _writer.AttachArtifactAsync(run, linked, ct);
        }

        // A FLASH step must point to actual captured USB/UDS/DEVICE bytes.
        // Neither a synthesized GUID nor a Boot reset ACK may imply APP success.
        var flashRefs = new HashSet<EvidenceRef>();
        foreach (var fact in result.Facts.Where(f => f.Type == "Flash"))
            if (Guid.TryParse(fact.Evidence, out var id) && id != Guid.Empty)
                flashRefs.Add(new EvidenceRef(id));
        foreach (var evidence in flashRefs)
        {
            var bytes = _raw.FindFlashEvidence(evidence);
            if (bytes is null) { missing++; continue; }
            await using var stream = new MemoryStream(bytes, writable: false);
            var linked = await _artifacts.PutAsync(stream, "ECU_FLASH_REAL_TRACE",
                "application/json", "Actual FLASH CAN/UDS/device trace " + run,
                evidence, ct);
            if (linked.ArtifactId != evidence.Value)
                throw new IOException("FLASH EvidenceRef mismatch.");
            await _writer.AttachArtifactAsync(run, linked, ct);
        }

        // Wave references are never invented from a TEST property. Each actual
        // capture/comparison ID comes from a completed real runtime step, and
        // each immutable source file must pass WaveEvidenceStore's SHA, signal,
        // captured-source, reference-fingerprint and recomputed-verdict checks.
        var waveIds = new HashSet<EvidenceRef>();
        foreach (var fact in result.Facts.Where(f =>
                     f.Type is "WaveStop" or "WaveAnalyze" or "WaveCompare"))
        {
            foreach (var token in (fact.Evidence ?? "").Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParse(token, out var id) || id == Guid.Empty)
                {
                    missing++;
                    continue;
                }
                waveIds.Add(new EvidenceRef(id));
            }
            if (fact.Type == "WaveCompare" && fact.Status == RealStepStatus.Completed &&
                (fact.Evidence ?? "").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length != 2)
                missing++;
        }
        var waveDirectory = Path.Combine(_root, "wave-records", "evidence");
        var waveStore = new WaveEvidenceStore(waveDirectory);
        foreach (var waveId in waveIds)
        {
            WaveEvidenceDocument verifiedWave;
            byte[] contents;
            try
            {
                verifiedWave = waveStore.Read(waveId);
                var wavePath = Path.Combine(waveDirectory,
                    waveId.Value.ToString("N") + ".wave.json");
                contents = await File.ReadAllBytesAsync(wavePath, ct);
                // Recheck the *exact bytes being archived*. Do not verify one
                // buffer and then persist another after an external file change.
                var sidecar = (await File.ReadAllTextAsync(wavePath + ".sha256", ct)).Trim();
                var actualHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(contents));
                if (!string.Equals(sidecar, actualHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Wave source SHA-256 changed after validation.");
                var archivedWave = WaveEvidenceCodec.Read(contents);
                if (archivedWave.Id != waveId || !WaveEvidenceCodec.Verify(archivedWave))
                    throw new InvalidDataException("Wave archive bytes failed content verification.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                                       UnauthorizedAccessException or ArgumentException or
                                       System.Text.Json.JsonException)
            {
                missing++;
                continue;
            }
            await using var stream = new MemoryStream(contents, writable: false);
            var linked = await _artifacts.PutAsync(stream, "ECU_WAVE_VERIFIED_EVIDENCE",
                WaveEvidenceCodec.MediaType,
                "Verified " + verifiedWave.Kind + " waveform for run " + run,
                waveId, ct);
            if (linked.ArtifactId != waveId.Value)
                throw new IOException("Wave EvidenceRef mismatch.");
            await _writer.AttachArtifactAsync(run, linked, ct);
        }

        // A successful wave assertion must be backed by *both* the captured
        // input and a recomputed comparison referencing that exact input.
        foreach (var fact in result.Facts.Where(f =>
                     f.Type == "WaveCompare" && f.Status == RealStepStatus.Completed))
        {
            var tokens = (fact.Evidence ?? "").Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                if (tokens.Length != 2 ||
                    !Guid.TryParse(tokens[0], out var captureId) ||
                    !Guid.TryParse(tokens[1], out var comparisonId) ||
                    captureId == comparisonId)
                {
                    missing++;
                    continue;
                }
                var capture = waveStore.Read(new EvidenceRef(captureId));
                var comparison = waveStore.Read(new EvidenceRef(comparisonId));
                if (capture.Kind != "Capture" || comparison.Kind != "Comparison" ||
                    comparison.CaptureEvidence?.Value != captureId ||
                    comparison.Result?.Verdict != Verdict.Pass)
                    missing++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                                       UnauthorizedAccessException or ArgumentException or
                                       System.Text.Json.JsonException)
            {
                missing++;
            }
        }

        var health = _raw.CaptureHealth();
        bool complete = health.DroppedEvents == 0 &&
            health.ObservedFrames > 0 && missing == 0;
        string? reason = complete ? null :
            "RAW_INCOMPLETE: lost=" + health.DroppedEvents +
            " frames=" + health.ObservedFrames + " missing=" + missing;
        return new(complete, health.DroppedEvents, reason);
    }
}
