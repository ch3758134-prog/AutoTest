using System.Globalization;
using System.Text;

namespace AutoTest.Specialists.Uds;

/// <summary>
/// Pure diagnostic response summary. No hardware, no service execution and no
/// OEM-specific assumptions about multi-DID data sizes.
/// </summary>
public static class UdsDiagnosticResponseFormatter
{
    public static string Explain(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.IsEmpty) return "无请求字节，无法解析。";
        if (response.IsEmpty) return "没有 ECU UDS 响应 PDU。USB 主控 ACK 不等于 ECU 正响应。";
        byte sid = request[0];
        if (response[0] == 0x7F)
        {
            if (response.Length < 3) return "异常负响应：不足 3 字节，未能确认 NRC。";
            byte rejected = response[1], nrc = response[2];
            string name = nrc switch
            {
                0x10 => "GeneralReject",
                0x11 => "ServiceNotSupported",
                0x12 => "SubFunctionNotSupported",
                0x13 => "IncorrectMessageLengthOrInvalidFormat",
                0x21 => "BusyRepeatRequest",
                0x22 => "ConditionsNotCorrect",
                0x24 => "RequestSequenceError",
                0x31 => "RequestOutOfRange",
                0x33 => "SecurityAccessDenied",
                0x35 => "InvalidKey",
                0x36 => "ExceededNumberOfAttempts",
                0x37 => "RequiredTimeDelayNotExpired",
                0x78 => "ResponsePending（等待最终响应）",
                0x7E => "SubFunctionNotSupportedInActiveSession",
                0x7F => "ServiceNotSupportedInActiveSession",
                _ => "OEM/未知 NRC"
            };
            return $"ECU 负响应：原始 SID=0x{rejected:X2}，NRC=0x{nrc:X2}（{name}）；" +
                (rejected == sid ? "关联当前请求。" : "回报的请求 SID 与本次请求不一致，请检查会话/总线。");
        }
        if (response[0] != (byte)(sid + 0x40))
            return $"响应服务 ID 不匹配：请求 0x{sid:X2}，收到 0x{response[0]:X2}；先查看原始 Trace，不能标为成功。";

        if (sid == 0x22 && request.Length == 3 && response.Length >= 3)
        {
            ushort asked = (ushort)((request[1] << 8) | request[2]);
            ushort returned = (ushort)((response[1] << 8) | response[2]);
            if (asked != returned)
                return $"ReadDID 应答 DID 不匹配：请求 0x{asked:X4}，返回 0x{returned:X4}，不能标为有效读取。";
            var payload = response[3..];
            string ascii = payload.Length > 0 && payload.Length <= 128 &&
                           payload.ToArray().All(x => x is >= 0x20 and <= 0x7E)
                ? $"；可打印 ASCII=\"{Encoding.ASCII.GetString(payload)}\"" : "";
            return $"ECU 已返回 ReadDataByIdentifier 正响应 0x62，DID=0x{asked:X4}，" +
                $"数据长度={payload.Length} 字节，数据={Convert.ToHexString(payload)}{ascii}。" +
                "未装载 ODX/ECU 定义时不猜测物理量单位或 DID 含义。";
        }
        if (sid == 0x19 && request.Length >= 2 && response.Length >= 3 &&
            request[1] == 0x02 && response[1] == 0x02)
        {
            int data = response.Length - 3;
            if (data % 4 != 0)
                return "DTC ReportByStatusMask 响应长度不是 4 字节记录的整数倍，请核对原始数据。";
            var lines = new List<string>
            {
                $"ECU DTC 报告：状态可用掩码 0x{response[2]:X2}，记录数量 {data / 4}。"
            };
            for (int i = 3; i < response.Length && lines.Count <= 20; i += 4)
                lines.Add($"DTC 0x{response[i]:X2}{response[i+1]:X2}{response[i+2]:X2}，状态 0x{response[i+3]:X2}");
            if (data / 4 > 20) lines.Add("仅显示前 20 条，完整内容查看原始 PDU。");
            return string.Join(Environment.NewLine, lines);
        }

        return $"收到 UDS 正响应 SID=0x{response[0]:X2}，长度={response.Length}；" +
            "此项尚无车型专用解析，原始响应见下方。";
    }
}
