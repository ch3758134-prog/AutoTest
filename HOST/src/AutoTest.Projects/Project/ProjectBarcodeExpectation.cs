using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace AutoTest.Projects.Project;

/// <summary>One complete barcode, ordered fields, explicit expected values.
/// The project declares applicability; barcode data cannot select individual resources.</summary>
public sealed record ProjectBarcodeField(string Name, string Purpose, string Expected = "");
public sealed record ProjectBarcodeMatch(bool Accepted, string Explanation,
    IReadOnlyDictionary<string, string> Fields);

public sealed record ProjectBarcodeExpectation(string Separator,
    ImmutableArray<ProjectBarcodeField> Fields)
{
    public const string Match = "Match", Identity = "Identity", Record = "Record";
    public const string Model = "ModelCode", Hardware = "HardwareCode", Serial = "DutSerial";

    public static ProjectBarcodeExpectation CreateDraft(string? model) => new("|",
        [new(Model, Match, model ?? ""), new(Hardware, Match), new(Serial, Identity),
         new("BatchCode", Record), new("ProductionDate", Record)]);

    public string ExpectedModel => Fields.First(x => x.Name == Model).Expected;
    public void Validate(string? model)
    {
        void Bad(string message) => throw new ProjectDataException("PROJECT_SCAN_EXPECTATION", message);
        if (Separator is null || Separator.Length != 1 ||
            Separator[0] is < '!' or > '~' || char.IsLetterOrDigit(Separator[0]))
            Bad("分隔符必须为一个可打印的非字母数字字符。");
        if (Fields.IsDefault || Fields.Length is < 3 or > 32)
            Bad("请配置 3～32 个有序字段。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) ||
                field.Name.Length > 64 || field.Name.Any(char.IsControl) ||
                !names.Add(field.Name))
                Bad("字段名称不能为空、重复或包含控制字符。");
            if (field!.Purpose is not (Match or Identity or Record))
                Bad("请选择字段用途：" + field.Name);
            if (field.Purpose == Match && (string.IsNullOrWhiteSpace(field.Expected) ||
                field.Expected.Any(c => c is < ' ' or > '~') ||
                field.Expected.Contains(Separator) || field.Expected != field.Expected.Trim()))
                Bad("精确匹配字段必须填写有效的期望内容：" + field.Name);
            if (field.Purpose != Match && !string.IsNullOrEmpty(field.Expected))
                Bad("仅匹配字段可以填写固定期望：" + field.Name);
        }
        if (Fields.Count(x => x.Purpose == Identity) != 1 ||
            !Fields.Any(x => x.Name == Serial && x.Purpose == Identity))
            Bad("必须且只能有一个 DutSerial 唯一身份字段。");
        if (!Fields.Any(x => x.Name == Model && x.Purpose == Match) ||
            !Fields.Any(x => x.Name == Hardware && x.Purpose == Match))
            Bad("产品型号 ModelCode、硬件版本 HardwareCode 必须是精确匹配字段。");
        if (!string.Equals(ExpectedModel, model, StringComparison.Ordinal))
            Bad("扫码期望型号必须与 ECU 工程型号一致。");
    }

    public ProjectBarcodeMatch Evaluate(string? raw, string? model)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        ProjectBarcodeMatch Reject(string reason) => new(false, reason, values);
        try { Validate(model); }
        catch (ProjectDataException error) { return Reject(error.Message); }
        if (string.IsNullOrEmpty(raw) || raw.Length > 512 ||
            raw.Any(c => c is < ' ' or > '~') || raw != raw.Trim())
            return Reject("条码为空、过长或含非法字符。");
        var segments = raw.Split(Separator, StringSplitOptions.None);
        if (segments.Length != Fields.Length)
            return Reject("期望 " + Fields.Length + " 个字段，实际收到 " + segments.Length + " 个。");
        var failures = new List<string>();
        for (var i = 0; i < Fields.Length; i++)
        {
            var field = Fields[i];
            var actual = segments[i];
            values.Add(field.Name, actual);
            if (string.IsNullOrWhiteSpace(actual) || actual != actual.Trim())
                failures.Add(field.Name + "：内容为空或包含首尾空白");
            else if (field.Purpose == Match && !string.Equals(actual, field.Expected, StringComparison.Ordinal))
                failures.Add(field.Name + "：期望 " + field.Expected + "，实际 " + actual);
            else if (field.Purpose == Identity && (actual.Length > 64 ||
                !Regex.IsMatch(actual, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
                failures.Add(field.Name + "：唯一编号格式不合法");
        }
        return failures.Count == 0
            ? new(true, "所有期望字段匹配，已提取唯一编号与追溯字段。", values)
            : Reject(string.Join("；", failures));
    }
}
