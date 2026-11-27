namespace AutoTest.Desktop.Shell;

/// <summary>
/// Conservative, display-neutral assessment of the ONE deployed production
/// contract. It cannot grant an Inspection RunId, physical actuation, PASS,
/// clamp position or Flash authority. Only confirmed inputs from real
/// adapters may clear the final two gates.
/// </summary>
public sealed record ProductionAdmissionFacts(
    bool DeployedProjectLoaded,
    bool ResourcesExecutable,
    bool ControllerConnected,
    bool EcuCanRateConfirmed,
    bool ScannerConnected,
    bool AcceptedScannerObservation,
    bool FrozenProjectMatches,
    bool SameControllerSession,
    bool SameScannerPort,
    bool SameEcuCanRate,
    bool TrustedPhysicalStartConnected,
    bool TrustedFixtureAndInterlockReady,
    bool VerifiedInspectionWriterConnected);

public sealed record ProductionAdmissionResult(
    string Code, string Stage, string Headline, string Guidance,
    int SatisfiedChecks, int TotalChecks, bool CanBeginFormalInspection);

public static class ProductionAdmissionReview
{
    public const int GateCount = 13;

    public static ProductionAdmissionResult Evaluate(ProductionAdmissionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var checks = new (bool Ready, string Code, string Stage, string Headline,
            string Guidance)[]
        {
            (facts.DeployedProjectLoaded, "PRODUCTION_PROJECT_NOT_DEPLOYED", "检测工程",
                "尚未识别检测工程", "请扫描 ECU 编号，系统将从已安装的工程库中自动选择。"),
            (facts.ResourcesExecutable, "PRODUCTION_ASSETS_NOT_READY", "检测工程",
                "测试或烧录配置需要检查", "请在工程工作区修复并导出完整工程，再导入生产工程库。"),
            (facts.ControllerConnected, "PRODUCTION_USB_DISCONNECTED", "设备连接",
                "主控未连接", "请连接主控设备，确认连接状态。"),
            (facts.EcuCanRateConfirmed, "PRODUCTION_CAN_RATE_NOT_CONFIRMED", "设备连接",
                "CAN 波特率未确认", "请在主窗口应用 ECU CAN 波特率并确认设置结果。"),
            (facts.ScannerConnected, "PRODUCTION_SCANNER_DISCONNECTED", "设备连接",
                "扫码枪未连接", "请连接本工位的扫码枪。"),
            (facts.AcceptedScannerObservation, "PRODUCTION_IDENTITY_NOT_ACCEPTED", "等待扫码",
                "请扫描 ECU 编号", "请扫描本台 ECU 的完整编号。"),
            (facts.FrozenProjectMatches, "PRODUCTION_PROJECT_SNAPSHOT_MISMATCH", "检测工程",
                "检测工程已更新", "工程内容已变化，请重新扫码匹配当前已安装的工程版本。"),
            (facts.SameControllerSession, "PRODUCTION_CONTROLLER_SESSION_STALE", "设备连接",
                "主控连接已变化", "请确认主控已连接，然后重新扫码。"),
            (facts.SameScannerPort, "PRODUCTION_SCANNER_SESSION_STALE", "设备连接",
                "扫码枪连接已变化", "请连接已设置的扫码枪，然后重新扫码。"),
            (facts.SameEcuCanRate, "PRODUCTION_CAN_RATE_MISMATCH", "设备连接",
                "CAN 波特率不匹配", "请核对主控 CAN 波特率与工程设置，然后重新扫码。"),
            (facts.TrustedPhysicalStartConnected, "PRODUCTION_PHYSICAL_START_NOT_CONNECTED", "启动设备",
                "启动按钮未接入", "尚未连接并验证工位启动按钮，正式检测不能开始。"),
            (facts.TrustedFixtureAndInterlockReady, "PRODUCTION_FIXTURE_PROOF_NOT_CONNECTED", "夹具与安全",
                "夹具状态尚未确认", "正式检测需要可信的机械到位与安全联锁反馈，不能以主控应答代替。"),
            (facts.VerifiedInspectionWriterConnected, "PRODUCTION_INSPECTION_NOT_CONNECTED", "检测记录",
                "检测记录功能未接入", "尚不能可靠保存每台 ECU 的完整检测结果，正式检测暂不可用。")
        };
        var passed = 0;
        foreach (var check in checks)
        {
            if (!check.Ready)
                return new(check.Code, check.Stage, check.Headline,
                    check.Guidance, passed, checks.Length, false);
            passed++;
        }
        return new("PRODUCTION_ADMISSION_CONDITIONS_MET", "等待启动",
            "工位检查通过",
            "请通过工位启动按钮开始检测，并确认烧录和测试结果正常记录。",
            passed, checks.Length, true);
    }
}
