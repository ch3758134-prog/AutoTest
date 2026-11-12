using System.Globalization;
using System.Windows.Data;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// Presentation-only translations for formal TEST editor steps.
/// The persisted node types and parameters are unchanged.
/// </summary>
public static class LabStepLocalization
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sequence"] = "顺序执行",
            ["TestCase"] = "测试用例",
            ["If"] = "条件判断 IF",
            ["Switch"] = "分支选择 SWITCH",
            ["Case"] = "分支 CASE",
            ["Default"] = "默认分支",
            ["When"] = "条件等待 WHEN",
            ["Loop"] = "循环",
            ["TryCatch"] = "异常处理",
            ["Subroutine"] = "子程序定义",
            ["CallSubroutine"] = "调用子程序",
            ["SetVariable"] = "设置变量",
            ["MockObserve"] = "旧版模拟观测（禁止真实运行）",
            ["Observe"] = "读取观测",
            ["Assert"] = "断言判定",
            ["Wait"] = "定时等待",
            ["ControllerState"] = "按键状态",
            ["Log"] = "写入记录",
            ["Fail"] = "主动判定失败",
            ["Prepare"] = "准备",
            ["Preparation"] = "准备",
            ["Test"] = "测试",
            ["Cleanup"] = "清理",
            ["body"] = "步骤",
            ["then"] = "条件成立",
            ["else"] = "条件不成立",
            ["cases"] = "分支集合",
            ["try"] = "执行",
            ["catch"] = "异常分支",
            ["finally"] = "始终清理"
        };

    private static readonly IReadOnlyDictionary<string, string> ParameterNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["variable"] = "变量名称",
            ["value"] = "变量值",
            ["equals"] = "期望值 / 匹配条件",
            ["milliseconds"] = "等待时间（毫秒）",
            ["timeoutMs"] = "超时时间（毫秒）",
            ["count"] = "循环次数",
            ["message"] = "日志内容",
            ["name"] = "子程序名称",
            ["target"] = "目标子程序",
            ["controllerId"] = "控制器编号",
            ["stateId"] = "按键状态编号",
            ["revision"] = "资源修订版本",
            ["assetId"] = "资源编号",
            ["assetRevision"] = "资源版本"
        };

    public static string DisplayParameter(string? key) =>
        key is not null && ParameterNames.TryGetValue(key, out var display)
            ? display : key ?? "";
    public static string Display(string? raw) =>
        raw is not null && Names.TryGetValue(raw, out var label)
            ? label
            : raw ?? "";
}

public sealed class LabStepTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LabStepLocalization.Display(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class LabParameterNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LabStepLocalization.DisplayParameter(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
