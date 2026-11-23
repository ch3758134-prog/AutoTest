using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// A field from one completed serial scanner frame. Both field numbers and
/// fixed-position offsets are ONE-based for operator-facing configuration.
/// No extraction can perform an ECU action.
/// </summary>
public sealed record ScanFieldMapping(string Target, int FieldNumber = 1,
    int Start = 1, int Length = 0);

/// <summary>
/// Physical scanner port and framing are configured separately. This station
/// rule only interprets a COMPLETE serial barcode and maps parsed fields to
/// stable system destinations: model, DUT identity, hardware and trace data.
/// Production still requires independently pinned ECU engineering resources.
/// </summary>
public sealed record ScanIdentityPolicy(
    int SchemaVersion,
    string Format,
    string Separator,
    string? FixedModelCode,
    int MinimumSerialLength,
    int MaximumSerialLength,
    string ParseMode = "Delimited",
    IReadOnlyList<ScanFieldMapping>? FieldMappings = null)
{
    public const int CurrentSchema = 1;
    public const string Ut1 = "UT1";
    public const string ModelSerial = "ModelSerial";
    public const string SerialOnly = "SerialOnly";
    public const string Mapped = "Mapped";
    public const string Delimited = "Delimited";
    public const string Fixed = "Fixed";

    public const string ModelTarget = "ModelCode";
    public const string SerialTarget = "DutSerial";
    public const string HardwareTarget = "HardwareCode";
    public static readonly IReadOnlyList<string> SupportedTargets =
    [
        ModelTarget, SerialTarget, HardwareTarget, "BatchCode",
        "ProductionDate", "SupplierCode", "Custom1", "Custom2"
    ];

    public static ScanIdentityPolicy Default => new(CurrentSchema, Ut1, "-", null, 8, 20);

    private static readonly Regex ModelPattern = new(
        @"^[A-Z0-9]{2,16}$", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex SerialPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema ||
            Format is not (Ut1 or ModelSerial or SerialOnly or Mapped))
            throw new FormatException("扫码规则版本或解析格式不受支持。");
        if (Separator is not ("-" or "|" or ":" or "/"))
            throw new FormatException("字段分隔符只允许 -、|、:、/。");
        if (MinimumSerialLength < 1 || MaximumSerialLength > 64 ||
            MinimumSerialLength > MaximumSerialLength)
            throw new FormatException("唯一编号长度必须在 1～64 内，且最小长度不能大于最大长度。");
        if (!string.IsNullOrWhiteSpace(FixedModelCode) &&
            !ModelPattern.IsMatch(FixedModelCode))
            throw new FormatException("工程型号只能使用 2～16 位大写字母和数字。");
        if (Format != Mapped) return;
        if (ParseMode is not (Delimited or Fixed))
            throw new FormatException("字段解析只支持分隔符或固定位置。");
        if (FieldMappings is null || FieldMappings.Count is < 1 or > 8)
            throw new FormatException("请为条码配置 1～8 个字段映射。");
        if (FieldMappings.Count(x => x.Target == SerialTarget) != 1)
            throw new FormatException("必须将且仅将一个字段映射到 ECU 唯一编号。");
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in FieldMappings)
        {
            if (field is null || !SupportedTargets.Contains(field.Target) ||
                !targets.Add(field.Target))
                throw new FormatException("字段目标不支持或重复映射。");
            if (ParseMode == Delimited && field.FieldNumber is < 1 or > 32)
                throw new FormatException("分隔符字段编号必须为 1～32。");
            if (ParseMode == Fixed && (field.Start is < 1 or > 256 ||
                                       field.Length is < 1 or > 256 ||
                                       field.Start + field.Length - 1 > 256))
                throw new FormatException("固定位置需填写 1～256 内有效的起始位置和长度。");
        }
    }

    public ScanIdentityResult Decode(string? raw)
    {
        try { Validate(); }
        catch (FormatException e)
        {
            return new("SCAN_POLICY_INVALID", null, null, null, e.Message);
        }
        if (string.IsNullOrEmpty(raw) || raw.Length > 256 ||
            raw.Any(c => c is < ' ' or > '~') ||
            !string.Equals(raw, raw.Trim(), StringComparison.Ordinal))
            return new("SCAN_PAYLOAD_INVALID", null, null, null,
                "扫码为空、含非 ASCII 可打印字符、首尾空白或超过长度限制。");

        string model, serial;
        string? hardware = null;
        IReadOnlyDictionary<string, string>? fields = null;
        if (Format == Ut1)
        {
            if (!Ut1BarcodeCodec.TryParse(raw, out var parsed, out var reason) ||
                parsed is null)
                return new(reason, null, null, null, "UT1 格式或 CRC 校验失败。");
            model = parsed.ProductCode;
            serial = parsed.Serial;
            hardware = parsed.HardwareCode;
        }
        else if (Format == ModelSerial)
        {
            var split = raw.IndexOf(Separator, StringComparison.Ordinal);
            if (split <= 0 || split >= raw.Length - Separator.Length)
                return new("SCAN_MODEL_SERIAL_FORMAT", null, null, null,
                    "条码必须严格为型号" + Separator + "唯一编号。");
            model = raw[..split];
            serial = raw[(split + Separator.Length)..];
        }
        else if (Format == Mapped)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] segments = ParseMode == Delimited
                ? raw.Split(Separator, StringSplitOptions.None)
                : Array.Empty<string>();
            foreach (var mapping in FieldMappings!)
            {
                string value;
                if (ParseMode == Delimited)
                {
                    if (mapping.FieldNumber > segments.Length)
                        return new("SCAN_FIELD_MISSING", null, null, null,
                            "条码缺少第 " + mapping.FieldNumber + " 个字段。");
                    value = segments[mapping.FieldNumber - 1];
                }
                else
                {
                    if (mapping.Start + mapping.Length - 1 > raw.Length)
                        return new("SCAN_FIELD_MISSING", null, null, null,
                            "固定位置字段「" + mapping.Target + "」超出本次扫码长度。");
                    value = raw.Substring(mapping.Start - 1, mapping.Length);
                }
                if (string.IsNullOrWhiteSpace(value) ||
                    !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                    return new("SCAN_FIELD_EMPTY", null, null, null,
                        "字段「" + mapping.Target + "」为空或包含首尾空格。");
                values.Add(mapping.Target, value);
            }
            model = values.GetValueOrDefault(ModelTarget) ?? FixedModelCode ?? "";
            serial = values[SerialTarget];
            hardware = values.GetValueOrDefault(HardwareTarget);
            fields = values;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(FixedModelCode))
                return new("SCAN_PROJECT_MODEL_REQUIRED", null, null, null,
                    "纯编号由当前 ECU 工程提供型号，请先打开或部署工程。");
            model = FixedModelCode;
            serial = raw;
        }

        if (!ModelPattern.IsMatch(model))
            return new("SCAN_MODEL_INVALID", null, null, null,
                "型号字段不是有效的工程型号代码（2～16 位大写字母或数字）。");
        if (serial.Length < MinimumSerialLength || serial.Length > MaximumSerialLength ||
            !SerialPattern.IsMatch(serial))
            return new("SCAN_SERIAL_INVALID", model, null, hardware,
                "ECU 唯一编号不符合长度或字符约束；不自动去零、转换大小写或截断。");
        return new("SCAN_IDENTITY_PARSED", model, serial, hardware,
            Format == SerialOnly
                ? "完整条码已映射到 ECU 唯一编号；型号由当前工程提供。"
                : "字段已解析，等待型号工程匹配；不是任何实体运行授权。",
            fields);
    }
}

public sealed record ScanIdentityResult(
    string Code, string? ModelCode, string? Serial, string? HardwareCode,
    string Explanation, IReadOnlyDictionary<string, string>? Fields = null)
{
    public bool Parsed => Code == "SCAN_IDENTITY_PARSED";
}
