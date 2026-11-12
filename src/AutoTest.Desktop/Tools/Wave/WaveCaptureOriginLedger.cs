using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Core;

namespace AutoTest.Desktop.Tools.Wave;

public sealed record WaveCaptureOriginFact(Guid CaptureEvidence, WaveInputKind InputKind,
    string SourceLabel, TimeSource TimeSource, TimeQuality TimeQuality,
    string DbcSha256, Guid[] SourceFrameEvidence, DateTimeOffset RecordedAtUtc,
    string? ArchiveSha256, string ExternalOperationId);

/// <summary>
/// Append-only provenance for WaveEvidenceDocument captures. Generated-frame
/// timestamps can be ReplayDerived, but generated data must be classified Mock.
/// ExternalOperationId is deliberately UNKNOWN until RECORD/INTEGRATION supplies the global ID.
/// </summary>
public sealed class WaveCaptureOriginLedger
{
    private readonly string _folder;
    public WaveCaptureOriginLedger(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = Path.GetFullPath(folder);
    }

    public void Write(WaveWorkbenchCapture capture, string? archiveSha256 = null,
        string externalOperationId = "UNKNOWN")
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Kind == WaveInputKind.Unknown || capture.Evidence.Evidence.Value == Guid.Empty)
            throw new ArgumentException("Explicit source provenance is required.");
        if ((capture.Kind == WaveInputKind.Physical && capture.Series.Origin.Source is not
             (TimeSource.HostArrival or TimeSource.DeviceSample)) ||
            (capture.Kind == WaveInputKind.Replay && capture.Series.Origin.Source != TimeSource.Replay))
            throw new InvalidDataException("Data source label and origin clock conflict.");
        var fact = new WaveCaptureOriginFact(capture.Evidence.Evidence.Value, capture.Kind,
            capture.SourceLabel, capture.Series.Origin.Source, capture.Series.Origin.Quality,
            capture.Series.SourceRevision.Sha256,
            capture.Series.InputEvidence.Select(x => x.Value).ToArray(), DateTimeOffset.UtcNow,
            archiveSha256, externalOperationId);
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, fact.CaptureEvidence.ToString("N") + ".origin.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fact);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path);
            File.WriteAllText(path + ".sha256", digest + "\n");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
