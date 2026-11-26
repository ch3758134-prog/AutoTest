using System.IO;
using System.Text.RegularExpressions;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Offline station production identity contracts. NONE of these objects
/// describes a TEST sequence, flash file, or a physical actuation command.
/// </summary>
public sealed record ProductionBarcodeSample(string Raw, bool ShouldAccept,
    string ExpectedSerial = "", string ExpectedModel = "");

public sealed record ProductionBarcodeRule(
    string RuleId, string Name, int Revision, bool Enabled, string Format,
    string Prefix, int ExactLength, string AllowedCharacters,
    string Separator, int SerialField, int ModelField, int HardwareField,
    int BatchField, int SerialStart, int SerialLength, int ModelStart,
    int ModelLength, int HardwareStart, int HardwareLength, string RegexExpression,
    IReadOnlyList<ProductionBarcodeSample> Samples)
{
    public const string Serial = "Serial";
    public const string Delimited = "Delimited";
    public const string Fixed = "Fixed";
    public const string RegexFormat = "Regex";
    public const string Ut1 = "UT1";

    public static ProductionBarcodeRule NewSerial() => new(
        Guid.NewGuid().ToString("N"), "整串唯一编号", 1, true, Serial,
        "", 0, @"[A-Za-z0-9_.-]+", "|", 1, 0, 0, 0,
        1, 0, 0, 0, 0, 0, "",
        // No invented positive/negative sample may count as real station
        // barcode validation. A fresh station must provide its own samples.
        Array.Empty<ProductionBarcodeSample>());

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleId) || RuleId.Length > 64 ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Revision < 1 ||
            Format is not (Serial or Delimited or Fixed or RegexFormat or Ut1) ||
            ExactLength is < 0 or > 256 || Prefix is null || Prefix.Length > 64 ||
            Separator is null || Separator.Length is < 1 or > 4 ||
            SerialField is < 1 or > 32 || ModelField is < 0 or > 32 ||
            HardwareField is < 0 or > 32 || BatchField is < 0 or > 32 ||
            SerialStart is < 1 or > 256 || SerialLength is < 0 or > 256 ||
            ModelStart is < 0 or > 256 || ModelLength is < 0 or > 256 ||
            HardwareStart is < 0 or > 256 || HardwareLength is < 0 or > 256 ||
            Samples is null || Samples.Count > 500 ||
            AllowedCharacters is null || RegexExpression is null)
            throw new InvalidDataException("编码规则 ID、格式、字段位置或长度无效：" + Name);
        if (Format == Fixed && (SerialLength == 0 ||
            SerialStart + SerialLength - 1 > 256))
            throw new InvalidDataException("固定位置必须指定完整编号区间。");
        if (new[] { SerialField, ModelField, HardwareField, BatchField }
            .Where(x => x > 0).Distinct().Count() !=
            new[] { SerialField, ModelField, HardwareField, BatchField }.Count(x => x > 0) &&
            Format == Delimited)
            throw new InvalidDataException("不同字段不能映射到相同的分隔字段序号。");
        if (AllowedCharacters.Length is < 1 or > 256 ||
            RegexExpression.Length > 512)
            throw new InvalidDataException("编码字符校验或正则过长/为空。");
        _ = StrictRegex(AllowedCharacters);
        if (Format == RegexFormat)
        {
            if (RegexExpression.Length == 0) throw new InvalidDataException("正则规则不能为空。");
            var re = StrictRegex(RegexExpression);
            if (!re.GetGroupNames().Contains("serial"))
                throw new InvalidDataException("正则规则必须包含 (?<serial>...) 命名组。");
        }
        foreach (var sample in Samples)
            if (sample is null || sample.Raw is null || sample.Raw.Length > 512)
                throw new InvalidDataException("编码规则的样例超出长度限制。");
    }

    private static Regex StrictRegex(string pattern) => new(
        @"\A(?:" + pattern + @")\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(80));

    public ProductionBarcodeFields Parse(string? raw)
    {
        if (!Enabled) return new(false, "RULE_DISABLED", "规则已停用", "", "", "", "");
        try { Validate(); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException)
        { return new(false, "RULE_INVALID", e.Message, "", "", "", ""); }
        if (string.IsNullOrEmpty(raw) || raw.Length > 256 ||
            raw.Any(c => c is < ' ' or > '~') || raw != raw.Trim())
            return new(false, "RAW_INVALID", "编码为空、超过256字符或包含无效字符", "", "", "", "");
        if (ExactLength > 0 && raw.Length != ExactLength)
            return new(false, "LENGTH_MISMATCH", "条码长度不符合本规则", "", "", "", "");
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal))
            return new(false, "PREFIX_MISMATCH", "固定前缀不匹配", "", "", "", "");
        try
        {
            if (!StrictRegex(AllowedCharacters).IsMatch(raw))
                return new(false, "CHARACTER_MISMATCH", "字符集校验不匹配", "", "", "", "");
            string serial, model = "", hardware = "", batch = "";
            if (Format == Ut1)
            {
                if (!Ut1BarcodeCodec.TryParse(raw, out var parsed, out var reason) ||
                    parsed is null)
                    return new(false, "CHECK_DIGIT_INVALID", "UT1 校验失败：" + reason,
                        "", "", "", "");
                serial = parsed.Serial; model = parsed.ProductCode;
                hardware = parsed.HardwareCode;
            }
            else if (Format == Serial)
            {
                serial = raw;
            }
            else if (Format == Delimited)
            {
                var fields = raw.Split([Separator], StringSplitOptions.None);
                string Item(int index) => index <= 0 ? "" :
                    index > fields.Length ? "" : fields[index - 1];
                serial = Item(SerialField); model = Item(ModelField);
                hardware = Item(HardwareField); batch = Item(BatchField);
            }
            else if (Format == Fixed)
            {
                string Part(int start, int length) => start <= 0 || length <= 0 ? "" :
                    start + length - 1 > raw.Length ? "" :
                    raw.Substring(start - 1, length);
                serial = Part(SerialStart, SerialLength);
                model = Part(ModelStart, ModelLength);
                hardware = Part(HardwareStart, HardwareLength);
            }
            else
            {
                var match = StrictRegex(RegexExpression).Match(raw);
                if (!match.Success)
                    return new(false, "REGEX_MISMATCH", "正则表达式未完整匹配", "", "", "", "");
                serial = match.Groups["serial"].Value;
                model = match.Groups["model"].Value;
                hardware = match.Groups["hardware"].Value;
                batch = match.Groups["batch"].Value;
            }
            if (serial.Length is < 1 or > 64 ||
                !Regex.IsMatch(serial, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80)))
                return new(false, "SERIAL_INVALID", "唯一编号字段为空或格式非法", "", "", "", "");
            if (model.Length > 0 && !Regex.IsMatch(model, @"\A[A-Z0-9]{2,16}\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80)))
                return new(false, "MODEL_INVALID", "型号字段格式不合法", "", "", "", "");
            if (hardware.Length > 64 || batch.Length > 64 ||
                hardware.Any(char.IsControl) || batch.Any(char.IsControl))
                return new(false, "OPTIONAL_FIELD_INVALID", "硬件版本或批次字段格式非法",
                    "", "", "", "");
            return new(true, "PARSED", "解析成功，未执行真实硬件操作",
                serial, model, hardware, batch);
        }
        catch (RegexMatchTimeoutException)
        { return new(false, "REGEX_TIMEOUT", "正则表达式处理超时，规则已拒绝", "", "", "", ""); }
        catch (ArgumentException error)
        { return new(false, "REGEX_INVALID", error.Message, "", "", "", ""); }
    }
}

