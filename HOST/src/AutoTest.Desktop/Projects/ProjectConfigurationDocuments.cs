using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Project;
using AutoTest.Specialists.Scan;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Projects;

/// <summary>Project-authored records: not hardware measurements, permissions or executable procedures.</summary>
public sealed record WaveSettingsDocument(int SchemaVersion, string Name,
    Guid? DbcAssetId, BusRole Bus, uint CanId, string SignalName, string Unit,
    Guid? EvidenceAssetId, WaveComparisonConfig Comparison,
    int? DbcAssetRevision = null, string? DbcAssetFingerprint = null,
    int? EvidenceAssetRevision = null, string? EvidenceAssetFingerprint = null)
{
    /// <summary>Optional, explicitly approved named reference; empty means not selected.</summary>
    public string ReferenceName { get; init; } = "";

    // 0 / missing is an old, unreviewed profile. Reading legacy WAVE assets
    // is allowed for editing, but the real TEST runner cannot treat one as
    // approved under a changed comparison algorithm.
    public const int CurrentComparisonAlgorithmRevision = 2;
    public int ApprovedAlgorithmRevision { get; init; }
    public string? ReviewedCriteriaSha256 { get; init; }
    public Guid? ApprovedReferenceAssetId { get; init; }
    public int? ApprovedReferenceAssetRevision { get; init; }
    public string? ApprovedReferenceAssetSha256 { get; init; }

    /// <summary>Freeze the exact signal, reference and ALL comparison criteria.
    /// A later edit cannot inherit approval from a previous safety judgement.</summary>
    public string ComputeReviewFingerprint()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Algorithm = CurrentComparisonAlgorithmRevision,
            DbcAssetId, DbcAssetRevision, DbcAssetFingerprint, Bus, CanId,
            SignalName, Unit, ReferenceName,
            ApprovedReferenceAssetId, ApprovedReferenceAssetRevision,
            ApprovedReferenceAssetSha256, Comparison
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public bool IsComparisonReviewCurrent =>
        ApprovedAlgorithmRevision == CurrentComparisonAlgorithmRevision &&
        ApprovedReferenceAssetId is { } referenceId && referenceId != Guid.Empty &&
        ApprovedReferenceAssetRevision is > 0 &&
        ApprovedReferenceAssetSha256 is { Length: 64 } &&
        ReviewedCriteriaSha256 is { Length: 64 } &&
        string.Equals(ReviewedCriteriaSha256, ComputeReviewFingerprint(),
            StringComparison.OrdinalIgnoreCase);

    public WaveSettingsDocument ApproveCurrentComparison() => this with
    {
        ApprovedAlgorithmRevision = CurrentComparisonAlgorithmRevision,
        ReviewedCriteriaSha256 = ComputeReviewFingerprint()
    };

    public static WaveSettingsDocument Create(string name) => new(1, name, null,
        BusRole.DutCan, 0, "", "", null,
        new WaveComparisonConfig(0, 0, 0.95, 1, 0.05, 1.6, 0, 0));

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Bus) ||
            CanId > 0x1FFFFFFF)
            throw new ProjectDataException("WAVE_SETTINGS", "波形配置数据或 CAN ID 无效。");
        if (ReferenceName is null || ReferenceName.Length > 128 ||
            (!string.IsNullOrWhiteSpace(ReferenceName) &&
             (!DbcAssetId.HasValue || string.IsNullOrWhiteSpace(SignalName))))
            throw new ProjectDataException("WAVE_REFERENCE",
                "必须先绑定 DBC 信号，才能选择命名参考波形；名称最多 128 字。");
        if (Comparison is null || Comparison.ValidateThresholds() is { } issue)
            throw new ProjectDataException("WAVE_THRESHOLDS",
                "波形判定参数无效：" + (Comparison?.ValidateThresholds() ?? "缺少参数"));
        if (ApprovedAlgorithmRevision is < 0 or > CurrentComparisonAlgorithmRevision ||
            ReviewedCriteriaSha256 is { } fingerprint &&
            (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
            throw new ProjectDataException("WAVE_APPROVAL",
                "波形判据的算法批准版本或指纹无效。");
        if (ApprovedReferenceAssetId is not null ||
            ApprovedReferenceAssetRevision is not null ||
            ApprovedReferenceAssetSha256 is not null)
        {
            if (ApprovedReferenceAssetId is not { } approvedId ||
                approvedId == Guid.Empty ||
                ApprovedReferenceAssetRevision is not > 0 ||
                ApprovedReferenceAssetSha256 is not { Length: 64 } approvedSha ||
                !approvedSha.All(Uri.IsHexDigit))
                throw new ProjectDataException("WAVE_REFERENCE_APPROVAL",
                    "已复核参考必须同时冻结资产 ID、修订号及 SHA256。");
        }
        if (DbcAssetId.HasValue && !ProjectConfigurationCodec.ValidPin(DbcAssetRevision, DbcAssetFingerprint))
            throw new ProjectDataException("WAVE_DBC_PIN", "已选择的 DBC 缺少资源修订与哈希。");
        if (EvidenceAssetId.HasValue &&
            !ProjectConfigurationCodec.ValidPin(EvidenceAssetRevision, EvidenceAssetFingerprint))
            throw new ProjectDataException("WAVE_REF_PIN", "关联波形证据缺少资源修订与哈希。");
        // Drafts may remain unbound; no physical waveform capture is implied.
    }
}

