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
    bool VerifiedInspectionWriterConnected);

public sealed record ProductionAdmissionResult(
    string Code, string Stage, string Headline, string Guidance,
    int SatisfiedChecks, int TotalChecks, bool CanBeginFormalInspection);

public static class ProductionAdmissionReview
{
    public const int GateCount = 12;

    public static ProductionAdmissionResult Evaluate(ProductionAdmissionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var checks = new (bool Ready, string Code, string Stage, string Headline,
            string Guidance)[]
        {
            (facts.DeployedProjectLoaded, "PRODUCTION_PROJECT_NOT_DEPLOYED", "工程预检",
                "正式工程未确认", "请在开发与调试中确认本工位唯一 ECU 工程，不允许直接执行草稿。"),
            (facts.ResourcesExecutable, "PRODUCTION_ASSETS_NOT_READY", "工程预检",
                "TEST 与烧录资源未就绪", "核对固定 TEST 顺序、DBC/波形引用和已发布前置烧录工艺。"),
            (facts.ControllerConnected, "PRODUCTION_USB_DISCONNECTED", "设备预检",
                "等待主控 USB", "主控必须完成本会话的实际 USB 连接。"),
            (facts.EcuCanRateConfirmed, "PRODUCTION_CAN_RATE_NOT_CONFIRMED", "设备预检",
                "ECU CAN 码率未确认", "主控 ECU CAN 设置后必须在本会话回读确认。"),
            (facts.ScannerConnected, "PRODUCTION_SCANNER_DISCONNECTED", "设备预检",
                "等待扫码设备", "请连接已绑定的真实 COM 扫码枪。"),
            (facts.AcceptedScannerObservation, "PRODUCTION_IDENTITY_NOT_ACCEPTED", "扫码识别",
                "等待 ECU 扫码", "扫描本台 ECU 的唯一编号；扫码本身不启动生产检测。"),
            (facts.FrozenProjectMatches, "PRODUCTION_PROJECT_SNAPSHOT_MISMATCH", "资源冻结",
                "扫码后工程版本已变化", "当前工程/型号与扫码时冻结的资源不一致，必须重新检查并扫码。"),
            (facts.SameControllerSession, "PRODUCTION_CONTROLLER_SESSION_STALE", "设备预检",
                "扫码后 USB 会话已改变", "该扫码只属于原主控会话；请确认新会话并重新扫码。"),
            (facts.SameScannerPort, "PRODUCTION_SCANNER_SESSION_STALE", "设备预检",
                "扫码设备端口已改变", "禁止沿用旧 COM 来源的身份，请重新扫码。"),
            (facts.SameEcuCanRate, "PRODUCTION_CAN_RATE_MISMATCH", "设备预检",
                "ECU CAN 码率不匹配", "当前主控波特率与冻结烧录配置不一致，请核对并重新扫码。"),
            (facts.TrustedPhysicalStartConnected, "PRODUCTION_PHYSICAL_START_NOT_CONNECTED", "实体 START",
                "实体 START 尚未接入", "缺少已验证的主控实体 START 输入与新按下沿，不能由软件按钮代替。"),
            (facts.VerifiedInspectionWriterConnected, "PRODUCTION_INSPECTION_NOT_CONNECTED", "正式记录",
                "正式 Inspection 归档未接入", "必须先绑定单台 RunId、真实 FLASH/TEST 证据和持久化判定，才能生产放行。")
        };
        var passed = 0;
        foreach (var check in checks)
        {
            if (!check.Ready)
                return new(check.Code, check.Stage, check.Headline,
                    check.Guidance, passed, checks.Length, false);
            passed++;
        }
        return new("PRODUCTION_ADMISSION_CONDITIONS_MET", "等待实体 START",
            "生产准入条件已核验",
            "仅表示本次准入条件满足；实际物理 START 边沿与正式 RUN 仍须由生产执行器创建并记录。",
            passed, checks.Length, true);
    }
}