public sealed record ProductionBarcodeFields(
    bool Accepted, string Code, string Explanation, string Serial,
    string Model, string Hardware, string Batch);

public sealed record ProductionIdentityRow(
    string Serial, string ModelCode, string HardwareCode,
    DateTimeOffset? ExpiresUtc = null, string Batch = "");

public sealed record ProductionProductBinding(
    string ModelCode, string HardwareCode, string ManifestPath);

/// <summary>One DUT position with two optional clamp outputs. An output ACK
/// is not evidence of mechanical closure. START is not a safety interlock.</summary>
public sealed record ProductionFixtureConfiguration(
    string StationSlotId, bool UseClamp1, bool UseClamp2,
    string Clamp1Output, string Clamp2Output, string ClampClosedFeedback,
    string ClampOpenFeedback, string StartInput, string StopInput,
    string InterlockInput, int MovementTimeoutMs, string ReleasePolicy,
    int FirstClamp = 0,
    bool Clamp1PressHigh = true, bool Clamp2PressHigh = true,
    int SettleDelayMs = 0)
{
    public static ProductionFixtureConfiguration Draft => new(
        "SLOT-01", false, false, "MCU:PTB1/J1-B5",
        "MCU:PTC2/J1-B4", "NOT_WIRED", "NOT_WIRED",
        "NOT_WIRED", "NOT_WIRED", "NOT_WIRED", 3000,
        "AFTER_DURABLE_RECORD_AND_SAFE_RELEASE_CONFIRMATION");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StationSlotId) ||
            StationSlotId.Length > 64 ||
            Clamp1Output != "MCU:PTB1/J1-B5" ||
            Clamp2Output != "MCU:PTC2/J1-B4" ||
            MovementTimeoutMs is < 100 or > 120000 ||
            SettleDelayMs is < 0 or > 30000 ||
            ReleasePolicy != "AFTER_DURABLE_RECORD_AND_SAFE_RELEASE_CONFIRMATION")
            throw new InvalidDataException("工位槽位、夹具通道或安全释放合同无效。");
        if (FirstClamp is < 0 or > 2 ||
            (UseClamp1 && UseClamp2 && FirstClamp == 0))
            throw new InvalidDataException("同时启用两路夹具时，必须明确选择先操作哪一路（1 或 2）。");
        if (StartInput is null || StopInput is null ||
            InterlockInput is null || ClampClosedFeedback is null ||
            ClampOpenFeedback is null)
            throw new InvalidDataException("工位反馈和实体输入不能为 null；未接线请明确使用 NOT_WIRED。");
        if (StartInput.Length > 128 || StopInput.Length > 128 ||
            InterlockInput.Length > 128 || ClampClosedFeedback.Length > 128 ||
            ClampOpenFeedback.Length > 128)
            throw new InvalidDataException("工位输入来源标识长度无效。");
    }

    public IReadOnlyList<string> PhysicalBlockers()
    {
        var issues = new List<string>
        {
            "工位输入配置只登记来源，不构成机械到位、实体 START 或互锁的实测证据；正式生产需可信固件/传感器适配和现场确认"
        };
        if (StartInput == "NOT_WIRED") issues.Add("实体 START 输入未接入并验证");
        if (StopInput == "NOT_WIRED") issues.Add("实体 STOP 输入未接入并验证");
        if (InterlockInput == "NOT_WIRED") issues.Add("安全互锁输入未接入并验证");
        if ((UseClamp1 || UseClamp2) && ClampClosedFeedback == "NOT_WIRED")
            issues.Add("夹具压紧机械反馈尚未取得可信来源");
        if ((UseClamp1 || UseClamp2) && ClampOpenFeedback == "NOT_WIRED")
            issues.Add("夹具释放机械反馈尚未取得可信来源");
        return issues;
    }
}

