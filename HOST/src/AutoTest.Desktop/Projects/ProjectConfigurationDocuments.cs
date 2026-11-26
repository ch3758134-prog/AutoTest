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

    /// <summary>Freeze the exact signal, reference and ALL comparison criteria.
    /// A later edit cannot inherit approval from a previous safety judgement.</summary>
    public string ComputeReviewFingerprint()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Algorithm = CurrentComparisonAlgorithmRevision,
            DbcAssetId, DbcAssetRevision, DbcAssetFingerprint, Bus, CanId,
            SignalName, Unit, ReferenceName, Comparison
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public bool IsComparisonReviewCurrent =>
        ApprovedAlgorithmRevision == CurrentComparisonAlgorithmRevision &&
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

public sealed record ScanRuleDraft(string RuleId, string Pattern, string IdentityFields,
    bool Published);
public sealed record ScanRecipeDraft(string RecipeId, string Model, string Customer,
    Guid? TestProgramAssetId, Guid? FirmwareAssetId, Guid? DbcAssetId,
    bool RequiresFlash, bool Published,
    int? TestProgramRevision = null, string? TestProgramFingerprint = null,
    int? FirmwareRevision = null, string? FirmwareFingerprint = null,
    int? DbcRevision = null, string? DbcFingerprint = null,
    Guid? TestProjectAssetId = null, int? TestProjectRevision = null,
    string? TestProjectFingerprint = null,
    Guid? FlashSettingsAssetId = null, int? FlashSettingsRevision = null,
    string? FlashSettingsFingerprint = null,
    string? FlashPolicy = null, int? TargetEcuCanKbps = null,
    string? FirmwareReleaseLabel = null, string? FlashJobFingerprint = null,
    string? HardwareCode = null)
{
    /// <summary>Legacy scan profiles are readable; legacy "requiresFlash" is
    /// NOT a verified CanTool production bundle.</summary>
    public string EffectiveFlashPolicy =>
        FlashPolicy is "NoFlash" or "FlashRequired" ? FlashPolicy :
        RequiresFlash ? "LegacyFlashUnbound" : "NoFlash";
}

public static class ScanFlashPolicy
{
    public const string NoFlash = "NoFlash";
    public const string FlashRequired = "FlashRequired";
    public static readonly int[] SupportedEcuCanKbps =
        [20, 50, 100, 125, 250, 500, 800, 1000];
}


public sealed record ScanMatchSettingsDocument(int SchemaVersion, string Name, bool Published,
    List<ScanRuleDraft> Rules, List<ScanRecipeDraft> Recipes)
{
    public static ScanMatchSettingsDocument Create(string name) => new(1, name, false, [], []);

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) ||
            Rules is null || Recipes is null || Rules.Count > 100 || Recipes.Count > 1000 ||
            Rules.Any(r => r is null) || Recipes.Any(r => r is null))
            throw new ProjectDataException("SCAN_CONFIG", "扫码规则或配方配置不完整。");
        if (Rules.GroupBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            Recipes.GroupBy(x => x.RecipeId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ProjectDataException("SCAN_CONFIG_DUPLICATE", "扫码规则或配方编号不能重复。");
        if (!Published) return; // Drafts can be saved but may never select a production recipe.
        if (!Rules.Any(r => r.Published))
            throw new ProjectDataException("SCAN_RULE_MISSING", "发布前至少需要一条已发布的有效规则。");
        var rules = ToRules();
        var errors = rules.Validate();
        if (errors.Length > 0)
            throw new ProjectDataException("SCAN_RULE_INVALID", string.Join("; ", errors.Take(8)));
        if (!Recipes.Any(r => r.Published))
            throw new ProjectDataException("SCAN_RECIPE_MISSING", "正式发布前必须指定至少一个配方。");
        foreach (var recipe in Recipes.Where(r => r.Published))
        {
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) ||
                (recipe.TestProgramAssetId is null && recipe.TestProjectAssetId is null) ||
                (recipe.TestProgramAssetId.HasValue &&
                 !ProjectConfigurationCodec.ValidPin(recipe.TestProgramRevision, recipe.TestProgramFingerprint)) ||
                (recipe.TestProjectAssetId.HasValue &&
                 !ProjectConfigurationCodec.ValidPin(recipe.TestProjectRevision, recipe.TestProjectFingerprint)) ||
                (recipe.FirmwareAssetId.HasValue &&
                 !ProjectConfigurationCodec.ValidPin(recipe.FirmwareRevision, recipe.FirmwareFingerprint)) ||
                (recipe.DbcAssetId.HasValue &&
                 !ProjectConfigurationCodec.ValidPin(recipe.DbcRevision, recipe.DbcFingerprint)) ||
                (recipe.FlashSettingsAssetId.HasValue &&
                 !ProjectConfigurationCodec.ValidPin(recipe.FlashSettingsRevision, recipe.FlashSettingsFingerprint)))
                throw new ProjectDataException("SCAN_RECIPE_INVALID",
                    "发布配方必须引用已保存的测试项目或测试程序，以及所选资源的确定修订与 SHA。");
            if (!string.IsNullOrWhiteSpace(recipe.HardwareCode) &&
                (recipe.HardwareCode.Length != 3 || recipe.HardwareCode[0] != 'H' ||
                 !char.IsAsciiDigit(recipe.HardwareCode[1]) ||
                 !char.IsAsciiDigit(recipe.HardwareCode[2])))
                throw new ProjectDataException("SCAN_HARDWARE",
                    "UT1 产品硬件版号应为 H00～H99；旧无版本条码可以不填。");
            if (recipe.FlashPolicy is not null and not ("NoFlash" or "FlashRequired"))
                throw new ProjectDataException("SCAN_FLASH_POLICY", "烧录策略只能选不烧录或必须烧录。");
            if (recipe.TargetEcuCanKbps is int rate &&
                !ScanFlashPolicy.SupportedEcuCanKbps.Contains(rate))
                throw new ProjectDataException("SCAN_ECU_CAN_BAUD", "目标 ECU CAN 波特率不在主控支持列表中。");
            if (recipe.FlashPolicy == ScanFlashPolicy.NoFlash && recipe.RequiresFlash ||
                recipe.FlashPolicy == ScanFlashPolicy.FlashRequired && !recipe.RequiresFlash)
                throw new ProjectDataException("SCAN_FLASH_POLICY",
                    "已发布配方的烧录策略和是否烧录标记不一致。");
            if (recipe.FlashPolicy == ScanFlashPolicy.FlashRequired &&
                (recipe.FirmwareAssetId is null || recipe.FlashSettingsAssetId is null ||
                 recipe.TargetEcuCanKbps is not (250 or 500) ||
                 string.IsNullOrWhiteSpace(recipe.FirmwareReleaseLabel)))
                throw new ProjectDataException("SCAN_FLASH_BUNDLE",
                    "请选择目标固件、烧录配置、250/500 kbit/s 及软件版本名称。");
            if (recipe.FlashJobFingerprint is { Length: > 0 } sha &&
                (sha.Length != 64 || !sha.All(Uri.IsHexDigit)))
                throw new ProjectDataException("SCAN_FLASH_SHA",
                    "冻结烧录任务 SHA256 无效。");
        }
    }

    public IdentityRuleSet ToRules()
    {
        var list = Rules.Select(r => new IdentityRule(r.RuleId, r.Pattern,
            (r.IdentityFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToImmutableArray(), r.Published ? RulePublication.Published : RulePublication.Draft));
        var fingerprint = RevisionFingerprint.FromBytes(JsonSerializer.SerializeToUtf8Bytes(Rules));
        return new IdentityRuleSet(list, fingerprint, Published);
    }
}

