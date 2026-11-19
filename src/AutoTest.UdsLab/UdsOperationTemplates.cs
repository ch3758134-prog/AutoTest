using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>
/// Human-facing UDS service templates. They build diagnostic PDUs, never CAN/ISO-TP
/// frames and never authorize a physical transmission. Unknown ECU-specific DIDs
/// stay user-configurable. The real UDS client and physical fence remain authoritative.
/// </summary>
public sealed record UdsOperationTemplate(
    byte Sid, string Name, string Group, string PrimaryLabel, string AuxiliaryLabel,
    string PrimaryDefault, string AuxiliaryDefault, string Guidance, bool PhysicalEnabled)
{
    public string Display => $"0x{Sid:X2}  {Name}" + (PhysicalEnabled ? "" : "  · 发送待授权");
}

public sealed record ComposedUdsOperation(string RequestHex, string ExpectedPrefixHex,
    string Decoder, int DecoderOffset);

/// <summary>UI choices for standard subfunctions; not an ECU capability claim.</summary>
public sealed record UdsSubfunctionPreset(byte Sid, string Name, string PrimaryHex, string AuxiliaryHex)
{
    public string Display => Name + "  ·  " + PrimaryHex;
}


public static class UdsOperationTemplates
{
    public static IReadOnlyList<UdsOperationTemplate> All { get; } =
    [
        new(0x22,"读取数据标识符 (DID)","ECU 信息","DID（两字节 HEX）","其他 DID（可选）",
            "","","填写 ECU 诊断规范中的 DID（两字节），可额外读取其他 DID。F190 常用于 VIN，但不假设每台 ECU 支持。",true),
        new(0x19,"读取故障码 (DTC)","故障诊断","子功能（HEX）","子功能参数（HEX）",
            "02","FF","02=按状态掩码查询 DTC，FF=状态掩码；其他子功能须依 ECU 规范编辑。",true),
        new(0x24,"读取 DID 缩放信息","ECU 信息","DID（两字节 HEX）","其他参数（HEX）",
            "","","根据 ECU 规范确定 DID 是否支持缩放信息。",true),
        new(0x23,"读取内存地址","高级诊断","地址及长度格式等完整参数（HEX）","追加数据（HEX）",
            "","", "0x23 需按 ECU 规范填写地址与长度格式；不会自动猜测内存布局。",true),
        new(0x10,"切换诊断会话","诊断会话","会话子功能（HEX）","追加数据（HEX）",
            "03","","01 默认会话、03 常见扩展会话。当前物理授权尚未开放本服务。",false),
        new(0x3E,"TesterPresent 会话维持","诊断会话","子功能（HEX）","追加数据（HEX）",
            "00","","00=请求正常正响应。自动维持会话须另行审查。",false),
        new(0x11,"ECU 复位","ECU 控制","复位类型（HEX）","追加数据（HEX）",
            "01","","真实复位会改变 ECU 状态，当前禁止实体发送。",false),
        new(0x14,"清除故障信息","故障诊断","DTC 组（3 字节）","追加数据（HEX）",
            "FFFFFF","","清故障与读取故障不同，当前禁止实体发送。",false),
        new(0x2E,"写入数据标识符","ECU 控制","DID（两字节 HEX）","写入的数据（HEX）",
            "","","必须依 ECU 规范填写写入数据；当前禁止实体发送。",false),
        new(0x2F,"输入输出控制","ECU 控制","DID（两字节 HEX）","控制参数和数据（HEX）",
            "","","影响 ECU I/O 状态；当前禁止实体发送。",false),
        new(0x31,"执行诊断例程","ECU 控制","例程子功能（HEX）","例程 ID（2 字节）及参数（HEX）",
            "01","","01 启动、02 停止、03 结果；必须有 ECU 厂商例程定义，当前禁止实体发送。",false),
        new(0x27,"安全访问","诊断安全","子功能（HEX）","附加数据（HEX）",
            "01","","涉及 Seed/Key、权限和锁定计数；当前禁止实体发送。",false),
        new(0x28,"通信控制","ECU 控制","控制类型（HEX）","通信类型等参数（HEX）",
            "","","可能关闭 ECU 通信，当前禁止实体发送。",false),
        new(0x2A,"周期数据标识符","高级诊断","传输模式（HEX）","周期数据标识符（HEX）",
            "","","0x2A 会更改 ECU 周期输出状态，当前禁止实体发送。",false),
        new(0x85,"故障码设置控制","ECU 控制","子功能（HEX）","其他参数（HEX）",
            "","","改变 DTC 设置策略，当前禁止实体发送。",false),
    ];

    public static UdsOperationTemplate? Find(byte sid) => All.FirstOrDefault(x => x.Sid == sid);
    public static IReadOnlyList<UdsSubfunctionPreset> PresetsFor(byte sid) =>
        KnownSubfunctions.Where(p => p.Sid == sid).ToArray();

