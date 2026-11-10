using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace AutoTest.Specialists.Flash;

/// <summary>
/// Read-only interpretation of CanTool Configuration.xml data. No live
/// ECU commands, no default CAN targets, no security DLL loading.
/// All XML/default/effective-value sources remain separate.
/// </summary>
public sealed record CanToolConfigurationField(
    string Field, string Value, string Source, string UsedBy, string Note);

public sealed record CanToolConfigurationSnapshot(
    string SourceName, string SourceSha256,
    uint BootRequestId, uint BootResponseId,
    uint AppRequestId, uint AppResponseId,
    uint FlashStart, uint FlashEndInclusive,
    string BaudRate,
    ImmutableArray<CanToolConfigurationField> Fields,
    ImmutableArray<string> Warnings)
{
    public ulong EndExclusive => (ulong)FlashEndInclusive + 1UL;
    public ulong FlashLength => EndExclusive - FlashStart;
    public bool ImageFits(FirmwareImage image) =>
        image.Segments.Length > 0 &&
        image.Segments.All(x => x.Address >= FlashStart && x.EndExclusive <= EndExclusive);
}

public static class CanToolConfigurationImporter
{
    public const int MaxProfileBytes = 512 * 1024;

    public static CanToolConfigurationSnapshot LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > MaxProfileBytes)
            throw new FormatException("配置文件不存在、为空或超过 512 KiB 上限。");
        return Parse(File.ReadAllBytes(path), file.Name);
    }

    public static CanToolConfigurationSnapshot Parse(ReadOnlySpan<byte> data,
        string sourceName = "Configuration.xml")
    {
        if (data.Length is < 1 or > MaxProfileBytes)
            throw new FormatException("配置文件为空或超出 512 KiB 限制。");
        byte[] bytes = data.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        XElement root;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var xml = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxProfileBytes
        }))
            root = XDocument.Load(xml).Root ?? throw new FormatException("XML 缺少根节点。");

        // The CanTool loader reads direct child elements, not arbitrary nested
        // nodes. We therefore reject conflicting duplicates instead of choosing
        // an ambiguous ECU target.
        string? Get(params string[] names)
        {
            var found = root.Elements().Where(e =>
                    names.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim()).Where(v => v.Length > 0).Distinct().ToArray();
            if (found.Length > 1)
                throw new FormatException("同一个配置字段存在相互冲突的多个值：" + names[0]);
            return found.FirstOrDefault();
        }
        uint RequiredHex(string name, params string[] aliases)
        {
            string? value = Get(new[] { name }.Concat(aliases).ToArray());
            if (value is null) throw new FormatException("缺少必须字段：" + name);
            var clean = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
            if (!uint.TryParse(clean, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out uint result))
                throw new FormatException("无效十六进制字段：" + name + "=" + value);
            return result;
        }
        var bootTx = RequiredHex("BootloadPhyID");
        var bootRx = RequiredHex("BootRespond");
        var appTx = RequiredHex("APPPhyID");
        var appRx = RequiredHex("APPRespond");
        var start = RequiredHex("FlashStartAddr", "FLASH起始地址", "FLASHStartAddr");
        var end = RequiredHex("FlashEndAddr", "FLASH结束地址", "FLASHEndAddr");
        if (new[] { bootTx, bootRx, appTx, appRx }.Any(id => id == 0 || id > 0x1FFFFFFF) ||
            bootTx == bootRx || appTx == appRx ||
            (bootTx > 0x7FF) != (bootRx > 0x7FF) ||
            (appTx > 0x7FF) != (appRx > 0x7FF))
            throw new FormatException("APP/Boot CAN ID 缺失、格式冲突或请求 ID 与响应 ID 相同。");
        if (start >= end)
            throw new FormatException("无效的 Flash 起止地址，End 在 CanTool XML 中为包含边界。");
        string baud = Get("BaudRate", "波特率") ?? "500K";
        var normalized = baud.ToUpperInvariant().Replace("BPS", "");
        if (normalized is not ("250K" or "500K" or "250000" or "500000"))
            throw new FormatException("CanTool 配置波特率只识别 250K/500K；不允许猜测主控实际 CAN1 速率。");

        var fields = ImmutableArray.CreateBuilder<CanToolConfigurationField>();
        void AddHex(string name, uint value, string used) => fields.Add(
            new(name, "0x" + value.ToString("X", CultureInfo.InvariantCulture),
                "XML", used, "只用于解释配置；未向主控板发送任何指令"));
        void AddOptional(string name, string used, params string[] aliases)
        {
            var value = Get(new[] { name }.Concat(aliases).ToArray());
            fields.Add(new(name, value ?? "未提供", value is null ? "XML 缺失" : "XML",
                used, value is null ? "不可视为已确认的 ECU 有效参数" : "仅已读取，现场行为未验证"));
        }
        AddHex("BootloadPhyID", bootTx, "Bootloader UDS 请求目标 CAN ID");
        AddHex("BootRespond", bootRx, "Bootloader UDS 响应过滤 CAN ID");
        AddHex("APPPhyID", appTx, "APP UDS 请求目标 CAN ID");
        AddHex("APPRespond", appRx, "APP UDS 响应过滤 CAN ID");
        AddHex("FlashStartAddr", start, "擦除范围与固件段起始地址边界");
        AddHex("FlashEndAddr", end, "包含结束地址；擦除长度=End-Start+1");
        fields.Add(new("FlashLength", "0x" + ((ulong)end - start + 1).ToString("X"),
            "派生值", "擦除范围/段合法性检查", "来源为 XML 地址；尚未证明目标 ECU 可擦除"));
        fields.Add(new("BaudRate", baud, Get("BaudRate", "波特率") is null ? "CanTool 默认" : "XML",
            "CAN 控制器速率（非 UDS PDU）", "必须另行确认主控 CAN1 的实际有效波特率"));
        AddOptional("VersionID", "参考代码主刷写流程未发现直接使用");
        AddOptional("DiagnosticsID", "参考代码主刷写流程未发现直接使用");
        AddOptional("BlockLength", "固件文件对齐块长；不等于 ECU 0x74 最大 UDS 请求长度");
        AddOptional("SafetyFilePath", "Seed/Key 提供程序；不得从 XML 路径推断 DLL ABI", "SeedToKeyDll_Path");
        AddOptional("ForceMinInterval", "ISO-TP CF 最小间隔", "ForceMinIntervalMs");
        AddOptional("BootFeature", "原 CanTool XML Loader 未解析；语义未核实");
        AddOptional("APPFeature", "原 CanTool XML Loader 未解析；语义未核实");
        foreach (var name in new[] { "P2TimeoutMs", "P2StarTimeoutMs", "EraseTimeoutMs",
                     "FlowControlTimeoutMs", "S3TimeoutMs", "ResetWaitMs",
                     "MaxRetries", "ChecksumRetryCount" })
            fields.Add(new(name, Get(name) ?? "未从 XML 加载",
                "原版 CanTool XML Loader 不读取", "运行期默认/手工编辑值",
                "即使 XML 包含此字段，也不能认为原版流程加载过；需冻结实际作业配置"));

        var warnings = ImmutableArray.Create(
            "仅校验配置文本和地址范围；不证明 ECU、主控波特率或 CAN1 编程会话已通过。",
            "原版 CanTool 0x74 高/低半字节解析有缺陷，禁止复用未审查的长度处理。",
            "Seed/Key DLL ABI 尚须独立验证；禁止静默回退演示算法。",
            "擦除、TransferData、校验、复位与 APP 复验均未授权，当前不执行任何物理写入。");
        return new(sourceName, hash, bootTx, bootRx, appTx, appRx, start, end,
            baud, fields.ToImmutable(), warnings);
    }
}
