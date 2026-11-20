namespace AutoTest.UdsLab;

/// <summary>
/// Editable user-facing UDS actions. These are protocol request examples,
/// not an ECU capability database or an authorized hardware test result.
/// </summary>
public static class DiagnosticStepPresets
{
    public static DiagnosticCase CreateWelcomeCase()
    {
        var example = new DiagnosticCase
        {
            Name = "示例｜读取 VIN 与故障信息",
            Description = "两项可编辑的 UDS 请求示例。必须按实际 ECU 诊断规范确认 DID、服务支持和通信 ID；本用例尚未与 ECU 通信。"
        };
        example.Steps.Add(Create("vin"));
        example.Steps.Add(Create("dtc"));
        return example;
    }

    public static DiagnosticCaseStep Create(string choice)
    {
        return choice switch
        {
            "vin" => Defined("读取 VIN（示例）", 0x22, "F190", "",
                "常见 VIN DID F190，需 ECU 实际支持；可在编辑区修改 DID 或原始请求。"),
            "dtc" => Defined("读取故障码列表", 0x19, "02", "FF",
                "0x19 / 02 按 DTC 状态掩码读取。FF 表示请求所有状态位；返回取决于 ECU。"),
            "dtc-count" => Defined("查询故障码数量", 0x19, "01", "FF",
                "0x19 / 01 查询符合状态掩码的 DTC 数量；此模板仅保留原始响应解析。"),
            "did" => Defined("读取自定义 DID", 0x22, "", "",
                "填写你的 ECU 诊断规范给出的 DID（2 字节）；没有规范时不能猜测真实含义。"),
            "session" => Defined("切换扩展诊断会话（未授权）", 0x10, "03", "",
                "仅供编辑用例。此服务当前实体发送尚未开放；运行前会明确拒绝，不会发送。"),
            "custom" => Raw("自定义 UDS 请求", "",
                "直接输入 UDS 应用层 PDU（不包含 CAN ID/ISO-TP 帧头）；物理发送仍受服务白名单约束。"),
            "service" => Defined("选择 UDS 服务", 0x22, "", "",
                "点击右侧 UDS 服务下拉框选择要调用的服务，再编辑该服务需要的参数。"),
            _ => throw new ArgumentOutOfRangeException(nameof(choice), "未知诊断动作：" + choice)
        };
    }

    private static DiagnosticCaseStep Defined(string name, byte sid,
        string primary, string auxiliary, string description)
    {
        var template = UdsOperationTemplates.Find(sid)
            ?? throw new InvalidOperationException("UDS 服务模板不存在：" + sid.ToString("X2"));
        string pdu = "", prefix = "", decoder = "Hex";
        int offset = 0;
        if (primary.Length > 0)
        {
            var composed = UdsOperationTemplates.Compose(template, primary, auxiliary);
            pdu = composed.RequestHex;
            prefix = composed.ExpectedPrefixHex;
            decoder = composed.Decoder;
            offset = composed.DecoderOffset;
        }
        return new DiagnosticCaseStep
        {
            Name = name, Purpose = description,
            Action = new DiagnosticAction
            {
                Name = name, Group = "诊断用例", RequestHex = pdu,
                ServiceSidHex = sid.ToString("X2"),
                PrimaryHex = primary, AuxiliaryHex = auxiliary,
                ExpectedPrefixHex = prefix, Decoder = decoder,
                DataOffset = offset, Scale = 1
            }
        };
    }

    private static DiagnosticCaseStep Raw(string name, string pdu, string description) => new()
    {
        Name = name, Purpose = description,
        Action = new DiagnosticAction
        {
            Name = name, Group = "诊断用例", RequestHex = pdu,
            RawMode = true, Decoder = "Hex", DataOffset = 0,
            Scale = 1
        }
    };
}
