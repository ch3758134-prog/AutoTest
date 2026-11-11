using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTest.Desktop.Bench;

// Only DUT CAN1: KEY_SIM's CAN0 / 0x730 protocol never appears in this list.
public sealed record BenchCanMessage(string Id, string Name, uint ArbitrationId,
    bool Extended, string DataHex, int PeriodMs,
    string? DbcRevision = null, string? DbcMessageName = null)
{
    public byte[] Payload()
    {
        var compact = new string((DataHex ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (compact.Length > 16 || (compact.Length & 1) != 0)
            throw new ArgumentException("CAN Data 必须为 0～8 字节的成对十六进制字符。");
        try { return Convert.FromHexString(compact); }
        catch (FormatException) { throw new ArgumentException("CAN Data 包含非法十六进制字符。"); }
    }
    public void Validate()
    {
        if (!Guid.TryParse(Id, out var guid) || guid == Guid.Empty ||
            string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100 ||
            ArbitrationId > (Extended ? 0x1FFFFFFFu : 0x7FFu) ||
            PeriodMs is < 20 or > 60000)
            throw new ArgumentException("CAN 报文配置无效：请检查名称、ID、帧格式及周期（20～60000ms）。");
        if ((DbcRevision is null) != (DbcMessageName is null) ||
            (DbcRevision is not null &&
             (string.IsNullOrWhiteSpace(DbcRevision) || string.IsNullOrWhiteSpace(DbcMessageName))))
            throw new ArgumentException("DBC 绑定来源不完整，请重新从工程 DBC 添加报文。");
        _ = Payload();
    }
    public static void ValidateAll(IReadOnlyList<BenchCanMessage> messages)
    {
        if (messages is null || messages.Count > 200)
            throw new ArgumentException("CAN 报文配置超出允许范围。");
        foreach (var message in messages) message.Validate();
        if (messages.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ArgumentException("CAN 报文记录存在重复 ID。");
    }
}
public sealed record BenchCanPeriodicProgress(long AcknowledgedFrames, string Description);
