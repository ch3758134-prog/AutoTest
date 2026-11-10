using AutoTest.Contracts;

namespace AutoTest.Devices.Can;

/// <summary>
/// Exact KEY_SIM/WS/EPB_EOL KeySim_ProcessFrame 0x730 contract:
/// 20 resistor-network switch bits, NOT twenty voltage channels and NOT
/// voltage set-points. CAN0 carries these standard 8-byte data frames.
/// </summary>
public static class KeySimCanCodec
{
    public const uint CommandId = 0x730;
    public const uint MaxMask = 0xFFFFF;

    public static CanFrame SetMask(uint mask)
    {
        if (mask > MaxMask)
            throw new ArgumentOutOfRangeException(nameof(mask),
                "KEY_SIM mask is exactly 20 bits; high four bits must be zero.");
        if (mask == 0) return ReleaseAll();
        Span<byte> bytes = stackalloc byte[8];
        bytes[0] = 0x01;
        bytes[1] = (byte)mask;
        bytes[2] = (byte)(mask >> 8);
        bytes[3] = (byte)(mask >> 16);
        // Data[4..7] and Data[3] high nibble must be zero for active firmware.
        return CanFrame.Create(BusRole.KeypadCan, false, CommandId, bytes);
    }

    public static CanFrame ReleaseAll() =>
        CanFrame.Create(BusRole.KeypadCan, false, CommandId,
            new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0 });

    public static bool TryParse(CanFrame frame, out uint mask, out bool release)
    {
        mask = 0; release = false;
        if (frame.Role != BusRole.KeypadCan || frame.ExtendedId ||
            frame.Id != CommandId || frame.Dlc != 8 ||
            frame.Data.IsDefault || frame.Data.Length != 8) return false;
        var data = frame.Data.AsSpan();
        if (data[4] != 0 || data[5] != 0 || data[6] != 0 || data[7] != 0 ||
            (data[3] & 0xF0) != 0) return false;
        if (data[0] == 0x80)
        {
            if (data[1] != 0 || data[2] != 0 || data[3] != 0) return false;
            release = true; return true;
        }
        if (data[0] != 0x01) return false;
        mask = (uint)data[1] | ((uint)data[2] << 8) | ((uint)data[3] << 16);
        return mask != 0;
    }
}
