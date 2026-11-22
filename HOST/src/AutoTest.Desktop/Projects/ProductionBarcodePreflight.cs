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
/// Initial read-only identity preflight. Intentionally does not reuse the obsolete
/// multi-Recipe selector, silently change projects or authorize a physical output.
/// An installed-project index and formal production plan are later gates.
/// </summary>
public static class ProductionBarcodePreflight
{
    public static ProductionBarcodePreflightResult Inspect(
        string rawBarcode, ProjectDefinition? currentProject)
    {
        if (!Ut1BarcodeCodec.TryParse(rawBarcode, out var label, out var parseCode))
            return new(parseCode, null, null, null,
                "条码未通过 UT1 格式/CRC 校验。当前仅开放 UT1 试验编码；不会降级为纯序列号或旧配方。");
        if (currentProject is null)
            return new("SCAN_PROJECT_NOT_OPEN", label!.ProductCode, label.Serial,
                label.HardwareCode, "已收到扫码数据，尚未打开 ECU 工程；未执行任何设备动作。");
        if (string.IsNullOrWhiteSpace(currentProject.EcuModelCode))
            return new("SCAN_PROJECT_MODEL_UNBOUND", label!.ProductCode, label.Serial,
                label.HardwareCode, "当前工程尚未配置 ECU 型号代码，请在扫码设置中明确绑定。");
        if (!string.Equals(currentProject.EcuModelCode, label!.ProductCode, StringComparison.Ordinal))
            return new("SCAN_OTHER_MODEL", label.ProductCode, label.Serial, label.HardwareCode,
                "扫描型号与当前工程不一致。未自动切换工程或选择旧配方。");
        return new("SCAN_MODEL_MATCHED", label.ProductCode, label.Serial, label.HardwareCode,
            "型号与当前工程匹配，编号已识别；尚未完成夹具/实体 START/原生烧录生产预检，不允许生产执行。");
    }
}
