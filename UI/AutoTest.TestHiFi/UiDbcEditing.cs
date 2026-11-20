using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoTest.TestHiFi;

/// <summary>
/// Conservative, UI-only DBC text insertion helpers. Does not replace the formal DBC parser.
/// Only generates syntactically well-formed BO_/SG_ declarations from validated fields.
/// Existing DBC content is not normalized or rewritten.
/// </summary>
internal static class UiDbcEditing
{
    internal sealed record Message(uint EncodedId, string Name, int Dlc, string Sender)
    {
        public string Label => Name + "  ·  0x" + (EncodedId & 0x1FFFFFFFu).ToString("X", CultureInfo.InvariantCulture)
            + (EncodedId > 0x7FFu ? " (29-bit)" : " (11-bit)") + "  ·  DLC " + Dlc;
    }

    private static readonly Regex MessagePattern = new(
        @"^BO_\s+(\d+)\s+([A-Za-z_][A-Za-z0-9_]*):\s+(\d+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex SignalPattern = new(
        @"^\s*SG_\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

    public static IReadOnlyList<Message> Messages(string text)
    {
        var result = new List<Message>();
        foreach (Match match in MessagePattern.Matches(text.Replace("\r\n", "\n")))
        {
            if (uint.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && int.TryParse(match.Groups[3].Value, out var length))
                result.Add(new Message(id, match.Groups[2].Value, length, match.Groups[4].Value));
        }
        return result;
    }

    private static string RequiredName(string input, string field)
    {
        var name = input.Trim();
        if (!NamePattern.IsMatch(name)) throw new ArgumentException(
            field + "只能使用英文字母、数字、下划线，且不能以数字开头；中文请通过别名配置显示。");
        return name;
    }
    private static double Number(string value, string field)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite(result)) throw new ArgumentException(field + "不是有效数值，请使用小数点。");
        return result;
    }
    private static int Integer(string value, string field, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            || result < min || result > max)
            throw new ArgumentException(field + "必须是 " + min + "～" + max + " 的整数。");
        return result;
    }
    private static uint ParseCanId(string input)
    {
        var text = input.Trim();
        uint id;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
                throw new ArgumentException("CAN ID 十六进制格式错误。");
        }
        else if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            throw new ArgumentException("CAN ID 请输入十进制或 0x 前缀的十六进制。");
        if (id > 0x1FFFFFFF) throw new ArgumentException("CAN ID 超过 29 位允许范围。");
        if (id > 0x7FF) id |= 0x80000000u; // Vector DBC extended-frame flag.
        return id;
    }

    public static string AddMessage(string source, string idInput, string nameInput, string dlcInput, string senderInput)
    {
        var id = ParseCanId(idInput);
        var name = RequiredName(nameInput, "报文名称");
        var sender = RequiredName(senderInput, "发送节点");
        var dlc = Integer(dlcInput, "DLC", 0, 64);
        if (Messages(source).Any(m => m.EncodedId == id || m.Name == name))
            throw new ArgumentException("同一 DBC 中已存在该报文 ID 或名称。");
        return source.TrimEnd('\r', '\n') + "\n\nBO_ " +
            id.ToString(CultureInfo.InvariantCulture) + " " + name + ": " +
            dlc.ToString(CultureInfo.InvariantCulture) + " " + sender + "\n";
    }

    public static string AddSignal(string source, uint messageId, string nameInput,
        string startInput, string lengthInput, string byteOrderInput, string signedInput,
        string factorInput, string offsetInput, string minInput, string maxInput,
        string unitInput, string receiverInput)
    {
        var name = RequiredName(nameInput, "信号名称");
        var start = Integer(startInput, "起始位", 0, 511);
        var length = Integer(lengthInput, "位长度", 1, 64);
        var endian = byteOrderInput.Trim().ToLowerInvariant() switch
        {
            "intel" or "1" => "1",
            "motorola" or "0" => "0",
            _ => throw new ArgumentException("字节序只能是 Intel 或 Motorola。")
        };
        var sign = signedInput.Trim() switch
        {
            "+" or "无符号" => "+", "-" or "有符号" => "-",
            _ => throw new ArgumentException("有符号字段请输入 + 或 -。")
        };
        var factor = Number(factorInput, "缩放因子");
        var offset = Number(offsetInput, "偏移");
        var min = Number(minInput, "最小值");
        var max = Number(maxInput, "最大值");
        if (max < min) throw new ArgumentException("最大值不能小于最小值。");
        var receiver = RequiredName(receiverInput, "接收节点");
        var unit = unitInput.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var normalized = source.Replace("\r\n", "\n");
        var messages = MessagePattern.Matches(normalized);
        Match? current = null;
        foreach (Match match in messages)
            if (uint.TryParse(match.Groups[1].Value, out var id) && id == messageId)
            { current = match; break; }
        if (current is null) throw new ArgumentException("未找到对应 DBC 报文，请先新建并选中报文。");
        var next = messages.Cast<Match>().FirstOrDefault(x => x.Index > current.Index);
        var end = next?.Index ?? normalized.Length;
        var segment = normalized.Substring(current.Index, end - current.Index);
        if (segment.Split('\n').Skip(1).Any(line => SignalPattern.Match(line).Groups[1].Value == name))
            throw new ArgumentException("此报文已存在同名信号。");
        var declaration = " SG_ " + name + " : " + start + "|" + length + "@" + endian + sign +
            " (" + factor.ToString("G17", CultureInfo.InvariantCulture) + "," +
            offset.ToString("G17", CultureInfo.InvariantCulture) + ") [" +
            min.ToString("G17", CultureInfo.InvariantCulture) + "|" +
            max.ToString("G17", CultureInfo.InvariantCulture) + "] \"" + unit + "\" " + receiver + "\n";
        var insert = current.Index + current.Length;
        return normalized.Insert(insert, "\n" + declaration);
    }
}
