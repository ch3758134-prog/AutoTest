using AutoTest.UI.Model;

namespace AutoTest.UI.Workspaces;

/// <summary>
/// UI-facing operation descriptions, not a shadow TEST DSL. Each name explains
/// what the operator edits; formal domains decide runtime semantics later.
/// </summary>
public sealed record StepUiSpec(
    string Title, string Help, string TargetLabel = "操作对象 / 引用",
    string ValueLabel = "操作值 / 参数", string ConditionLabel = "条件 / 表达式",
    string InitialValue = "", string[]? Branches = null);

public static class StepInspectorCatalog
{
    public static StepUiSpec For(string type) => type switch
    {
        "等待" => new(type, "等待一个固定时长；单位毫秒。", "—", "等待时间（毫秒）", "—", "500"),
        "CAN 单次发送" => new(type, "从 DBC 选报文，编辑数据或信号赋值；这里只组织发送草稿。", "DBC 报文", "发送数据 HEX / 信号值"),
        "CAN 周期发送" => new(type, "周期任务可独立配置与停止。", "DBC 报文", "发送数据与周期（ms）"),
        "停止周期发送" => new(type, "停止先前创建的周期发送任务。", "周期任务", "—"),
        "CAN 信号读取" => new(type, "选择 DBC 信号并配置读取参数。", "DBC 信号", "变量名", "数据质量"),
        "CAN 信号判定" => new(type, "定义目标信号、比较条件与超时。", "DBC 信号", "期望值", "比较运算 / 超时"),
        "等待响应 WHEN" => new(type, "条件满足时进入子流程，可设定超时。", "DBC 信号或变量", "超时时间（ms）", "触发条件", "1000", ["满足条件", "超时分支"]),
        "条件 IF / ELSE" => new(type, "条件成立和否则有两个独立分支。", "表达式变量", "—", "IF 条件", "", ["THEN", "ELSE"]),
        "SWITCH" => new(type, "根据表达式选择 CASE 或默认分支。", "表达式", "—", "SWITCH 表达式", "", ["CASE 1", "DEFAULT"]),
        "CASE" => new(type, "作为 SWITCH 的分支节点。", "匹配对象", "CASE 匹配值"),
        "循环" => new(type, "循环次数与循环体在同一结构中编辑。", "—", "循环次数", "循环条件", "2", ["BODY"]),
        "TRY / CATCH" => new(type, "异常路径与 FINALLY 清理分离。", "—", "—", "—", "", ["TRY", "CATCH", "FINALLY"]),
        "步骤分组" => new(type, "按顺序组织一组步骤。", "—", "—", "—", "", ["BODY"]),
        "设置变量" => new(type, "设置可在后续表达式中使用的变量。", "变量名", "变量值"),
        "计算" => new(type, "配置变量与计算表达式。", "目标变量", "计算表达式"),
        "期望判定" => new(type, "比较表达式与目标值。", "左操作数", "右操作数", "比较方式"),
        "调用公共流程" => new(type, "选择公共流程及所需的固定修订。", "公共流程", "修订引用"),
        "模拟外部输入" => new(type, "引用输入动作，指定保持和恢复条件。", "输入动作", "保持时间（ms）"),
        "IGN 控制" => new(type, "仅配置 IGN 目标状态，真实控制单独授权。", "IGN", "ON / OFF"),
        "夹具控制" => new(type, "配置夹具动作及等待条件，非本地立即执行。", "电磁阀", "压紧 / 释放"),
        "UDS 请求" => new(type, "编辑服务请求 HEX，后端接线后使用实际 ISO-TP。", "UDS 会话", "请求 HEX"),
        "ECU 烧录" => new(type, "引用独立 Flash Profile，运行必须正式授权。", "烧录配置", "固件资源"),
        "波形开始" => new(type, "选择采集信号与记录配置。", "波形配置", "采集时长（ms）"),
        "波形停止" => new(type, "停止本次波形采集。", "采集任务", "—"),
        "波形分析" => new(type, "引用采集数据与分析参数。", "信号与参考", "分析配置"),
        "波形判定" => new(type, "指定参考、容差和判定条件。", "参考波形", "容差/覆盖率"),
        "日志记录" => new(type, "在流程中记录文本信息。", "—", "日志文本"),
        "中断 / 跳过" => new(type, "控制流程时必须明确作用范围。", "作用范围", "原因"),
        _ => new(type, "编辑该步骤的 UI 参数草稿。")
    };
    public static UiStep New(string type, string phase, string branch = "") => new()
    {
        Type = type, Name = type, Phase = phase, Branch = branch,
        Value = For(type).InitialValue
    };
}