/// <summary>
/// A narrow station process definition for this ECU. These are verified
/// physical capabilities, not a second TEST DSL. This authored file cannot
/// authorize live outputs; real production requires a separate stage executor,
/// station evidence and an immutable release snapshot.
/// </summary>
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
    public const string ScanSettingsKind = "scan-rules";
    public const string TestConfigurationKind = "test-configuration";
    public const string BenchConfigurationKind = "bench-configuration";
    public const string InspectionKind = "inspection-station";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false
    };
    public static bool ValidPin(int? revision, string? sha) =>
        revision is > 0 && sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
    public static bool IsConfiguration(string kind) => kind is
        ControllerKind or WaveSettingsKind or FlashSettingsKind or ScanSettingsKind or
        TestConfigurationKind or BenchConfigurationKind or InspectionKind;

    public static byte[] Create(string kind, string name) => Encode(kind, kind switch
    {
        ControllerKind => EcuControlProfile.Create(name, "待绑定硬件", Guid.NewGuid()),
        WaveSettingsKind => WaveSettingsDocument.Create(name),
        FlashSettingsKind => FlashSettingsDocument.Create(name),
        ScanSettingsKind => ScanMatchSettingsDocument.Create(name),
        TestConfigurationKind => TestSystemConfigurationDocument.Create(name),
        BenchConfigurationKind => TestBenchConfigurationDocument.Create(name),
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
                ScanSettingsKind => JsonSerializer.Deserialize<ScanMatchSettingsDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空扫码配置。"),
                TestConfigurationKind => JsonSerializer.Deserialize<TestSystemConfigurationDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空 TCF。"),
                BenchConfigurationKind => JsonSerializer.Deserialize<TestBenchConfigurationDocument>(bytes, Json)
                    ?? throw new ProjectDataException("CONFIG_JSON", "空 TBC。"),
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
            case ScanSettingsKind: ((ScanMatchSettingsDocument)document).Validate(); break;
            case TestConfigurationKind: ((TestSystemConfigurationDocument)document).Validate(); break;
            case BenchConfigurationKind: ((TestBenchConfigurationDocument)document).Validate(); break;
            case InspectionKind: ((InspectionStationProcedureDocument)document).Validate(); break;
            default: throw new ArgumentException("不支持的配置资源：" + kind);
        }
    }
}

public sealed record ProjectConfigurationSession(ProjectId ProjectId, string ManifestPath,
    AssetId AssetId, int AssetRevision, string AssetName, string Kind,
    string FilePath, string FileSha256, object Document);
