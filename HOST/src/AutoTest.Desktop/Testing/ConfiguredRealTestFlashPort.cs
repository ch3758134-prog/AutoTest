using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Programming;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>Adapter for the existing CanToolFlashJob/CanToolFlashWorkflow on
/// the existing station session. Never opens an independent USB link.</summary>
public sealed class ConfiguredRealTestFlashPort : ITestFlashPort
{
    private readonly HardwareConnectionService _hardware;
    private readonly ConfiguredStationActionPort _actions;
    private readonly RealRunEvidenceRecorder _evidence;
    private readonly TestDocument _document;
    private readonly IReadOnlyList<TestTargetMapping> _mappings;
    private readonly IReadOnlyDictionary<StepId, CanToolFlashJob> _jobs;
    private readonly string _root, _planFingerprint, _testConsent, _flashConsent;
    private readonly long _stationEpoch;
    private readonly bool _hadWrites;
    private int _flashAttempted;

    public ConfiguredRealTestFlashPort(HardwareConnectionService hardware,
        ConfiguredStationActionPort actions, TestDocument document,
        IReadOnlyList<TestTargetMapping> mappings,
        IReadOnlyDictionary<StepId, CanToolFlashJob> frozenJobs,
        string projectRoot, string planFingerprint, string testConsent,
        string flashConsent, RealRunEvidenceRecorder evidence)
    {
        _hardware = hardware;
        _actions = actions;
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _document = document;
        _mappings = mappings;
        _jobs = frozenJobs;
        _root = projectRoot;
        _planFingerprint = planFingerprint;
        _testConsent = testConsent;
        _flashConsent = flashConsent;
        _stationEpoch = hardware.CurrentStationEpoch;
        _hadWrites = TestRunStepScope.RequiresPhysicalWrite(document);
    }

    private static string ResolveFile(string projectRoot, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new InvalidDataException("FLASH 缺少必须的 XML、固件或安全文件。");
        return Path.GetFullPath(Path.IsPathRooted(candidate)
            ? candidate : Path.Combine(projectRoot, candidate));
    }

    public static IReadOnlyDictionary<StepId, CanToolFlashJob> FreezeJobs(
        TestDocument document, string projectRoot)
    {
        var jobs = new Dictionary<StepId, CanToolFlashJob>();
        // R1's physical FLASH authorization accepts exactly one immutable job
        // per TEST run. Reject multiple enabled FLASH steps during preflight,
        // rather than failing later with an opaque Single() exception or
        // accidentally reusing one destructive-write consent for another job.
        var flashSteps = TestRunStepScope.EnabledSteps(document)
            .Where(s => s.Type == "Flash").ToArray();
        if (flashSteps.Length > 1)
            throw new NotSupportedException(
                "R1 首版一次真实测试只允许一个启用的 FLASH 步骤；请拆分成独立用例，分别预检并授权烧录。");
        foreach (var step in flashSteps)
        {
            string Get(string key) => step.Properties.GetValueOrDefault(key, "").Trim();
            var xml = ResolveFile(projectRoot, Get("programmingProfile"));
            var firmware = ResolveFile(projectRoot, Get("firmwarePath"));
            var security = ResolveFile(projectRoot, Get("securityLibraryPath"));
            var target = Get("targetLabel");
            if (string.IsNullOrWhiteSpace(target))
                throw new InvalidDataException("FLASH 必须填写现场核对的 ECU 型号/身份。");
            var config = CanToolXmlProfileReader.Load(xml);
            if (Get("settingsPath") is { Length: > 0 } settings)
                config.LoadRelatedJson(ResolveFile(projectRoot, settings));
            var job = CanToolFlashJob.Freeze(config, firmware,
                Get("binBaseAddress") is { Length: > 0 } binBase ? binBase : null,
                security, target);
            jobs.Add(step.Id, job);
        }
        return jobs;
    }

    public async Task<TestFlashResult> ExecuteAsync(StepNode step, CancellationToken ct)
    {
        if (!_jobs.TryGetValue(step.Id, out var job))
            throw new InvalidOperationException("未冻结 FLASH 资源；拒绝执行。");
        if (!_hardware.IsConnected || _stationEpoch <= 0 ||
            _hardware.CurrentStationEpoch != _stationEpoch)
            throw new IOException("主控会话切换或断开，禁止烧录。");
        if (!string.Equals(_flashConsent, job.Consent, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("必须手动输入完整的冻结 FLASH 授权指纹。");
        // Prevent edits to on-disk XML/JSON/HEX/S19/DLL after preflight.
        var current = FreezeJobs(_document, _root);
        if (!current.TryGetValue(step.Id, out var latest) ||
            !string.Equals(latest.Fingerprint, job.Fingerprint, StringComparison.Ordinal))
            throw new IOException("FLASH 资源预检后改变，拒绝擦除。");

        // A single frozen FLASH step may still be reached more than once
        // through Loop/sequence/call execution. One explicit consent authorizes
        // exactly one physical attempt, regardless of success, failure or stop.
        // Block a second destructive call before any cleanup or ECU action.
        if (Interlocked.Exchange(ref _flashAttempted, 1) != 0)
            throw new InvalidOperationException(
                "当前 TEST 运行已经尝试过一次 FLASH；禁止循环或重复烧录。请重新预检并单独授权下一次。");
        // Physical fence cannot hold TEST and FLASH grants simultaneously.
        // First stop all periodic CAN and release held KEY masks.
        await _actions.CleanupAsync(ct);
        var flashEvidence = _evidence.BeginFlashCapture();
        try
        {
            var result = await _hardware.ProgramTestFlashAsync(job, _flashConsent, ct);
            bool protocolComplete = result.ResetAcknowledged &&
                result.TransferAcknowledged == job.PlannedBytes &&
                result.VerifiedBytes == job.PlannedBytes;
            // Only a proven APP diagnostic session may reauthorize TEST writes.
            if (protocolComplete && result.AppSessionObserved && _hadWrites &&
                !ct.IsCancellationRequested &&
                _hardware.IsConnected && _hardware.CurrentStationEpoch == _stationEpoch)
                await _actions.AuthorizeAsync(_planFingerprint, _testConsent,
                    _mappings, _document, ct);
            return new TestFlashResult(protocolComplete, result.AppSessionObserved,
                result.TransferAcknowledged, result.VerifiedBytes, result.Outcome,
                "真实 CAN FLASH；APP 诊断会话并不证明固件版本或生产放行。" +
                " 固件SHA256=" + job.FirmwareSha256 + " 任务SHA256=" + job.Fingerprint,
                flashEvidence.ToString());
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new TestFlashExecutionException("FLASH 执行失败，ECU 状态可能未知。",
                flashEvidence.ToString(), error);
        }
        finally { _evidence.EndFlashCapture(flashEvidence); }
    }
}
