using System.Globalization;

namespace AutoTest.Devices.Station;

/// <summary>
/// Fully explicit, bounded raw 20-switch operation. No implied analogue voltage or ECU state.
/// Wire strings: Hold:0x00001:1000 (no implicit release), Release,
/// Wait:1000 (no CAN transmission). Pulse is an explicit legacy press/release.
/// </summary>
public enum RawKeyOperation { Pulse, Hold, Release, Wait }
public sealed record RawKeyAction(RawKeyOperation Action, uint Mask, int DurationMs)
{
    public static RawKeyAction Parse(string specification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specification);
        var fields = specification.Split(':', StringSplitOptions.TrimEntries);
        if (!Enum.TryParse<RawKeyOperation>(fields[0], true, out var operation))
            throw new FormatException("原始 Key 操作必须是 Hold、Release、Wait 或明确短按 Pulse。");
        if (operation == RawKeyOperation.Release)
        {
            if (fields.Length != 1)
                throw new FormatException("Release 不允许附带 Key 或时间参数。");
            return new(operation, 0, 0);
        }
        if (operation == RawKeyOperation.Wait)
        {
            if (fields.Length != 2 ||
                !int.TryParse(fields[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var wait) ||
                wait is < 1 or > 600000)
                throw new FormatException("纯等待格式为 Wait:1000（1～600000 毫秒），不发送 KEY CAN。");
            return new(operation, 0, wait);
        }
        if (fields.Length != (operation == RawKeyOperation.Pulse ? 3 : 2) &&
            !(operation == RawKeyOperation.Hold && fields.Length == 3))
            throw new FormatException("按键保持：Hold:0x00001:1000；纯等待：Wait:1000；释放：Release。");
        var value = fields[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? fields[1][2..] : fields[1];
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask) ||
            mask > 0xFFFFFu || operation == RawKeyOperation.Pulse && mask == 0)
            throw new FormatException("KEY 组合必须在 0x00000～0xFFFFF；短按不能使用空组合。");
        if (operation == RawKeyOperation.Hold && mask == 0 && fields.Length != 3)
            throw new FormatException("零掩码是全部断开；纯等待请使用 Wait:1000。");
        var duration = 0;
        if (fields.Length == 3 &&
            (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out duration) ||
             duration is < 1 or > 600000))
            throw new FormatException("按键持续时间必须在 1～600000 毫秒之间。");
        if (operation == RawKeyOperation.Pulse && duration > 60000)
            throw new FormatException("短按最长 60000 毫秒。");
        return new(operation, mask, duration);
    }
}