/// <summary>Not a ProgrammingProfile and not a flash execution authorization.</summary>
public sealed record FlashSettingsDocument(int SchemaVersion, string Name, string Target,
    string ExpectedEcuIdentity, Guid? FirmwareAssetId,
    uint? RequestCanId, uint? ResponseCanId, bool ExtendedCanId,
    int P2Milliseconds, int P2StarMilliseconds, string Session,
    string SecurityLevel, string SeedKeyProvider, string EraseRoutine,
    string VerifyRoutine, string MemoryRanges, bool ApprovedByOperator,
    int? FirmwareRevision = null, string? FirmwareFingerprint = null)
{
    public static FlashSettingsDocument Create(string name) => new(1, name, "", "", null,
        null, null, false, 50, 5000, "", "", "", "", "", "", false);

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) ||
            P2Milliseconds is < 1 or > 60000 || P2StarMilliseconds is < 1 or > 600000)
            throw new ProjectDataException("FLASH_SETTINGS", "烧录配置的基本字段或超时无效。");
        var maximum = ExtendedCanId ? 0x1FFFFFFFu : 0x7FFu;
        if (RequestCanId > maximum || ResponseCanId > maximum)
            throw new ProjectDataException("FLASH_CAN_ID", "请求/响应 CAN ID 超出帧类型范围。");
        if (FirmwareAssetId.HasValue &&
            !ProjectConfigurationCodec.ValidPin(FirmwareRevision, FirmwareFingerprint))
            throw new ProjectDataException("FLASH_IMAGE_PIN", "固件配置缺少资源修订与哈希。");
        if (ApprovedByOperator)
            throw new ProjectDataException("FLASH_NOT_AUTHORIZED",
                "配置文件不能包含物理操作授权，必须在真实运行流程单独获得授权。");
        // Authored settings NEVER authorize the real physical flash path.
    }
}

public sealed record InspectionStationAction(Guid ActionId, string Kind, int WaitAfterMilliseconds)
{
    public static readonly string[] Kinds =
        ["Clamp1Press", "Clamp1Release", "Clamp2Press", "Clamp2Release",
         "IgnOn", "IgnOff", "Wait"];

    public void Validate()
    {
        if (ActionId == Guid.Empty || !Kinds.Contains(Kind, StringComparer.Ordinal) ||
            WaitAfterMilliseconds is < 0 or > 120000 ||
            (Kind == "Wait" && WaitAfterMilliseconds == 0))
            throw new ProjectDataException("INSPECTION_ACTION",
                "工位动作类型、等待时间或动作身份无效。Wait 需要正数毫秒。");
    }
}

