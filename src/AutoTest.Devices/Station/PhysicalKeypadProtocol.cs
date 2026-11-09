using System.Buffers.Binary;
using System.Globalization;

namespace AutoTest.Devices.Station;

/// <summary>KEY_SIM 20-resistor-switch network; NOT twenty DUT-facing buttons.
/// Main controller USB 0x40 forwards this frame over CAN0, standard ID 0x730, DLC8.</summary>
public static class PhysicalKeypadProtocol
{
    public const uint AllKeysMask = 0xFFFFF;
    public const uint KeyCanId = 0x730;

    public static byte[] EncodeUsbCanPayload(uint completeMask)
    {
        if (completeMask > AllKeysMask) throw new ArgumentOutOfRangeException(nameof(completeMask));
        var payload = new byte[15];
        payload[0] = 0; // CAN0: keypad network, NOT DUT CAN1
        payload[1] = 0; // 11-bit standard CAN frame
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2, 4), KeyCanId);
        payload[6] = 8; // DLC
        if (completeMask == 0) payload[7] = 0x80; // explicit release all
        else
        {
            payload[7] = 0x01;
            payload[8] = (byte)completeMask;
            payload[9] = (byte)(completeMask >> 8);
            payload[10] = (byte)(completeMask >> 16);
        }
        return payload;
    }

    public static bool TryDecodeExactKeyFrame(ReadOnlySpan<byte> frame, out uint mask)
    {
        mask = 0;
        if (frame.Length != 15 || frame[0] != 0 || frame[1] != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(2, 4)) != KeyCanId ||
            frame[6] != 8 || !frame.Slice(11, 4).SequenceEqual(new byte[4])) return false;
        if (frame[7] == 0x80) return frame.Slice(8, 3).SequenceEqual(new byte[3]);
        if (frame[7] != 0x01 || (frame[10] & 0xF0) != 0) return false;
        mask = (uint)(frame[8] | (frame[9] << 8) | (frame[10] << 16));
        return mask != 0;
    }

    public static bool TryParseCompleteMask(string? rawHex, out uint mask)
    {
        mask = 0;
        if (string.IsNullOrWhiteSpace(rawHex)) return false;
        string s = rawHex.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s.Length is >= 1 and <= 5 &&
               uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask) &&
               mask <= AllKeysMask;
    }

    public static string Confirmation(uint completeMask)
    {
        if (completeMask > AllKeysMask) throw new ArgumentOutOfRangeException(nameof(completeMask));
        return completeMask == 0 ? "AUTHORIZE KEY RELEASE ALL" : $"AUTHORIZE KEY MASK {completeMask:X5}";
    }
}
