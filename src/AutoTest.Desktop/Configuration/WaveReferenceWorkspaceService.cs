using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Configuration;

/// <summary>
/// Operator-controlled promotion of an already persisted physical/replay capture.
/// Never auto-approve the latest trace or treat arbitrary imported files as references.
/// </summary>
public static class WaveReferenceWorkspaceService
{
    public sealed record CaptureChoice(Guid Evidence, string Label);

    private static string Root(ProjectWorkspaceService project) =>
        Path.Combine(project.Current?.RootDirectory ??
            throw new InvalidOperationException("Open the engineering project first."),
            "wave-records");

    private static bool Fits(WaveSettingsDocument config, WaveSeries series)
    {
        var s = series.Signal;
        return config.DbcAssetId.HasValue &&
            config.DbcAssetId == s.DbcAsset.Id.Value &&
            config.DbcAssetRevision == s.DbcAsset.Revision.Value &&
            string.Equals(config.DbcAssetFingerprint, s.DbcAsset.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase) &&
            config.Bus == s.Role && config.CanId == s.FrameId &&
            config.SignalName == s.SignalName && config.Unit == s.Unit;
    }

    public static IReadOnlyList<CaptureChoice> FindCandidates(ProjectWorkspaceService project,
        WaveSettingsDocument config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (!config.DbcAssetId.HasValue || string.IsNullOrWhiteSpace(config.SignalName))
            throw new InvalidOperationException("Select a DBC signal and apply the waveform settings first.");
        var folder = Path.Combine(Root(project), "evidence");
        if (!Directory.Exists(folder)) return [];
        var store = new WaveEvidenceStore(folder);
        var results = new List<CaptureChoice>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.wave.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            var first = Path.GetFileName(file).Split('.')[0];
            if (!Guid.TryParseExact(first, "N", out var id)) continue;
            try
            {
                var evidence = store.Read(new EvidenceRef(id));
                if (evidence.Kind != "Capture" || !Fits(config, evidence.Actual) ||
                    !WaveTimeQuality.Describe(evidence.Actual).CanJudge)
                    continue;
                results.Add(new CaptureChoice(id, evidence.CreatedUtc.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss") + "  ·  " +
                    evidence.Actual.Samples.Length + " 样本  ·  " +
                    evidence.Actual.Origin.Source + "  ·  " + id.ToString("N")[..8]));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
            {
                // Corrupt/partial captures are not candidates for promotion.
            }
        }
        return results;
    }

    public static IReadOnlyList<string> ReferenceNames(ProjectWorkspaceService project,
        WaveSettingsDocument config)
    {
        var root = Root(project);
        var catalog = new WaveReferenceCatalog(Path.Combine(root, "references"));
        var matches = catalog.List().Where(x =>
            config.DbcAssetId.HasValue && x.Signal.DbcAsset.Id.Value == config.DbcAssetId &&
            x.Signal.DbcAsset.Revision.Value == config.DbcAssetRevision &&
            string.Equals(x.Signal.DbcAsset.Fingerprint.Sha256,
                config.DbcAssetFingerprint, StringComparison.OrdinalIgnoreCase) &&
            x.Signal.Role == config.Bus && x.Signal.FrameId == config.CanId &&
            x.Signal.SignalName == config.SignalName && x.Signal.Unit == config.Unit)
            .Select(x => x.Name);
        return new[] { "" }.Concat(matches).Concat(
            string.IsNullOrWhiteSpace(config.ReferenceName)
                ? Array.Empty<string>() : new[] { config.ReferenceName })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Promote(ProjectWorkspaceService project, WaveSettingsDocument config,
        Guid evidenceId, string referenceName)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (evidenceId == Guid.Empty || string.IsNullOrWhiteSpace(referenceName) ||
            referenceName.Trim().Length > 128)
            throw new ArgumentException("Choose a completed waveform and a reference name up to 128 characters.");
        var root = Root(project);
        var store = new WaveEvidenceStore(Path.Combine(root, "evidence"));
        var evidence = store.Read(new EvidenceRef(evidenceId));
        if (evidence.Kind != "Capture" || !Fits(config, evidence.Actual))
            throw new InvalidDataException("Captured data is not from the exact configured DBC signal/version.");
        if (!WaveTimeQuality.Describe(evidence.Actual).CanJudge)
            throw new InvalidDataException("Waveform quality does not permit reference creation.");
        new WaveReferenceCatalog(Path.Combine(root, "references"))
            .Save(referenceName.Trim(), evidence.Actual);
    }
}
