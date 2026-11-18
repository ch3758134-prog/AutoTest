using System;

namespace EPB_UsbTest;

/// <summary>
/// 帧协议（与固件一致，线契约不变）:
///   请求 : A5 | SEQ(2 LE) | CMD | LEN | PAYLOAD | CRC16(2 LE) | 5A   → 8 + LEN 字节
///   响应 : A5 | SEQ(2 LE) | CMD | STATUS | LEN | PAYLOAD | CRC16(2) | 5A → 9 + LEN 字节
/// CRC 覆盖 SEQ..PAYLOAD（不含 SOF/EOF），MODBUS CRC-16，低字节在前。
/// </summary>
public static class Frame
{
    public const byte SOF = 0xA5;
    public const byte EOF = 0x5A;

    public const byte CMD_PING = 0xF0;
    public const byte CMD_GET_VERSION = 0xF1;

    /* 旧供电常量只供历史 JSON 兼容读取；实际 USB 发送已在 UsbLink 阻断。 */
    public const byte CMD_SET_ECU_PWR = 0x10;   // {u8 on}，on=1 时 RL_DRV(主继电器)+ECU_PWR 同开
    public const byte CMD_GET_ECU_PWR = 0x11;   // → {u8}
    public const byte CMD_SET_IGN = 0x12;       // {u8 on}
    public const byte CMD_GET_IGN = 0x13;       // → {u8}
    public const byte CMD_SET_KEYPWR = 0x14;    // {u8 on}
    public const byte CMD_GET_KEYPWR = 0x15;    // → {u8}
    public const byte CMD_GET_ALL_PWR = 0x1F;   // 已废止，禁止发送
    // 当前 Main_Control USB v0.2：两只夹具独立设定，不使用旧 SOL 命令。
    public const byte CMD_SET_CLAMP = 0x18; // payload = [clamp(1/2), state(0/1)]
    public const byte CMD_GET_CLAMPS = 0x19; // 响应 byte0: bit0/bit1 MCU GPIO 读回
    public const string MAIN_CONTROL_V04_VERSION = "EPB_TEST_CTRL v0.4-dev";

    // 旧 ECU/KEY 电源、SOL 和综合电源查询都不能用于现役主控。
    public static bool IsObsoleteControlCommand(byte cmd) =>
        cmd is 0x10 or 0x11 or 0x14 or 0x15 or 0x16 or 0x17 or 0x1F;

