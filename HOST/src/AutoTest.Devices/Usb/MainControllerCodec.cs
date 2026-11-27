namespace AutoTest.Devices.Usb;
public static class MainControllerCodec
{
    // Main_Control docs/interfaces/USB_HOST_CONTROL_INTERFACE.md v0.2.
    public const byte Ping = 0xF0, GetVersion = 0xF1;
    public const byte CanSend = 0x40, CanRxIndication = 0x41;
    // Main_Control/Main_Control branch, 2026-11-20: SET [baud code 0..7],
    // GET => [baud code]. ECU_CAN_PORT only; KEY CAN remains fixed 500k.
    public const byte SetEcuCanBaud = 0x42, GetEcuCanBaud = 0x43;
    // Three external, debounced TestCtr buttons on PTC7/6/5, NOT KEY_SIM K1..K20.
    public const byte ExternalKeyEvent = 0x44, GetExternalKeyInputs = 0x45;

    public static ExternalKeyInputEvent ParseExternalKeyEvent(UsbResponse response)
    {
        if (response.Sequence != 0 || response.Command != ExternalKeyEvent ||
            response.Status != 0 || response.Payload.Length != 5)
            throw new InvalidDataException("主控外部 KEY 0x44 事件帧格式错误。");
        var data = response.Payload;
        if (data[0] is < 1 or > 3 || data[1] > 1 || (data[2] & 0xF8) != 0 ||
            ((data[2] >> (data[0] - 1)) & 1) != data[1])
            throw new InvalidDataException("主控外部 KEY 0x44 编号或 GPIO 电平不合法。");
        return new ExternalKeyInputEvent(data[0], data[1], data[2],
            (ushort)(data[3] | (data[4] << 8)), DateTimeOffset.UtcNow);
    }

    public static async Task<ExternalKeyInputSnapshot> ReadExternalKeyInputsAsync(
        UsbSessionService usb, CancellationToken ct = default)
    {
        var response = await usb.RequestAsync(GetExternalKeyInputs,
            ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        if (response.Payload.Length != 3 || (response.Payload[0] & 0xF8) != 0)
            throw new InvalidDataException("主控 0x45 外部 KEY 电平快照必须为合法的 3 字节。");
        return new ExternalKeyInputSnapshot(response.Payload[0],
            (ushort)(response.Payload[1] | (response.Payload[2] << 8)));
    }

    public static byte EcuCanBaudCode(int kbps) => kbps switch
    {
        20 => 0, 50 => 1, 100 => 2, 125 => 3,
        250 => 4, 500 => 5, 800 => 6, 1000 => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(kbps),
            "固件 ECU CAN 仅支持 20/50/100/125/250/500/800/1000 kbit/s")
    };

    public static int EcuCanBaudKbps(byte code) => code switch
    {
        0 => 20, 1 => 50, 2 => 100, 3 => 125,
        4 => 250, 5 => 500, 6 => 800, 7 => 1000,
        _ => throw new InvalidDataException("主控 ECU CAN 波特率回读代码无效: " + code)
    };

    public static int ParseEcuCanBaud(string raw)
    {
        var text = raw.Trim().ToUpperInvariant().Replace(" ", "");
        if (text.EndsWith("KBPS", StringComparison.Ordinal)) text = text[..^4];
        else if (text.EndsWith("K", StringComparison.Ordinal)) text = text[..^1];
        else if (text.EndsWith("BIT/S", StringComparison.Ordinal)) text = text[..^5];
        if (!int.TryParse(text, out var number))
            throw new FormatException("非法 ECU CAN 码率: " + raw);
        return number >= 20000 && number % 1000 == 0 ? number / 1000 : number;
    }

    public static async Task<int> ReadEcuCanBaudAsync(
        UsbSessionService usb, CancellationToken ct = default)
    {
        var response = await usb.RequestAsync(GetEcuCanBaud,
            ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        if (response.Payload.Length != 1)
            throw new InvalidDataException("ECU CAN 0x43 应返回唯一 1 字节速率代码。");
        return EcuCanBaudKbps(response.Payload[0]);
    }

    public static async Task<int> SetAndVerifyEcuCanBaudAsync(
        UsbSessionService usb, int kbps, CancellationToken ct = default)
    {
        byte code = EcuCanBaudCode(kbps);
        var ack = await usb.RequestAsync(SetEcuCanBaud,
            new byte[] { code }, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        if (ack.Payload.Length != 0)
            throw new InvalidDataException("ECU CAN 0x42 设置成功应答必须为空。");
        var actual = await ReadEcuCanBaudAsync(usb, ct).ConfigureAwait(false);
        if (actual != kbps)
            throw new IOException("ECU CAN SET 0x42 已 ACK，但 GET 0x43 读回 " +
                actual + " kbit/s，不等于目标 " + kbps + " kbit/s。");
        return actual;
    }
    public const byte SetIgn = 0x12, GetIgn = 0x13;
    public const byte SetClamp = 0x18, GetClamps = 0x19;
    // Main_Control / ASW_IoCtrl: six individual high-side LED outputs.
    public const byte SetLed = 0x1A, GetLeds = 0x1B, SetLeds = 0x1C;
    public const string ClampContractVersion = "EPB_TEST_CTRL v0.4-dev";
    public static byte[] EncodePing(ushort sequence) => UsbWireCodec.EncodeRequest(sequence,Ping,ReadOnlySpan<byte>.Empty);
    public static byte[] EncodeCan(ushort sequence,ReadOnlySpan<byte> payload)
    {
        if(payload.Length!=15) throw new ArgumentException("CAN USB payload must contain 15 bytes.",nameof(payload));
        return UsbWireCodec.EncodeRequest(sequence,CanSend,payload);
    }
    public static byte[] EncodeIgn(ushort sequence,bool on) => UsbWireCodec.EncodeRequest(sequence,SetIgn,new byte[]{on?(byte)1:(byte)0});
    public static byte[] EncodeClamp(ushort sequence, byte clamp, bool press)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        return UsbWireCodec.EncodeRequest(sequence, SetClamp, new byte[] { clamp, press ? (byte)1 : (byte)0 });
    }
    public static byte[] EncodeLed(ushort sequence, byte index, bool on)
    {
        if (index is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(index));
        return UsbWireCodec.EncodeRequest(sequence, SetLed, new byte[] { index, on ? (byte)1 : (byte)0 });
    }
    // GET_LEDS returns 1 byte (bits 0..5). GPIO readback is NOT physical output voltage.
    // RL_DRV is internal hardware setup and must NEVER be exposed as a USB command.
    // GPIO readback is not a physical clamp-position or valve-voltage measurement.
}

    
/// <summary>Authenticated, CRC-checked 0x44 external KEY input; level polarity not calibrated.</summary>
public readonly record struct ExternalKeyInputEvent(
    byte Key, byte Level, byte Levels, ushort Sequence, DateTimeOffset ReceivedUtc);

/// <summary>Read-only 0x45 debounced levels and the MCU change counter.</summary>
public readonly record struct ExternalKeyInputSnapshot(byte Levels, ushort Sequence);
