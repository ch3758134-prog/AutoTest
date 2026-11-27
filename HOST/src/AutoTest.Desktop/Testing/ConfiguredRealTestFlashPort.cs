using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.FlashLab.Programming;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// R1 boundary: FLASH is the ECU production workflow's mandatory pre-test
/// operation, never a TEST step. Retains the existing ITestFlashPort contract
/// for old TEST document decoding, but forbids the obsolete XML/HEX/DLL
/// per-step programming path at authoring preflight and runtime.
/// Standalone programming uses NativeFlashJobBuilder + the real FlashLab.
/// </summary>
public sealed class ConfiguredRealTestFlashPort : ITestFlashPort
{
    // Retain the constructor shape while retiring the second physical
    // programming implementation; no caller may implicitly acquire USB here.
    public ConfiguredRealTestFlashPort(HardwareConnectionService hardware,
        ConfiguredStationActionPort actions, TestDocument document,
        IReadOnlyList<TestTargetMapping> mappings,
        IReadOnlyDictionary<StepId, CanToolFlashJob> frozenJobs,
        string projectRoot, string planFingerprint, string testConsent,
        string flashConsent, RealRunEvidenceRecorder evidence)
    {
    }

    public static IReadOnlyDictionary<StepId, CanToolFlashJob> FreezeJobs(
        TestDocument document, string projectRoot)
    {
        if (TestRunStepScope.EnabledSteps(document)
            .Any(step => step.Type == "Flash"))
            throw new NotSupportedException(
                "FLASH 是 ECU 正式检测的前置阶段，不属于 TEST 测试用例步骤。" +
                "请在工程 FLASH 工具中设置 HEX 和烧录参数。" +
                "旧用例中的 FLASH 步骤必须移除，不再解析 XML 或选择安全 DLL。");
        return new Dictionary<StepId, CanToolFlashJob>();
    }

    public Task<TestFlashResult> ExecuteAsync(StepNode step, CancellationToken ct) =>
        throw new NotSupportedException(
            "TEST 步骤禁止直接烧录 ECU。正式检测由统一流程先烧录、再运行完整 TEST。");
}
