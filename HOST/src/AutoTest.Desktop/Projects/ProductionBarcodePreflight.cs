using AutoTest.Projects.Project;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Projects;

public sealed record ProductionBarcodePreflightResult(string Code, string? ModelCode,
    string? DutSerial, string? HardwareCode, string Explanation)
{
    // Identity matched is NOT an authorized production run, clamp, or flash job.
    public bool IdentityMatched => Code == "SCAN_MODEL_MATCHED";
}

/// <summary>
/// Read-only identity preflight. Never selects the legacy multi-recipe,
/// switches projects, authorizes the clamp or START, or allocates a new unit.
/// </summary>
public static class ProductionBarcodePreflight
{
    // Compatibility with existing explicit UT1 checks; NOT fallback inference
    // for an unconfigured scanner in the live production workbench.
    public static ProductionBarcodePreflightResult Inspect(
        string rawBarcode, ProjectDefinition? currentProject) =>
        Inspect(rawBarcode, currentProject, ScanIdentityPolicy.Default);

    public static ProductionBarcodePreflightResult Inspect(
        string rawBarcode, ProjectDefinition? currentProject, ScanIdentityPolicy policy)
    {
        // For serial-only labels, the deployed project's model is already
        // authoritative. Do not require a second scanner-specific model.
        if (policy.Format == ScanIdentityPolicy.SerialOnly &&
            currentProject?.EcuModelCode is { Length: > 0 } model)
            policy = policy with { FixedModelCode = model };
        var parsed = policy.Decode(rawBarcode);
        if (!parsed.Parsed)
            return new(parsed.Code, parsed.ModelCode, parsed.Serial,
                parsed.HardwareCode, parsed.Explanation);
        if (currentProject is null)
            return new("SCAN_PROJECT_NOT_OPEN", parsed.ModelCode, parsed.Serial,
                parsed.HardwareCode,
                "已识别条码，但当前没有打开 ECU 工程；不得据此启动硬件。");
        if (string.IsNullOrWhiteSpace(currentProject.EcuModelCode))
            return new("SCAN_PROJECT_MODEL_UNBOUND", parsed.ModelCode, parsed.Serial,
                parsed.HardwareCode,
                "当前工程尚未设置正式型号代码，不能把显示名称作为型号。");
        if (!string.Equals(currentProject.EcuModelCode, parsed.ModelCode, StringComparison.Ordinal))
            return new("SCAN_OTHER_MODEL", parsed.ModelCode, parsed.Serial,
                parsed.HardwareCode,
                "扫码型号与当前工程不一致。需要检查工位型号登记，不自动选择旧配方。");
        return new("SCAN_MODEL_MATCHED", parsed.ModelCode, parsed.Serial,
            parsed.HardwareCode,
            "型号与当前工程匹配；仅完成身份预检，尚未取得夹具/实体 START/前置烧录执行许可。");
    }
}
