using System.IO;
using System.Text.RegularExpressions;

namespace AutoTest.Application.Production;

/// <summary>
/// One station barcode is the complete DUT identity. A fixed, leading marker
/// before the '-' delimiter selects one installed project package.
/// Do not derive a separate serial, model, fixture or test recipe from it.
/// </summary>
public sealed record FixedMarkerBarcode(string FullBarcode, string Marker)
{
    public const char Separator = '-';
    private static readonly Regex MarkerPattern = new(
        @"\A[A-Z0-9_]{2,32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnitPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,95}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeMarker(string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker) || marker != marker.Trim())
            throw new InvalidDataException("识别标记不能为空或包含空格。");
        var value = marker.ToUpperInvariant();
        if (!MarkerPattern.IsMatch(value))
            throw new InvalidDataException(
                "识别标记应为 2～32 位字母、数字或下划线，不包含分隔符 '-'。");
        return value;
    }

    public static bool TryParse(string? raw, out FixedMarkerBarcode? barcode,
        out string reason)
    {
        barcode = null;
        reason = "";
        // Incoming COM framing has already removed CR/LF. Never silently
        // trim or modify physical barcode identity.
        if (string.IsNullOrEmpty(raw) || raw.Length > 129 ||
            raw.Any(char.IsControl) || raw != raw.Trim())
        {
            reason = "扫码编号为空、过长或包含空格/不可见字符。";
            return false;
        }
        var index = raw.IndexOf(Separator);
        if (index < 2 || index == raw.Length - 1)
        {
            reason = "编号应以固定识别标记和 '-' 开头，后面保留每台 ECU 的完整编号。";
            return false;
        }
        var marker = raw[..index].ToUpperInvariant();
        if (!MarkerPattern.IsMatch(marker) ||
            !UnitPattern.IsMatch(raw[(index + 1)..]))
        {
            reason = "编号格式不符合工位统一编码规范。";
            return false;
        }
        barcode = new(raw, marker);
        return true;
    }
}