using System.Globalization;
using AutoTest.FlashLab.Configuration;

namespace AutoTest.FlashLab.Programming;

/// <summary>
/// Native AutoTest engineering configuration: a revisioned project document,
/// not a per-device consent and not an imported CanTool XML reference.
/// Parameters are typed, and only values ACTUALLY consumed by the verified
/// CanTool workflow are editable in this R1 version.
/// </summary>
public sealed record NativeFlashProfileDocument(
    int SchemaVersion,
    Guid ProfileId,
    int Revision,
    string EcuModelCode,
    string ProgrammingMethodId,
    uint AppRequestCanId,
    uint AppResponseCanId,
    uint BootRequestCanId,
    uint BootResponseCanId,
    int EcuCanKbps,
    uint FlashStart,
    uint FlashEndInclusive,
    int BlockLength,
    string? BinBaseAddress,
    string? FirmwareRelativePath,
    string? FirmwareSha256,
    string? SecurityRelativePath,
    string? SecuritySha256,
    // Preserved from the actual 2026-11-18 HydEPB Configuration(2).xml.
    // These are original reference/feature fields, NOT additional UDS endpoints.
    uint? ReferenceVersionCanId = null,
    uint? ReferenceDiagnosticsCanId = null,
    string? ReferenceBootFeature = null,
    string? ReferenceAppFeature = null)
    : IFlashParameterSource
{
    // Human-readable source filename. The stored firmware copy remains hashed.
    public string? FirmwareDisplayName { get; init; }

    // Keep content-addressed firmware paths out of normal user-facing labels.
    // Older projects without the original filename cannot reconstruct it.
    public string GetFirmwareDisplayLabel()
    {
        if (string.IsNullOrWhiteSpace(FirmwareRelativePath)) return "尚未选择 HEX";
        return string.IsNullOrWhiteSpace(FirmwareDisplayName)
            ? "已导入 HEX（原始文件名未记录）"
            : Path.GetFileName(FirmwareDisplayName);
    }

    public const string VerifiedMethod = "CanTool-HydEPB-v1";
    public const int CurrentSchema = 1;
    public static NativeFlashProfileDocument CreateDraft(string ecuModelCode) =>
        new(CurrentSchema, Guid.NewGuid(), 1, ecuModelCode, VerifiedMethod,
            0, 0, 0, 0, 500, 0, 0, 512, null, null, null, null, null);

    /// <summary>
    /// Historical HydEPB source: user's Configuration(3).xml (same values as the 2026-11-18 source).
    /// 512-byte block alignment comes separately from the successful reference
    /// CAN flash trace. This is an engineering DRAFT, never a published Job.
    /// The HEX image and reviewed SafetyFile are intentionally not fabricated.
    /// </summary>
    public static NativeFlashProfileDocument CreateHydEpbReferenceDraft() =>
        CreateDraft("HYDEPB") with
        {
            AppRequestCanId = 0x747,
            AppResponseCanId = 0x74F,
            BootRequestCanId = 0x18DA50F1,
            BootResponseCanId = 0x18DAF150,
            EcuCanKbps = 500,
            FlashStart = 0x10008000,
            FlashEndInclusive = 0x1006FFFF,
            BlockLength = 512,
            ReferenceVersionCanId = 0x74F,
            ReferenceDiagnosticsCanId = 0x18FE150E,
            ReferenceBootFeature = "73",
            ReferenceAppFeature = "701"
        };

    /// <summary>
    /// Backfills ONLY missing HydEPB reference fields of an existing draft.
    /// Never overwrite a configured ID, memory limit, baud rate, firmware,
    /// reviewed security library, stable ProfileId, or published version.
    /// </summary>
    public NativeFlashProfileDocument WithMissingHydEpbReference() =>
        !string.Equals(EcuModelCode, "HYDEPB", StringComparison.Ordinal) ||
        ProgrammingMethodId != VerifiedMethod ? this : this with
        {
            AppRequestCanId = AppRequestCanId == 0 ? 0x747u : AppRequestCanId,
            AppResponseCanId = AppResponseCanId == 0 ? 0x74Fu : AppResponseCanId,
            BootRequestCanId = BootRequestCanId == 0 ? 0x18DA50F1u : BootRequestCanId,
            BootResponseCanId = BootResponseCanId == 0 ? 0x18DAF150u : BootResponseCanId,
            FlashStart = FlashStart == 0 ? 0x10008000u : FlashStart,
            FlashEndInclusive = FlashEndInclusive == 0 ? 0x1006FFFFu : FlashEndInclusive,
            ReferenceVersionCanId = ReferenceVersionCanId ?? 0x74Fu,
            ReferenceDiagnosticsCanId = ReferenceDiagnosticsCanId ?? 0x18FE150Eu,
            ReferenceBootFeature = string.IsNullOrWhiteSpace(ReferenceBootFeature)
                ? "73" : ReferenceBootFeature,
            ReferenceAppFeature = string.IsNullOrWhiteSpace(ReferenceAppFeature)
                ? "701" : ReferenceAppFeature
        };

    public void ValidateDraft()
    {
        if (SchemaVersion != CurrentSchema || ProfileId == Guid.Empty || Revision < 1)
            throw new FormatException("原生烧录配置身份、版本或 Schema 无效。");
        if (!System.Text.RegularExpressions.Regex.IsMatch(EcuModelCode ?? "",
                @"^[A-Z0-9]{2,16}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
            throw new FormatException("请明确配置 ECU 型号代码（2～16 位大写字母/数字）。");
        if (ProgrammingMethodId != VerifiedMethod)
            throw new NotSupportedException("当前尚未验证其它 ECU 原生烧录方法。");
        if (EcuCanKbps is not (250 or 500))
            throw new NotSupportedException("参考 CanTool Flash Job 仅支持 ECU CAN 250k/500k。");
        if (BlockLength is < 64 or > 4096 || (BlockLength & (BlockLength - 1)) != 0)
            throw new FormatException("块长应为 64～4096 字节的 2 次幂。");
        // Drafts must persist while the engineer fills one ID at a time.
        // A non-empty pair is checked immediately; the complete pairs remain
        // mandatory in ValidateForFreeze before any physical request.
        if (AppRequestCanId != 0 && AppResponseCanId != 0)
            CheckEndpoint(AppRequestCanId, AppResponseCanId, "APP");
        if (BootRequestCanId != 0 && BootResponseCanId != 0)
            CheckEndpoint(BootRequestCanId, BootResponseCanId, "Boot");
        if ((FlashStart != 0 || FlashEndInclusive != 0) && FlashStart >= FlashEndInclusive)
            throw new FormatException("擦除地址区间无效。");
        ValidatePin(FirmwareRelativePath, FirmwareSha256, "固件");
        // Security is supplied by the AutoTest runtime, never per project.
        // Historical JSON fields are ignored rather than requested again.
        // Non-executable provenance fields from the user's original CanTool
        // XML must be retained faithfully without treating them as new services.
        if (ReferenceVersionCanId is > 0x1FFFFFFF ||
            ReferenceDiagnosticsCanId is > 0x1FFFFFFF)
            throw new FormatException("原始配置中的参考 CAN ID 超过 29 位。");
        static bool ValidFeature(string? value) => value is null ||
            System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9A-Fa-f]{1,8}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        if (!ValidFeature(ReferenceBootFeature) || !ValidFeature(ReferenceAppFeature))
            throw new FormatException("原始配置的 Feature/item 必须为十六进制文本。");
    }

    private static void ValidatePin(string? path, string? hash, string label)
    {
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(hash)) return;
        if (string.IsNullOrWhiteSpace(path) || hash is null || hash.Length != 64 ||
            !hash.All(Uri.IsHexDigit))
            throw new FormatException(label + "必须同时有工程内受控相对路径与 SHA256。");
    }

    private static void CheckEndpoint(uint tx, uint rx, string label)
    {
        if (tx == 0 || rx == 0 || tx == rx || tx > 0x1FFFFFFF ||
            rx > 0x1FFFFFFF || (tx > 0x7FF) != (rx > 0x7FF))
            throw new FormatException(label + " 请求/响应 CAN ID 无效或帧型不一致。");
    }

    public void ValidateForFreeze()
    {
        ValidateDraft();
        CheckEndpoint(AppRequestCanId, AppResponseCanId, "APP");
        CheckEndpoint(BootRequestCanId, BootResponseCanId, "Boot");
        if (FlashStart >= FlashEndInclusive)
            throw new FormatException("请配置真实 ECU 擦除范围。");
        if (string.IsNullOrWhiteSpace(FirmwareRelativePath))
            throw new FormatException("请选择当前 ECU 的 HEX 固件。");

        // The physical reference was verified for ONE ECU model only.
        // A new model code with copied CAN IDs and Flash addresses is NOT
        // evidence that its bootloader, erase ranges or security match.
        // Keep other models editable as drafts, but fail closed at freeze.
        if (!string.Equals(EcuModelCode, "HYDEPB", StringComparison.Ordinal))
            throw new NotSupportedException(
                "当前烧录方法仅已验证 HYDEPB ECU；其它型号不能套用 HydEPB 参数执行实体烧录，须先独立验证对应 OEM 方法。");

        // Only one proven physical reference profile exists today.
        // Changing these exact endpoints / memory geometry would advertise
        // an unverified OEM programming method and could erase another ECU.
        if (AppRequestCanId != 0x747 || AppResponseCanId != 0x74F ||
            BootRequestCanId != 0x18DA50F1 || BootResponseCanId != 0x18DAF150 ||
            FlashStart != 0x10008000 || FlashEndInclusive != 0x1006FFFF ||
            BlockLength != 512)
            throw new NotSupportedException(
                "当前 R1 原生烧录仅接受已有真实 CanTool 轨迹验证的 HydEPB 端点、地址和 0x200 块长；未验证的配置不能实体运行。");
    }

    public void ValidateConfiguredRange()
    {
        ValidateDraft();
        if (FlashStart >= FlashEndInclusive)
            throw new FormatException("Flash 起止地址尚未有效配置。");
    }

    public (uint Request, uint Response) GetEndpoint(bool boot)
    {
        var tx = boot ? BootRequestCanId : AppRequestCanId;
        var rx = boot ? BootResponseCanId : AppResponseCanId;
        CheckEndpoint(tx, rx, boot ? "Boot" : "APP");
        return (tx, rx);
    }

    public string Get(string name) => name switch
    {
        "APPPhyID" => Hex(AppRequestCanId),
        "APPRespond" => Hex(AppResponseCanId),
        "BootloadPhyID" => Hex(BootRequestCanId),
        "BootRespond" => Hex(BootResponseCanId),
        "FlashStartAddr" => Hex(FlashStart),
        "FlashEndAddr" => Hex(FlashEndInclusive),
        "BaudRate" => EcuCanKbps.ToString(CultureInfo.InvariantCulture) + "K",
        "BlockLength" => Hex(checked((uint)BlockLength)),
        "VersionID" => ReferenceVersionCanId is { } ver ? Hex(ver) : "",
        "DiagnosticsID" => ReferenceDiagnosticsCanId is { } diag ? Hex(diag) : "",
        "BootFeature" => ReferenceBootFeature ?? "",
        "APPFeature" => ReferenceAppFeature ?? "",
        "ForceMinInterval" or "SafetyFilePath" => "",
        _ => throw new KeyNotFoundException("未支持的烧录参数：" + name)
    };

    public IEnumerable<KeyValuePair<string, string>> FingerprintEntries()
    {
        // Match original CanTool reader's stable ordering for configuration
        // fingerprints (data SHA and aligned blocks are appended separately).
        foreach (var field in new[]
        {
            "BootloadPhyID", "BootRespond", "APPPhyID", "APPRespond",
            "VersionID", "DiagnosticsID", "FlashStartAddr", "FlashEndAddr",
            "BaudRate", "BlockLength", "SafetyFilePath", "ForceMinInterval"
        })
            yield return new(field, Get(field));
        // Original XML's BootFeature/item and APPFeature/item are preserved
        // as provenance, not interpreted as newly executable UDS commands.
        // Include them in newly frozen job identity only when present,
        // preserving legacy fingerprint behavior for old unset profiles.
        if (!string.IsNullOrWhiteSpace(ReferenceBootFeature))
            yield return new("BootFeature", ReferenceBootFeature);
        if (!string.IsNullOrWhiteSpace(ReferenceAppFeature))
            yield return new("APPFeature", ReferenceAppFeature);
    }

    private static string Hex(uint value) => "0x" + value.ToString("X", CultureInfo.InvariantCulture);
}