    private static readonly UdsSubfunctionPreset[] KnownSubfunctions =
    [
        new(0x19, "按状态读取 DTC 数量", "01", "FF"),
        new(0x19, "按状态读取 DTC 列表", "02", "FF"),
        new(0x10, "默认诊断会话（实体未开放）", "01", ""),
        new(0x10, "扩展诊断会话（实体未开放）", "03", ""),
        new(0x10, "编程会话（实体未开放）", "02", ""),
        new(0x3E, "请求 TesterPresent 正响应（实体未开放）", "00", ""),
        new(0x11, "硬复位（实体未开放）", "01", ""),
        new(0x11, "钥匙关开复位（实体未开放）", "02", ""),
        new(0x11, "软复位（实体未开放）", "03", ""),
        new(0x31, "启动例程（实体未开放）", "01", ""),
        new(0x31, "停止例程（实体未开放）", "02", ""),
        new(0x31, "获取例程结果（实体未开放）", "03", ""),
        new(0x27, "请求 Seed（实体未开放）", "01", ""),
        new(0x27, "发送 Key（实体未开放）", "02", "")
    ];


    private static byte[] HexPart(string raw, string name, int? requiredLength = null)
    {
        byte[] value = string.IsNullOrWhiteSpace(raw) ? [] : UdsHex.Parse(raw);
        if (requiredLength is int size && value.Length != size)
            throw new FormatException(name + "必须填写 " + size + " 个十六进制字节。");
        return value;
    }

    public static ComposedUdsOperation Compose(UdsOperationTemplate template, string primary, string auxiliary)
    {
        var a = HexPart(primary, template.PrimaryLabel);
        var b = HexPart(auxiliary, template.AuxiliaryLabel);
        byte sid = template.Sid;
        if (sid is 0x22 or 0x24)
        {
            if (a.Length != 2) throw new FormatException("DID 必须为 2 个字节，如 F190。");
            if (sid == 0x24 && b.Length != 0)
                throw new FormatException("0x24 模板不支持附加 DID，请在原始模式编辑。");
            if (sid == 0x22 && b.Length % 2 != 0)
                throw new FormatException("其他 DID 必须以 2 字节为一组。");
        }
        else if (sid is 0x19 or 0x10 or 0x3E or 0x11 or 0x27 or 0x28 or 0x2A or 0x31 or 0x85)
        {
            if (a.Length != 1) throw new FormatException("子功能应填写 1 个字节。");
            if (sid == 0x19 && a[0] == 0x02 && b.Length != 1)
                throw new FormatException("0x19 子功能 02 需要 1 字节 DTC 状态掩码。");
            if (sid == 0x31 && b.Length < 2)
                throw new FormatException("0x31 需要 2 字节例程 ID。");
        }
        else if (sid is 0x14 or 0x2E or 0x2F)
        {
            if (a.Length != (sid == 0x14 ? 3 : 2))
                throw new FormatException(sid == 0x14 ? "DTC 组必须为 3 个字节。" : "DID 必须为 2 个字节。");
            if ((sid is 0x2E or 0x2F) && b.Length == 0)
                throw new FormatException("此服务还需要写入/控制参数。");
        }
        else if (sid == 0x23 && a.Length == 0)
            throw new FormatException("0x23 必须指定地址和长度格式参数。");

        byte[] request = [sid, ..a, ..b];
        if (request.Length > 4095) throw new FormatException("请求 PDU 超过经典 ISO-TP 长度上限。");
        byte[] prefix = sid switch
        {
            0x22 or 0x24 or 0x2E or 0x2F => [(byte)(sid + 0x40), ..a.Take(2)],
            0x10 or 0x11 or 0x19 or 0x27 or 0x28 or 0x2A or 0x3E or 0x31 or 0x85
                => [(byte)(sid + 0x40), a[0]],
            _ => [(byte)(sid + 0x40)]
        };
        if (sid == 0x31 && b.Length >= 2) prefix = [(byte)(sid + 0x40), a[0], b[0], b[1]];
        return new ComposedUdsOperation(UdsHex.Format(request), UdsHex.Format(prefix),
            sid == 0x19 && a[0] == 0x02 ? "Dtc" :
            sid == 0x22 && a[0] == 0xF1 && a[1] == 0x90 && b.Length == 0 ? "Ascii" : "Hex",
            sid is 0x22 or 0x24 ? prefix.Length : 0);
    }

    public static (string Primary, string Auxiliary) SplitRequest(UdsOperationTemplate template, byte[] pdu)
    {
        if (pdu.Length == 0 || pdu[0] != template.Sid) return (template.PrimaryDefault, template.AuxiliaryDefault);
        int n = template.Sid switch
        {
            0x22 or 0x24 or 0x2E or 0x2F => 2,
            0x14 => 3,
            0x23 => pdu.Length - 1,
            _ => 1
        };
        n = Math.Min(n, pdu.Length - 1);
        return (UdsHex.Format(pdu.Skip(1).Take(n)), UdsHex.Format(pdu.Skip(1 + n)));
    }
}
