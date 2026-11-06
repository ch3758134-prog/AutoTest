using System.Buffers.Binary;
using AutoTest.Contracts;

namespace AutoTest.Devices.Can;
public static class CanFrameCodec
{
    public const int PayloadLength = 15;
    public static byte[] ToUsbPayload(CanFrame frame, BusRoleMap map)
    {
        if (frame.Data.IsDefault || frame.Dlc != frame.Data.Length || frame.Dlc > 8 ||
            frame.Id > (frame.ExtendedId ? 0x1FFFFFFFu : 0x7FFu))
            throw new ArgumentException("Invalid classic CAN frame.", nameof(frame));
        ValidatePorts(map);
        int channel = frame.Role == BusRole.DutCan ? map.DutPort : map.KeypadPort;
        byte[] output = new byte[PayloadLength];
        output[0] = (byte)channel;
        output[1] = frame.ExtendedId ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(2), frame.Id);
        output[6] = frame.Dlc;
        frame.Data.AsSpan().CopyTo(output.AsSpan(7));
        return output;
    }
    public static bool FromUsbPayload(ReadOnlySpan<byte> input, BusRoleMap map, out CanFrame? frame)
    {
        frame = null;
        if (input.Length != PayloadLength || input[0] > 1 || input[1] > 1 || input[6] > 8 ||
            map.DutPort == map.KeypadPort || map.DutPort is < 0 or > 1 || map.KeypadPort is < 0 or > 1)
            return false;
        BusRole role = input[0] == map.DutPort ? BusRole.DutCan : BusRole.KeypadCan;
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(2, 4));
        if (id > (input[1] == 1 ? 0x1FFFFFFFu : 0x7FFu)) return false;
        // Firmware ASW_BusProxy_OnCanRx copies all eight MCAL data bytes, even if DLC < 8.
        // Tail bytes are not defined as zero; preserve all 15 raw bytes in CAN trace, but
        // publish only the first DLC bytes as the semantic CAN data payload.
        frame = CanFrame.Create(role, input[1] == 1, id, input.Slice(7, input[6]));
        return true;
    }
    private static void ValidatePorts(BusRoleMap map)
    {
        if (map.DutPort is < 0 or > 1 || map.KeypadPort is < 0 or > 1 || map.DutPort == map.KeypadPort)
            throw new InvalidOperationException("Distinct physical CAN ports 0 and 1 required.");
    }
}
