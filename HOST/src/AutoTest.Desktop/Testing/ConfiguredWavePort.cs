using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Specialists.Wave;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Wave recording is a data acquisition operation, not a test verdict.
/// Both Read and Wave use the same verified live DBC signal source.
/// A comparison result is returned as data for an explicit TEST Expect step.
/// </summary>
public sealed class ConfiguredWavePort : ITestWavePort
{
    private readonly ProjectWorkspaceService _projects;
    private readonly LiveDbcSignalService _signals;
    private readonly WaveCaptureService _capture;
    private readonly WaveReferenceCatalog _catalog;
    private readonly Dictionary<string, WaveCaptureId> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WaveEvidenceRef> _completed = new(StringComparer.OrdinalIgnoreCase);
    // A named wave reference is resolved ONCE before TEST can touch the ECU.
    // It cannot silently drift to a different reference between Start and Compare.
    private readonly Dictionary<string, WaveReferenceEntry> _frozenReferences =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task FreezeProfileReferencesAsync(
        IReadOnlyList<TestTargetMapping> required, CancellationToken ct)
    {
        var project = _projects.Current ?? throw new InvalidOperationException("工程尚未打开。");
        foreach (var mapping in required.Where(x => x.Kind == TestTargetKind.WaveProfile))
        {
            ct.ThrowIfCancellationRequested();
            mapping.Validate();
            var asset = project.Definition.Assets.SingleOrDefault(x => x.Id == mapping.Asset)
                ?? throw new InvalidDataException("波形映射资源缺失：" + mapping.Name);
            if (asset.Kind != ProjectWorkspaceService.WaveSettingsKind ||
                asset.Revision != mapping.AssetRevision ||
                !string.Equals(asset.Sha256, mapping.AssetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("波形配置引用已失效：" + mapping.Name);
            var path = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (!asset.VerifyHash(bytes))
                throw new InvalidDataException("波形配置文件与保存的指纹不一致：" + mapping.Name);
            var cfg = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(asset.Kind, bytes);
            if (!cfg.IsComparisonReviewCurrent)
                throw new InvalidDataException("波形判据尚未批准新版比较算法，或已改变 DBC、参考、阈值：" +
                    mapping.Name + "。请在 WAVE 配置中复核后保存新修订，再更新 TEST 资源引用。");
            if (string.IsNullOrWhiteSpace(cfg.ReferenceName) || !cfg.DbcAssetId.HasValue)
                throw new InvalidDataException("波形判定缺少已确认的参考波形或 DBC：" + mapping.Name);
            var matches = _catalog.List().Where(x =>
                x.Name == cfg.ReferenceName &&
                x.Signal.DbcAsset.Id.Value == cfg.DbcAssetId.Value &&
                x.Signal.DbcAsset.Revision.Value == cfg.DbcAssetRevision &&
                string.Equals(x.Signal.DbcAsset.Fingerprint.Sha256,
                    cfg.DbcAssetFingerprint, StringComparison.OrdinalIgnoreCase) &&
                x.Signal.Role == cfg.Bus && x.Signal.FrameId == cfg.CanId &&
                x.Signal.SignalName == cfg.SignalName && x.Signal.Unit == cfg.Unit).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("标准波形没有唯一有效的资源版本：" + mapping.Name);
            _ = _catalog.Load(matches[0].Reference);
            _frozenReferences.Add(mapping.Name, matches[0]);
        }
    }

    public ConfiguredWavePort(ProjectWorkspaceService projects, LiveDbcSignalService signals)
    {
        _projects = projects;
        _signals = signals;
        var context = _projects.Current ?? throw new InvalidOperationException("An open project is required for wave evidence.");
        var root = Path.Combine(context.RootDirectory, "wave-records");
        _catalog = new WaveReferenceCatalog(Path.Combine(root, "references"));
        _capture = new WaveCaptureService(_signals, new WaveEvidenceStore(Path.Combine(root, "evidence")),
            new WaveReferenceRepository(Path.Combine(root, "references", "assets")));
    }

    public async Task StartAsync(TestTargetMapping target, string captureKey, int durationMs,
        CancellationToken ct)
    {
        if (target.Kind != TestTargetKind.Signal)
            throw new ArgumentException("Wave capture requires a mapped DBC signal.");
        if (string.IsNullOrWhiteSpace(captureKey))
            throw new ArgumentException("Wave capture identifier required.");
        if (_active.ContainsKey(captureKey) || _completed.ContainsKey(captureKey))
            throw new InvalidOperationException("Wave capture key already used in this run.");
        if (durationMs <= 0 || durationMs > 3600000)
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        var signal = _signals.ResolveSignal(target);
        var id = await _capture.StartAsync(signal,
            new WaveCaptureConfig(TimeSpan.FromMilliseconds(durationMs), 100000), ct);
        _active.Add(captureKey, id);
    }

    public async Task<TestWaveResult> StopAsync(string captureKey, CancellationToken ct)
    {
        if (!_active.Remove(captureKey, out var id))
            return new(false, "CAPTURE_MISSING", null, null, "Wave capture has not started.");
        try
        {
            var evidence = await _capture.StopAsync(id, ct);
            _completed[captureKey] = evidence;
            var recorded = _capture.ReadEvidence(evidence.Evidence);
            return new(true, "CAPTURED", recorded.Actual.Samples.Length.ToString(),
                evidence.Evidence.ToString(), "Actual DBC-decoded samples recorded; no judgement yet.");
        }
        catch (WaveCaptureException ex)
        {
            return new(false, ex.Code, null, null, ex.Message);
        }
    }

    public Task<TestWaveResult> AnalyzeAsync(string captureKey, string referenceKey,
        double tolerance, double minimumCoverage, CancellationToken ct)
    {
        // Legacy ad-hoc TEST WaveCompare parameters cannot establish approval
        // of an immutable algorithm/reference/threshold contract. The WAVE
        // workbench still offers independent manual comparisons; an actual
        // automatic TEST must select a reviewed WaveProfile mapping.
        return Task.FromResult(new TestWaveResult(false, "WAVE_PROFILE_REQUIRED",
            null, null,
            "正式 TEST 的波形判定必须指定已保存并复核的 WAVE 配置资源；" +
            "不能再用步骤临时容差与自由参考名绕过算法审查。"));
    }

    private async Task<TestWaveResult> LegacyAdHocCompareNotForExecutionAsync(
        string captureKey, string referenceKey,
        double tolerance, double minimumCoverage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!double.IsFinite(tolerance) || tolerance < 0 ||
            !double.IsFinite(minimumCoverage) ||
            minimumCoverage < 0 || minimumCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Wave criteria invalid.");
        if (!_completed.TryGetValue(captureKey, out var evidence))
            return new(false, "CAPTURE_NOT_AVAILABLE", null, null, "Complete capture before analysis.");
        var actual = _capture.ReadEvidence(evidence.Evidence).Actual;
        var matches = _catalog.List().Where(e => e.Name == referenceKey &&
            e.Signal == actual.Signal).ToArray();
        if (matches.Length != 1)
            return new(false, "REFERENCE_NOT_UNIQUE", null, evidence.Evidence.ToString(),
                "Choose one existing named reference for this exact DBC signal revision.");
        var evaluated = await _capture.CompareAsync(evidence, matches[0].Reference,
            new CompareProfile(tolerance, minimumCoverage));
        var available = evaluated.Verdict is Verdict.Pass or Verdict.Fail or Verdict.Inconclusive;
        return new(available, evaluated.Verdict.ToString(), evaluated.Verdict.ToString(),
            string.Join(",", evaluated.Evidence.Select(x => x.ToString())), evaluated.Reason);
    }

    /// <summary>
    /// Evaluate using one pinned wave-settings resource from the active project.
    /// This produces a measurement result, never an implicit TEST verdict.
    /// </summary>
    public async Task<TestWaveResult> AnalyzeWithProfileAsync(string captureKey,
        TestTargetMapping profile, string referenceKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (profile.Kind != TestTargetKind.WaveProfile)
            throw new ArgumentException("Selected mapping is not a wave analysis profile.");
        profile.Validate();
        if (!_completed.TryGetValue(captureKey, out var evidence))
            return new(false, "CAPTURE_NOT_AVAILABLE", null, null,
                "Complete the signal recording before applying its analysis profile.");

        var project = _projects.Current ?? throw new InvalidOperationException("Project is closed.");
        var asset = project.Definition.Assets.SingleOrDefault(a => a.Id == profile.Asset)
            ?? throw new InvalidDataException("The mapped waveform configuration asset is missing.");
        if (asset.Kind != ProjectWorkspaceService.WaveSettingsKind ||
            asset.Revision != profile.AssetRevision ||
            !string.Equals(asset.Sha256, profile.AssetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Wave analysis configuration changed after TEST mapping.");

        var file = ProjectPathPolicy.Normalize(project.RootDirectory, asset.RelativePath);
        var bytes = await File.ReadAllBytesAsync(file, ct);
        if (!asset.VerifyHash(bytes))
            throw new InvalidDataException("Wave analysis configuration content differs from project revision.");
        var settings = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(asset.Kind, bytes);
        if (!settings.IsComparisonReviewCurrent)
            return new(false, "WAVE_ALGORITHM_REVIEW_REQUIRED",
                null, evidence.Evidence.ToString(),
                "波形判据的算法、DBC、参考或容差已发生变化，须重新审核保存配置，" +
                "然后更新用例资源修订才允许真实自动判定。");
        var actual = _capture.ReadEvidence(evidence.Evidence).Actual;
        var signal = actual.Signal;
        if (!settings.DbcAssetId.HasValue ||
            settings.DbcAssetId.Value != signal.DbcAsset.Id.Value ||
            settings.DbcAssetRevision != signal.DbcAsset.Revision.Value ||
            !string.Equals(settings.DbcAssetFingerprint,
                signal.DbcAsset.Fingerprint.Sha256, StringComparison.OrdinalIgnoreCase) ||
            settings.Bus != signal.Role || settings.CanId != signal.FrameId ||
            settings.SignalName != signal.SignalName || settings.Unit != signal.Unit)
            return new(false, "PROFILE_SIGNAL_MISMATCH", null, evidence.Evidence.ToString(),
                "Wave profile must refer to the exact captured DBC signal, revision, bus and unit.");
        if (_projects.Current?.ManifestPath != project.ManifestPath)
            throw new InvalidOperationException("Project changed during waveform analysis.");
        // The configured reference is frozen before run. A per-step name is
        // permitted only when it is the SAME approved reference.
        if (!_frozenReferences.TryGetValue(profile.Name, out var pinned) ||
            pinned.Name != settings.ReferenceName ||
            (referenceKey.Length > 0 && referenceKey != pinned.Name) ||
            pinned.Signal != signal)
            return new(false, "REFERENCE_PIN_MISMATCH", null, evidence.Evidence.ToString(),
                "运行中参考波形或信号与冻结的批准版本不一致。");

        var evaluated = await _capture.CompareWithConfigForRunAsync(
            evidence, pinned.Reference, settings.Comparison);
        var usable = evaluated.Verdict is Verdict.Pass or Verdict.Fail or Verdict.Inconclusive;
        return new(usable, evaluated.Verdict.ToString(), evaluated.Verdict.ToString(),
            string.Join(",", evaluated.Evidence.Select(x => x.ToString())),
            "Profile " + asset.Name + ": " + evaluated.Reason);
    }

    /// <summary>Explicit user action only; never silently bless a failed DUT trace as a reference.</summary>
    public WaveReference PromoteCapturedReference(string captureKey, string name)
    {
        if (!_completed.TryGetValue(captureKey, out var evidence))
            throw new InvalidOperationException("No completed capture.");
        var series = _capture.ReadEvidence(evidence.Evidence).Actual;
        return _catalog.Save(name, series);
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        foreach (var key in _active.Keys.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            await StopAsync(key, ct);
        }
    }
}