public sealed record StationProductionConfiguration(
    int SchemaVersion, int Revision, string Mode, string FixedModel,
    string FixedHardware, IReadOnlyList<ProductionBarcodeRule> Rules,
    IReadOnlyList<ProductionIdentityRow> IdentityRows,
    IReadOnlyList<ProductionProductBinding> Products,
    ProductionFixtureConfiguration Fixture)
{
    public const string FixedStation = "FixedStation";
    public const string BarcodeModel = "BarcodeModel";
    public const string RegistryModel = "RegistryModel";
    public static StationProductionConfiguration New() => new(
        1, 1, FixedStation, "", "",
        [ProductionBarcodeRule.NewSerial()], [],
        [], ProductionFixtureConfiguration.Draft);

    public void ValidateDraft()
    {
        if (SchemaVersion != 1 || Revision < 1 ||
            Mode is not (FixedStation or BarcodeModel or RegistryModel) ||
            Rules is null || IdentityRows is null || Products is null ||
            Fixture is null || Rules.Count > 200 || IdentityRows.Count > 100000 ||
            Products.Count > 256 || !Rules.Any(x => x.Enabled))
            throw new InvalidDataException("生产配置版本、身份来源或规则列表不完整。");
        Fixture.Validate();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rules)
        {
            rule.Validate();
            if (!ruleIds.Add(rule.RuleId))
                throw new InvalidDataException("多个编码规则使用了相同 ID：" + rule.RuleId);
        }

        var productKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in Products)
        {
            if (string.IsNullOrWhiteSpace(x.ModelCode) ||
                !Regex.IsMatch(x.ModelCode, @"\A[A-Z0-9]{2,16}\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80)) ||
                string.IsNullOrWhiteSpace(x.ManifestPath) ||
                !Path.IsPathFullyQualified(x.ManifestPath) ||
                !productKeys.Add(x.ModelCode + "\u001f" + x.HardwareCode))
                throw new InvalidDataException("型号/硬件版本到 ECU 工程的映射重复或非法。");
        }
        var serials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in IdentityRows)
        {
            if (string.IsNullOrWhiteSpace(row.Serial) || row.Serial.Length > 64 ||
                row.ExpiresUtc is { } expired && expired <= DateTimeOffset.UtcNow ||
                !serials.Add(row.Serial))
                throw new InvalidDataException("编号绑定表存在重复编号或无效行：" + row.Serial);
            if (!Products.Any(x => x.ModelCode == row.ModelCode &&
                x.HardwareCode == row.HardwareCode))
                throw new InvalidDataException("编号绑定表引用未登记产品：" + row.Serial);
        }
    }

    public void Validate()
    {
        ValidateDraft();
        if (Products.Count == 0)
            throw new InvalidDataException("正式发布至少需要绑定一份完整 ECU 工程。");
        if (Mode == FixedStation &&
            !Products.Any(x => x.ModelCode == FixedModel && x.HardwareCode == FixedHardware))
            throw new InvalidDataException("固定型号工位未对应唯一已发布的产品工程。");
    }
}

public sealed record ProductionPublishedProduct(
    string ModelCode, string HardwareCode, string ManifestPath, Guid ProjectId,
    string ManifestSha256, Guid TestProjectId, string TestProjectSha256,
    string NativeFlashSha256, string FlashJobFingerprint,
    string FirmwareSha256, int EcuCanKbps, string FixtureSha256,
    IReadOnlyList<string> OrderedCaseNames);

public sealed record PublishedStationProductionConfiguration(
    int SchemaVersion, string ConfigurationSha256, Guid PublicationId,
    DateTimeOffset PublishedAtUtc, StationProductionConfiguration Configuration,
    IReadOnlyList<ProductionPublishedProduct> Projects);

public sealed record ProductionIdentityDecision(
    string Code, string Detail, string Raw, string RuleId, int RuleRevision,
    string Serial, string Model, string Hardware, string Batch,
    string ModelBasis, ProductionPublishedProduct? Project)
{
    public bool Accepted => Code == "PRODUCTION_IDENTITY_RESOLVED" && Project is not null;
}
