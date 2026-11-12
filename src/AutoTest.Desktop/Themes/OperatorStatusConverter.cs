using System.Globalization;
using System.Windows.Data;

namespace AutoTest.Desktop.Themes;

/// <summary>
/// UI-only presentation of transport/diagnostic state codes. The underlying
/// status codes and audit events remain untouched for engineering support.
/// </summary>
public sealed class OperatorStatusConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Physical Read"] = "实机读取",
            ["Physical"] = "真实设备",
            ["Replay"] = "离线记录",
            ["Mock"] = "旧版模拟数据（不可用于实机判定）",
            ["Unknown"] = "未知",
            ["All"] = "全部",
            ["DEVICE_NOT_CONNECTED"] = "主控尚未连接",
            ["SERVICE_TEMPLATE_NOT_SENT"] = "已选诊断服务，尚未发送",
            ["PROFILE_NOT_LOADED"] = "尚未加载 ECU 诊断配置",
            ["PROFILE_CONFIG_LOADED_NOT_VERIFIED"] = "诊断配置已加载，请核对实际 ECU",
            ["PREPARED_NOT_SENT"] = "请求已编辑，尚未发送",
            ["PARAMETER_INVALID"] = "请求参数不合法",
            ["REPLAY_REQUEST"] = "正在查询已保存的诊断记录",
            ["PHYSICAL_READ_REQUEST"] = "正在向真实 ECU 发送读取请求",
            ["UNSUPPORTED_SOURCE"] = "当前数据来源不支持发送",
            ["CANCELLED"] = "已取消",
            ["REQUEST_CANCELLED"] = "请求已取消",
            ["WORKBENCH_ERROR"] = "操作失败，请查看下方错误说明",
            ["PHYSICAL_NOT_SENT"] = "真实 CAN1 请求未发送",
            ["PHYSICAL_ACCEPTED"] = "主控已确认请求，需结合 ECU 响应判定",
            ["PHYSICAL_REJECTED"] = "设备拒绝请求",
            ["PHYSICAL_UNKNOWN"] = "主控返回不确定，请检查连接和收发记录",
            ["REPLAY_ERROR"] = "离线记录读取失败",
            ["SERVICE_SAVED"] = "诊断服务已保存",
            ["SERVICE_REMOVED"] = "诊断服务已删除",
            ["DOCUMENT_SAVED"] = "诊断配置文件已保存",
            ["DOCUMENT_LOADED"] = "诊断配置文件已加载",
            ["DOCUMENT_ERROR"] = "诊断配置文件操作失败",
            ["VALIDATION_FAILED"] = "配置校验失败",
            ["PROFILE_PARSED_NOT_VERIFIED"] = "配置已解析，尚未确认实际 ECU",
            ["PROFILE_PARSED_NOT_FLASHED"] = "配置已解析，尚未烧录",
            ["IMAGE_ERROR"] = "固件镜像解析失败",
            ["IMAGE_PARSED_NOT_FLASHED"] = "固件已解析，尚未烧录",
            ["IMAGE_NOT_SELECTED"] = "尚未选择固件文件",
            ["PHYSICAL_FLASH_REQUIRES_EXPLICIT_P09_JOB"] = "真实烧录尚未授权，请检查配置并完成本次实机授权",
            ["CATALOG_NOT_READY"] = "尚未发布扫码与测试配方",
            ["NOT_STARTED"] = "尚未运行",
            ["NO_HARDWARE_ACK"] = "尚未收到主控确认",
            ["NO_OBSERVATION"] = "暂无 ECU 观测结果"
        };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = value as string ?? "";
        if (Labels.TryGetValue(raw, out var label)) return label;
        if (raw.StartsWith("REPLAY_LOADED_", StringComparison.OrdinalIgnoreCase))
            return "已加载离线诊断记录";
        if (raw.StartsWith("PHYSICAL_", StringComparison.OrdinalIgnoreCase))
            return "真实设备操作状态：" + raw["PHYSICAL_".Length..];
        // Unexpected machine errors remain visible for support, not mislabeled as success.
        return raw;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
