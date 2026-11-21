using System.Text;
using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Optional, versioned barcode payload proposed by the R1-SCAN research.
/// Only parses externally issued labels; never generates or allocates serials.
/// The CRC detects typing corruption and does not authenticate the physical DUT.
/// </summary>
public static class Ut1BarcodeCodec
{
    private static readonly Regex Format = new(
        @"^UT1-(?<model>[A-Z0-9]{4,8})-(?<hardware>H[0-9]{2})-(?<serial>[A-Z0-9]{8,20})-(?<crc>[0-9A-F]{4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public sealed record Label(string ProductCode, string HardwareCode,
        string Serial, string Checksum);

    public static bool TryParse(string? text, out Label? parsed, out string code)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            code = "BARCODE_EMPTY";
            return false;
        }
        if (text.Length > 60 || !text.StartsWith("UT1-", StringComparison.Ordinal))
        {
            code = "BARCODE_FORMAT";
            return false;
        }
        var match = Format.Match(text);
        if (!match.Success)
        {
            code = "BARCODE_FORMAT";
            return false;
        }
        var prefix = text[..text.LastIndexOf('-')];
        var expected = ComputeChecksum(prefix).ToString("X4",
            System.Globalization.CultureInfo.InvariantCulture);
        var actual = match.Groups["crc"].Value;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            code = "BARCODE_CRC_MISMATCH";
            return false;
        }
        parsed = new(match.Groups["model"].Value, match.Groups["hardware"].Value,
            match.Groups["serial"].Value, actual);
        code = "BARCODE_UT1_OK";
        return true;
    }

    public static ushort ComputeChecksum(string payloadWithoutSuffix)
    {
        if (!payloadWithoutSuffix.All(c => c is >= ' ' and <= '~'))
            throw new ArgumentException("UT1 只能使用 ASCII 可打印字符。");
        ushort crc = 0xFFFF;
        foreach (byte b in Encoding.ASCII.GetBytes(payloadWithoutSuffix))
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (ushort)((crc & 0x8000) != 0
                    ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
