namespace AutoTest.UdsLab;

/// <summary>
/// Best-effort, evidence-based ECU diagnostic session observation.
/// It does not create an ECU session, run TesterPresent or infer session
/// type from USB connect/host CAN lease. Physical 0x10/0x3E is still blocked.
/// </summary>
public sealed class DiagnosticEcuSessionTracker
{
    private byte? _lastConfirmedSession;
    private DateTimeOffset? _confirmedAt;

    public string Description { get; private set; } =
        "ECU 会话：未知。连接 USB 不等于 ECU 诊断会话已确认。";

    public void Invalidate(string reason)
    {
        _lastConfirmedSession = null;
        _confirmedAt = null;
        Description = "ECU 会话：未知 / " + reason + "。";
    }

    public void ObservePositive(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.Length < 1 || response.Length < 1) return;
        if (request[0] == 0x10 && request.Length >= 2 && response.Length >= 2 &&
            response[0] == 0x50 && (request[1] & 0x7F) == (response[1] & 0x7F))
        {
            _lastConfirmedSession = (byte)(response[1] & 0x7F);
            _confirmedAt = DateTimeOffset.UtcNow;
            string sessionName = _lastConfirmedSession switch
            {
                0x01 => "默认",
                0x02 => "编程",
                0x03 => "扩展",
                _ => "ECU 自定义"
            };
            string timeInfo = "";
            if (response.Length >= 6)
            {
                int p2Ms = (response[2] << 8) | response[3];
                int p2StarMs = ((response[4] << 8) | response[5]) * 10;
                timeInfo = $" · ECU 报告 P2={p2Ms}ms/P2*={p2StarMs}ms";
            }
            Description = $"最近确认的会话：{sessionName} (0x{_lastConfirmedSession:X2}) · " +
                _confirmedAt.Value.ToLocalTime().ToString("HH:mm:ss") + timeInfo +
                "。未提供 S3/持续会话证据时不保证仍有效。";
        }
        else if (request[0] == 0x11 && response[0] == 0x51)
        {
            Invalidate("ECU 复位后旧会话不可确认");
        }
        else if (_lastConfirmedSession is null)
        {
            Description = "ECU 已返回诊断正响应；当前会话类型仍未确认。";
        }
        else
        {
            Description = "最近确认会话 0x" + _lastConfirmedSession.Value.ToString("X2") +
                "（" + _confirmedAt?.ToLocalTime().ToString("HH:mm:ss") +
                "）。后续通信成功不等于 S3 会话持续有效。";
        }
    }

    public void ObserveNegative(byte nrc)
    {
        // NRC can depend on current session but does not identify the session.
        Description = _lastConfirmedSession is null
            ? "ECU 返回 NRC 0x" + nrc.ToString("X2") + "；当前会话仍未知。"
            : "ECU 返回 NRC 0x" + nrc.ToString("X2") +
              "；最近确认的会话不代表当前依然有效。";
    }
}