    public static byte[] ClampPayload(byte clamp, bool press)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        return new[] { clamp, press ? (byte)1 : (byte)0 };
    }

    /* CAN 发送（0x40，channel：0=CAN0 按键板 / 1=CAN1 ECU） */
    public const byte CMD_CAN_SEND = 0x40;      // {channel, ide, id(4LE), dlc, data[8]} = 15B
    /* CAN 收到帧主动回传（0x41，SEQ=0 不配对，上位机订阅消费） */
    public const byte CMD_CAN_RX_IND = 0x41;    // {channel, ide, id(4LE), dlc, data[8]} = 15B

    /* 整车环境广播（0x50~0x53，经 CAN1 → ECU） */
    public const byte CMD_SET_BROADCAST = 0x50;     // {count, frame×N} frame={ide,id(4LE),period_ms(2LE),dlc,data[8]}=16B
    public const byte CMD_BROADCAST_START = 0x51;
    public const byte CMD_BROADCAST_STOP = 0x52;
    public const byte CMD_BROADCAST_STATUS = 0x53;  // → {u8 running, u8 count}
    public const int MAX_BC_FRAMES = 8;             // 固件上限，超限返回 ERR_FMT

    public const byte STS_OK = 0x00;
    public const byte STS_ERR_FMT = 0x02;
    public const byte STS_ERR_CMD = 0x03;
    public const byte STS_ERR_BUSY = 0x05;
    public const byte STS_ERR_HW = 0x06;

    /// <summary>STATUS 字节 → 中文描述（日志用）。</summary>
    public static string StatusName(byte status) => status switch
    {
        STS_OK => "OK",
        STS_ERR_FMT => "ERR_FMT 格式错误",
        STS_ERR_CMD => "ERR_CMD 未知指令",
        STS_ERR_BUSY => "ERR_BUSY 忙",
        STS_ERR_HW => "ERR_HW 硬件错误",
        _ => $"0x{status:X2}",
    };

    /// <summary>供电指令 payload：{on}。</summary>
    public static byte[] PowerPayload(bool on) => new[] { on ? (byte)1 : (byte)0 };

    /// <summary>CAN_SEND payload：{channel, ide, id(4LE), dlc, data[8]} = 15B。</summary>
    public static byte[] CanSendPayload(byte channel, bool ide, uint id, byte dlc, byte[] data)
    {
        if (channel > 1) throw new ArgumentOutOfRangeException(nameof(channel), "channel 只能是 0/1");
        if (dlc > 8 || data.Length > 8) throw new ArgumentException("dlc/data 超限（最大 8）");
        var p = new byte[15];
        p[0] = channel;
        p[1] = ide ? (byte)1 : (byte)0;
        p[2] = (byte)id;
        p[3] = (byte)(id >> 8);
        p[4] = (byte)(id >> 16);
        p[5] = (byte)(id >> 24);
        p[6] = dlc;
        for (int i = 0; i < 8; i++) p[7 + i] = i < data.Length ? data[i] : (byte)0;
        return p;
    }

    /// <summary>模拟按键板完整输出状态：标准帧 0x730，经 CAN0 透传。</summary>
    public static byte[] KeypadOutputPayload(uint outputMask)
    {
        if ((outputMask & ~0xFFFFFu) != 0) throw new ArgumentOutOfRangeException(nameof(outputMask), "按键输出状态只能使用 20 bit");
        var data = new byte[8];
        data[0] = 0x01;
        data[1] = (byte)outputMask;
        data[2] = (byte)(outputMask >> 8);
        data[3] = (byte)((outputMask >> 16) & 0x0F);
        return CanSendPayload(0, false, 0x730, 8, data);
    }

    /// <summary>停止模拟按键板当前输出并恢复全部断开。</summary>
    public static byte[] KeypadResetPayload() => CanSendPayload(0, false, 0x730, 8, new byte[8] { 0x80, 0, 0, 0, 0, 0, 0, 0 });

    /// <summary>SET_BROADCAST payload：{count, frame×N}，frame={ide,id(4LE),period_ms(2LE),dlc,data[8]}=16B。</summary>
    public static byte[] SetBroadcastPayload(IReadOnlyList<(byte ide, uint id, ushort periodMs, byte dlc, byte[] data)> frames)
    {
        if (frames.Count == 0 || frames.Count > 8)
            throw new ArgumentException("帧数必须在 1~8 之间");
        var p = new byte[1 + frames.Count * 16];
        p[0] = (byte)frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            int o = 1 + i * 16;
            p[o] = f.ide;
            p[o + 1] = (byte)f.id;
            p[o + 2] = (byte)(f.id >> 8);
            p[o + 3] = (byte)(f.id >> 16);
            p[o + 4] = (byte)(f.id >> 24);
            p[o + 5] = (byte)f.periodMs;
            p[o + 6] = (byte)(f.periodMs >> 8);
            p[o + 7] = f.dlc;
            for (int k = 0; k < 8; k++) p[o + 8 + k] = k < f.data.Length ? f.data[k] : (byte)0;
        }
        return p;
    }

    /// <summary>解析 0x41 CAN_RX_IND payload：{channel, ide, id(4LE), dlc, data[8]} = 15B。
    /// 与 0x40 CAN_SEND 同构，对齐固件 EAL_CAN 帧模型（带 ide）。</summary>
    public static bool TryParseCanRxInd(byte[] payload, out CanFrame frame)
    {
        frame = default;
        if (payload.Length < 15) return false;
        frame = new CanFrame(
            Channel: payload[0],
            Ide: payload[1] != 0,
            Id: (uint)(payload[2] | (payload[3] << 8) | (payload[4] << 16) | (payload[5] << 24)),
            Dlc: Math.Min(payload[6], (byte)8),
            Data: payload[7..15]);
        return true;
    }

    /// <summary>组一帧请求。</summary>
    public static byte[] MakeRequest(ushort seq, byte cmd, byte[] payload)
    {
        if (payload.Length > 255) throw new ArgumentException($"payload 超限（帧 LEN 为单字节，最大 255），实际 {payload.Length}");
        int len = payload.Length;
        var f = new byte[8 + len];
        f[0] = SOF;
        f[1] = (byte)seq;
        f[2] = (byte)(seq >> 8);
        f[3] = cmd;
        f[4] = (byte)len;
        if (len > 0) Array.Copy(payload, 0, f, 5, len);
        ushort crc = Crc16.Compute(f, 1, 4 + len);   // SEQ..PAYLOAD
        f[5 + len] = (byte)crc;
        f[6 + len] = (byte)(crc >> 8);
        f[7 + len] = EOF;
        return f;
    }
}

/// <summary>一条收到的 CAN 帧（0x41 上报）。</summary>
public readonly record struct CanFrame(byte Channel, bool Ide, uint Id, byte Dlc, byte[] Data)
{
    /// <summary>读线程打的时间戳（Environment.TickCount64，ms，单调）：帧到达 USB 的时刻，用于帧间隔测量。
    /// 不打在 UI 线程——UI 批处理会把同一批帧的处理时间压缩，间隔失真（实测 100ms 帧读成 10ms）。</summary>
    public long TimeMs { get; init; }

    public string IdText => Ide ? $"0x{Id:X8}" : $"0x{Id:X3}";
    /// <summary>数据按字节带位置标签：B0:5A B1:01 …——一眼看出是第几位字节（对齐 obsItems 的 byteIndex）。
    /// 结构体内 lambda 不能访问 this 成员，先把 Dlc/Data 复制到局部变量。</summary>
    public string DataText
    {
        get
        {
            var dlc = Dlc;
            var data = Data;
            return string.Join(" ", Enumerable.Range(0, dlc).Select(i => $"B{i}:{data[i]:X2}"));
        }
    }
}

/// <summary>MODBUS CRC-16（poly 0x8005，init 0xFFFF，输出低字节在前）。</summary>
public static class Crc16
{
    public static ushort Compute(byte[] buf, int offset, int count)
        => Compute((System.Collections.Generic.IList<byte>)buf, offset, count);

    public static ushort Compute(System.Collections.Generic.IList<byte> buf, int offset, int count)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < count; i++)
        {
            crc ^= buf[offset + i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }
}
