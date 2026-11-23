using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Scanner payload interpretation, never a hardware or production authorization.
/// Transport COM settings and the ECU model/project registry are separate.
/// No inference from project file names, no serial allocation, and no implicit
/// conversion of an arbitrary barcode to a UT1 label.
/// </summary>
public sealed record ScanIdentityPolicy(
    int SchemaVersion,
    string Format,
    string Separator,
    string? FixedModelCode,
    int MinimumSerialLength,
    int MaximumSerialLength)
{
    public const int CurrentSchema = 1;
    public const string Ut1 = "UT1";
    public const string ModelSerial = "ModelSerial";
    public const string SerialOnly = "SerialOnly";
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
            Format is not (Ut1 or ModelSerial or SerialOnly))
            throw new FormatException("扫码规则版本或格式不受支持。");
        if (Separator is not ("-" or "|" or ":" or "/"))
            throw new FormatException("型号+编号模式的分隔符只允许 -、|、:、/。");
        if (MinimumSerialLength < 1 || MaximumSerialLength > 64 ||
            MinimumSerialLength > MaximumSerialLength)
            throw new FormatException("唯一编号长度必须在 1～64 内，且最小长度不能大于最大长度。");
        if (Format == SerialOnly &&
            (string.IsNullOrWhiteSpace(FixedModelCode) ||
             !ModelPattern.IsMatch(FixedModelCode)))
            throw new FormatException("纯序列号模式必须明确绑定一个正式 ECU 型号码。");
        if (!string.IsNullOrWhiteSpace(FixedModelCode) &&
            !ModelPattern.IsMatch(FixedModelCode))
            throw new FormatException("固定 ECU 型号码只能使用 2～16 位大写字母和数字。");
    }

    public ScanIdentityResult Decode(string? raw)
    {
        try { Validate(); }
        catch (FormatException e)
        {
            return new("SCAN_POLICY_INVALID", null, null, null, e.Message);
        }
        if (string.IsNullOrEmpty(raw) || raw.Length > 256 ||
            raw.Any(c => c is < ' ' or > '~') || !string.Equals(raw, raw.Trim(), StringComparison.Ordinal))
            return new("SCAN_PAYLOAD_INVALID", null, null, null,
                "原始扫码为空、包含非 ASCII 可打印字符、首尾空白或超过长度限制。");
        string model, serial;
        string? hardware = null;
        if (Format == Ut1)
        {
            if (!Ut1BarcodeCodec.TryParse(raw, out var parsed, out var reason) || parsed is null)
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
        else
        {
            // Serial-only labels do not identify the product. A deliberate
            // single-model station binding is the only source of the model.
            model = FixedModelCode!;
            serial = raw;
        }
        if (!ModelPattern.IsMatch(model))
            return new("SCAN_MODEL_INVALID", null, null, null,
                "型号字段只能使用 2～16 位大写字母和数字。");
        if (serial.Length < MinimumSerialLength || serial.Length > MaximumSerialLength ||
            !SerialPattern.IsMatch(serial))
            return new("SCAN_SERIAL_INVALID", model, null, hardware,
                "唯一编号不满足长度或字符规则；不自动去零、大小写转换或截断。");
        return new("SCAN_IDENTITY_PARSED", model, serial, hardware,
            Format == SerialOnly
                ? "纯序列号已按显式绑定的工位型号解析；仅为身份，不代表实体型号验证。"
                : "已解析型号和唯一编号；等待型号工程匹配及真实生产预检。");
    }
}

public sealed record ScanIdentityResult(
    string Code, string? ModelCode, string? Serial, string? HardwareCode,
    string Explanation)
{
    public bool Parsed => Code == "SCAN_IDENTITY_PARSED";
}
