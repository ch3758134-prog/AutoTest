
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;

/// <summary>DBC BO_/VAL_ IDs set bit31 for 29-bit CAN frames.
/// The actual CAN ID is the low 29 bits; never compare the flagged raw
/// DBC integer directly to an observed CAN frame.</summary>
public static class DbcFrameId
{
    public const uint ExtendedFlag = 0x80000000u;
    public const uint MaxExtended = 0x1FFFFFFFu;
    public const uint MaxStandard = 0x7FFu;
    public static (uint Id, bool Extended) Decode(uint raw)
    {
        if ((raw & 0x60000000u) != 0)
            throw new ProjectDataException("DBC_FRAME_ID", $"Unsupported DBC ID upper bits: 0x{raw:X8}");
        bool extended = (raw & ExtendedFlag) != 0;
        uint id = raw & MaxExtended;
        if (!extended && id > MaxStandard)
            throw new ProjectDataException("DBC_FRAME_ID", $"11-bit ID exceeds 0x7FF: 0x{id:X}");
        return (id,extended);
    }
    public static uint Encode(uint id, bool extended)
    {
        if (id > MaxExtended || (!extended && id > MaxStandard))
            throw new ProjectDataException("DBC_FRAME_ID", $"Invalid CAN ID/frame format: 0x{id:X}");
        return id | (extended ? ExtendedFlag : 0u);
    }
}
