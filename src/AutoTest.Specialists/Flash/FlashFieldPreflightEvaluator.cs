using System.Collections.Immutable;

namespace AutoTest.Specialists.Flash;

/// <summary>
/// Conservative non-destructive evidence checklist. A successful controller
/// connection, matching XML, or one UDS DID response is never equivalent to
/// physical programming authorization.
/// </summary>
public sealed record FlashPreflightItem(string Category, string State, string Evidence)
{
    public bool Passed => State == "PASS";
}
public sealed record FlashFieldEvidence(bool UsbConnected, bool ControllerVersionVerified,
    long UsbGeneration, bool LiveUdsReadAccepted, bool LiveUdsEndpointMatches,
    bool CanBitrateVerified, bool EcuIdentityMatched, bool MeasuredPowerInRange,
    bool SecurityProviderValidated, bool OemProgrammingContractValidated,
    bool RecoveryProcedureValidated);
public sealed record FlashFieldPreflight(ImmutableArray<FlashPreflightItem> Items)
{
    public bool ReadOnlyCommunicationProven =>
        Items.Any(x => x.Category == "UDS_READONLY" && x.Passed);
    public bool PhysicalProgrammingAllowed => false; // physical writes are not implemented
}
public static class FlashFieldPreflightEvaluator
{
    public static FlashFieldPreflight Evaluate(CanToolConfigurationSnapshot? profile,
        FirmwareImage? image, FlashFieldEvidence evidence)
    {
        var list = ImmutableArray.CreateBuilder<FlashPreflightItem>();
        void Check(string category, bool good, string ok, string missing) =>
            list.Add(new(category, good ? "PASS" : "BLOCKED", good ? ok : missing));
        Check("USB", evidence.UsbConnected && evidence.ControllerVersionVerified &&
            evidence.UsbGeneration > 0, $"主控会话代次 {evidence.UsbGeneration}；版本已核验",
            "需要当前 WinUSB 连接与固件版本证据");
        Check("PROFILE", profile is not null,
            profile is null ? "" : $"仅样本配置：{profile.SourceName} SHA256={profile.SourceSha256}",
            "未导入真实 Configuration.xml；不得使用示例 CAN ID 猜测目标 ECU");
        bool validImage=false;
        if (image is not null)
        {
            try { FirmwareImageParser.ValidateSegments(image.Segments); validImage=true; }
            catch (Exception ex) when (ex is ArgumentException or FirmwareFormatException)
            { validImage=false; }
        }
        Check("FIRMWARE", validImage, image is null ? "" :
            $"{image.ByteCount:N0} 字节，{image.Segments.Length} 个已解析段",
            "未加载或无法验证固件地址段");
        Check("FLASH_MAP", profile is not null && validImage && profile.ImageFits(image!),
            "全部段位于当前 XML 的地址范围（不等于 ECU 实际许可）",
            "固件地址范围未验证或超出 XML Flash 区间");
        Check("UDS_READONLY", evidence.LiveUdsReadAccepted && evidence.LiveUdsEndpointMatches &&
            evidence.UsbConnected && evidence.ControllerVersionVerified,
            "真实 ECU 对匹配的 CAN1 端点返回有效只读 UDS 应答",
            "尚未在当前 USB 连接代次及 APP/Boot 配置端点确认 ECU 的 UDS 应答");
        Check("CAN_BITRATE", evidence.CanBitrateVerified,
            "当前主控有效 CAN1 速率已有独立验证",
            "XML 波特率仅是期望配置，未证明物理 CAN1 实际使用该值");
        Check("ECU_IDENTITY", evidence.EcuIdentityMatched,
            "ECU 硬件/软件身份与已批准刷写 Profile 一致",
            "缺少目标 ECU 身份与可刷固件 Revision 的独立匹配");
        Check("POWER_IGN", evidence.MeasuredPowerInRange,
            "实际电压/IGN 和台架供电条件确认",
            "未证明持续供电、目标电压及 IGN 条件");
        Check("SECURITY_DLL", evidence.SecurityProviderValidated,
            "Seed/Key 提供程序来源、ABI、ECU 兼容性已经批准",
            "SafetyFile.dll 无原生 ZLGKey 导出；演示算法不能当成 ECU 解锁能力");
        Check("OEM_SEQUENCE", evidence.OemProgrammingContractValidated,
            "OEM 编程会话、擦除 RID/参数、0x34、校验与复位已逐项验证",
            "CanTool 参考流程和当前 ECU 的 OEM 编程合同未闭环");
        Check("RECOVERY", evidence.RecoveryProcedureValidated,
            "故障中止、恢复及最终 APP 复验有实测方案",
            "擦除失败/断电恢复与最终身份验收条件未获验证");
        list.Add(new("PHYSICAL_WRITE", "BLOCKED",
            "FieldStudio 当前没有物理 Flash 发送/擦除权限；即使其他条件全部通过也不能解除此锁。"));
        return new(list.ToImmutable());
    }
}

/// <summary>
/// ISO 14229-1 positive 0x74 response: upper nibble gives the count of bytes
/// encoding maxNumberOfBlockLength. Value is the whole 0x36 request, not payload.
/// </summary>
public static class UdsDownloadResponseParser
{
    public static uint MaxTransferRequestBytes(ReadOnlySpan<byte> response)
    {
        if (response.Length < 3 || response[0] != 0x74 ||
            (response[1] & 0x0F) != 0)
            throw new FormatException("Invalid 0x74 length-format identifier.");
        int count = response[1] >> 4;
        if (count is < 1 or > 4 || response.Length != count + 2)
            throw new FormatException("Invalid 0x74 maxNumberOfBlockLength length.");
        uint max=0;
        for (int i=0;i<count;i++) max=(max<<8)|response[2+i];
        if (max < 3) throw new FormatException("0x74 maximum transfer request must include SID + BSC + data.");
        return max;
    }
}
