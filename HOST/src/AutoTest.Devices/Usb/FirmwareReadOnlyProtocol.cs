using System.Text;

namespace AutoTest.Devices.Usb;

/// <summary>Source checked against EPB_Test_Keil/Project/source/BSW/CDD/USB/CDD_USB.h
/// and ASW/UsbSvc/ASW_UsbSvc.c. These are MCU pin reads, NOT electrical measurements.</summary>
public static class FirmwareReadOnlyProtocol
{
    public const byte Ping = 0xF0, GetVersion = 0xF1;
    public const byte GetEcuPower = 0x11, GetIgn = 0x13;
    public const byte GetKeyPower = 0x15, GetSolenoid = 0x17, GetAllPower = 0x1F;
    public const byte CanRxIndication = 0x41;
    public const byte CanTransmit = 0x40;
    public const byte SetEcuPower = 0x10, SetIgn = 0x12, SetKeyPower = 0x14, SetSolenoid = 0x16;
    public static string Explain(byte command, byte[] bytes)
    {
        if (command == Ping)
            return bytes.AsSpan().SequenceEqual("PONG"u8) ? "PONG" : "PING_PAYLOAD_MISMATCH";
        if (command == GetVersion)
        {
            string text = Encoding.ASCII.GetString(bytes);
            return text == MainControllerUsbIdentity.FirmwareVersion ? text : "VERSION_MISMATCH: " + text;
        }
        if (bytes.Length != 1) return $"INVALID_LENGTH: {bytes.Length}, expected 1";
        return command switch
        {
            GetEcuPower => $"ECU_PWR GPIO={(bytes[0] != 0 ? "HIGH" : "LOW")}; not a measured DUT supply",
            GetIgn => $"IGN GPIO={(bytes[0] != 0 ? "HIGH" : "LOW")}; not a measured IGN voltage",
            GetKeyPower => $"KEY_PWR GPIO={(bytes[0] != 0 ? "HIGH" : "LOW")}; not a measured supply",
            GetSolenoid => $"SOL firmware action={bytes[0]} (0=up,1=down); physical fixture state unverified",
            GetAllPower => $"ECU_PWR={(bytes[0] & 1) != 0}, IGN={(bytes[0] & 2) != 0}, KEY_PWR={(bytes[0] & 4) != 0}; GPIO only",
            _ => "UNKNOWN_READBACK"
        };
    }
    public static bool ValidReadOnlyPayload(byte command, ReadOnlySpan<byte> payload)
    {
        if (command == Ping) return payload.SequenceEqual("PONG"u8);
        if (command == GetVersion) return Encoding.ASCII.GetString(payload) == MainControllerUsbIdentity.FirmwareVersion;
        if (command is GetEcuPower or GetIgn or GetKeyPower or GetSolenoid or GetAllPower)
            return payload.Length == 1 && (command == GetAllPower ? (payload[0] & 0xF8) == 0 : payload[0] <= 1);
        return false;
    }
}
