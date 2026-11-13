using System.Globalization;

namespace AutoTest.Devices.Station;

/// <summary>
/// Fully explicit, bounded raw 20-switch operation. No implied analogue voltage or ECU state.
/// Wire strings: Pulse:0x00001:300, Hold:0x00001, Release.
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
        if (fields.Length != (operation == RawKeyOperation.Pulse ? 3 : 2))
            throw new FormatException("按键保持：Hold:0x00001；短按：Pulse:0x00001:300。");
        var value = fields[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? fields[1][2..] : fields[1];
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask) ||
            mask is 0 or > 0xFFFFFu)
            throw new FormatException("KeyMask 必须为非零 20 位 HEX 组合。");
        var duration = 0;
        if (operation == RawKeyOperation.Pulse &&
            (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out duration) ||
             duration is < 1 or > 60000))
            throw new FormatException("短按保持时间必须在 1～60000 毫秒之间。");
        return new(operation, mask, duration);
    }
}
