using System.Globalization;

namespace AutoTest.Devices.Station;

/// <summary>
/// Fully explicit, bounded raw 20-switch operation. No implied analogue voltage or ECU state.
/// Wire strings: Pulse:0x00001:300, Hold:0x00001, Hold:0x00001:1000, Release.
/// </summary>
public enum RawKeyOperation { Pulse, Hold, Release }
public sealed record RawKeyAction(RawKeyOperation Action, uint Mask, int DurationMs)
{
    public static RawKeyAction Parse(string specification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specification);
        var fields = specification.Split(':', StringSplitOptions.TrimEntries);
        if (!Enum.TryParse<RawKeyOperation>(fields[0], true, out var operation))
            throw new FormatException("原始 Key 操作必须是 Pulse、Hold 或 Release。");
        if (operation == RawKeyOperation.Release)
        {
            if (fields.Length != 1)
                throw new FormatException("Release 不允许附带 Key 或时间参数。");
            return new(operation, 0, 0);
        }
        if (fields.Length != (operation == RawKeyOperation.Pulse ? 3 : 2) &&
            !(operation == RawKeyOperation.Hold && fields.Length == 3))
            throw new FormatException("按键保持：Hold:0x00001:1000；短按：Pulse:0x00001:300。");
        var value = fields[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? fields[1][2..] : fields[1];
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask) ||
            mask > 0xFFFFFu || operation == RawKeyOperation.Pulse && mask == 0)
            throw new FormatException("KEY 组合必须在 0x00000～0xFFFFF；短按不能使用空组合。");
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