public sealed record InspectionStationProcedureDocument(int SchemaVersion, string Name,
    List<InspectionStationAction> BeforeStart, List<InspectionStationAction> AfterFinish)
{
    public static InspectionStationProcedureDocument Create(string name) =>
        new(1, name, [], []);

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            BeforeStart is null || AfterFinish is null ||
            BeforeStart.Count > 100 || AfterFinish.Count > 100)
            throw new ProjectDataException("INSPECTION_PROCEDURE",
                "工位动作配置的名称、版本或动作数量无效。");
        var actions = BeforeStart.Concat(AfterFinish).ToArray();
        foreach (var action in actions)
        {
            if (action is null)
                throw new ProjectDataException("INSPECTION_ACTION", "工位动作不能为 null。");
            action.Validate();
        }
        if (actions.Select(x => x.ActionId).Distinct().Count() != actions.Length)
            throw new ProjectDataException("INSPECTION_ACTION_DUPLICATE",
                "同一检测工艺中动作身份不能重复。");
    }
}

public static class ProjectConfigurationCodec
{
    public const string ControllerKind = "controller";
    public const string WaveSettingsKind = "wave-settings";
    public const string FlashSettingsKind = "flash-settings";
    public const string InspectionKind = "inspection-station";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false
    };
    public static bool ValidPin(int? revision, string? sha) =>
        revision is > 0 && sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
    public static bool IsConfiguration(string kind) => kind is
        ControllerKind or WaveSettingsKind or FlashSettingsKind or InspectionKind;

    public static byte[] Create(string kind, string name) => Encode(kind, kind switch
    {
        ControllerKind => EcuControlProfile.Create(name, "待绑定硬件", Guid.NewGuid()),
        WaveSettingsKind => WaveSettingsDocument.Create(name),
        FlashSettingsKind => FlashSettingsDocument.Create(name),
        InspectionKind => InspectionStationProcedureDocument.Create(name),
        _ => throw new ArgumentException("不支持的配置类型：" + kind)
    });

    public static object Decode(string kind, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024)
            throw new ProjectDataException("CONFIG_SIZE", "配置文件大小超过限制。");
        try
        {
            object document = kind switch
            {
                ControllerKind => ControllerRepository.Decode(bytes),
                WaveSettingsKind => JsonSerializer.Deserialize<WaveSettingsDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空波形配置。"),
                FlashSettingsKind => JsonSerializer.Deserialize<FlashSettingsDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空烧录配置。"),
                InspectionKind => JsonSerializer.Deserialize<InspectionStationProcedureDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空工位动作配置。"),
                _ => throw new ArgumentException("不支持的配置资源：" + kind)
            };
            Validate(kind, document);
            return document;
        }
        catch (JsonException e) { throw new ProjectDataException("CONFIG_JSON", e.Message); }
    }

    public static byte[] Encode(string kind, object document)
    {
        Validate(kind, document);
        var bytes = kind == ControllerKind
            ? ControllerRepository.Encode((EcuControlProfile)document)
            : JsonSerializer.SerializeToUtf8Bytes(document, document.GetType(), Json);
        if (bytes.Length > 2 * 1024 * 1024)
            throw new ProjectDataException("CONFIG_SIZE", "配置文件超过 2MB。");
        return bytes;
    }

    public static void Validate(string kind, object document)
    {
        switch (kind)
        {
            case ControllerKind: ((EcuControlProfile)document).Validate(); break;
            case WaveSettingsKind: ((WaveSettingsDocument)document).Validate(); break;
            case FlashSettingsKind: ((FlashSettingsDocument)document).Validate(); break;
            case InspectionKind: ((InspectionStationProcedureDocument)document).Validate(); break;
            default: throw new ArgumentException("不支持的配置资源：" + kind);
        }
    }
}

public sealed record ProjectConfigurationSession(ProjectId ProjectId, string ManifestPath,
    AssetId AssetId, int AssetRevision, string AssetName, string Kind,
    string FilePath, string FileSha256, object Document);
