using System.Collections.Immutable;
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
    Guid? ReferenceAssetId, WaveComparisonConfig Comparison)
{
    public static WaveSettingsDocument Create(string name) => new(1, name, null,
        BusRole.DutCan, 0, "", "", null,
        new WaveComparisonConfig(0, 0, 0.95, 1, 0.05, 1.6, 0, 0));

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Bus) ||
            CanId > 0x1FFFFFFF)
            throw new ProjectDataException("WAVE_SETTINGS", "波形配置数据或 CAN ID 无效。");
        if (Comparison is null || Comparison.ValidateThresholds() is { } issue)
            throw new ProjectDataException("WAVE_THRESHOLDS",
                "波形判定参数无效：" + (Comparison?.ValidateThresholds() ?? "缺少参数"));
        // DBC/Signal/Reference may remain empty in a saved draft; execution is not enabled.
    }
}

/// <summary>Not a ProgrammingProfile and not a flash execution authorization.</summary>
public sealed record FlashSettingsDocument(int SchemaVersion, string Name, string Target,
    string ExpectedEcuIdentity, Guid? FirmwareAssetId,
    uint? RequestCanId, uint? ResponseCanId, bool ExtendedCanId,
    int P2Milliseconds, int P2StarMilliseconds, string Session,
    string SecurityLevel, string SeedKeyProvider, string EraseRoutine,
    string VerifyRoutine, string MemoryRanges, bool ApprovedByOperator)
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
        // Authored settings NEVER authorize the real physical flash path.
    }
}

public sealed record ScanRuleDraft(string RuleId, string Pattern, string IdentityFields,
    bool Published);
public sealed record ScanRecipeDraft(string RecipeId, string Model, string Customer,
    Guid? TestProgramAssetId, Guid? FirmwareAssetId, Guid? DbcAssetId,
    bool RequiresFlash, bool Published);

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
        var rules = ToRules();
        var errors = rules.Validate();
        if (errors.Length > 0)
            throw new ProjectDataException("SCAN_RULE_INVALID", string.Join("; ", errors.Take(8)));
        if (!Recipes.Any(r => r.Published))
            throw new ProjectDataException("SCAN_RECIPE_MISSING", "正式发布前必须指定至少一个配方。");
        foreach (var recipe in Recipes.Where(r => r.Published))
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) || recipe.TestProgramAssetId is null ||
                (recipe.RequiresFlash && recipe.FirmwareAssetId is null))
                throw new ProjectDataException("SCAN_RECIPE_INVALID",
                    "已发布配方必须指定测试程序，烧录配方必须指定固件资源。");
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

public static class ProjectConfigurationCodec
{
    public const string ControllerKind = "controller";
    public const string WaveSettingsKind = "wave-settings";
    public const string FlashSettingsKind = "flash-settings";
    public const string ScanSettingsKind = "scan-rules";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false
    };
    public static bool IsConfiguration(string kind) => kind is
        ControllerKind or WaveSettingsKind or FlashSettingsKind or ScanSettingsKind;

    public static byte[] Create(string kind, string name) => Encode(kind, kind switch
    {
        ControllerKind => EcuControlProfile.Create(name, "待绑定硬件", Guid.NewGuid()),
        WaveSettingsKind => WaveSettingsDocument.Create(name),
        FlashSettingsKind => FlashSettingsDocument.Create(name),
        ScanSettingsKind => ScanMatchSettingsDocument.Create(name),
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
            default: throw new ArgumentException("不支持的配置资源：" + kind);
        }
    }
}

public sealed record ProjectConfigurationSession(ProjectId ProjectId, string ManifestPath,
    AssetId AssetId, int AssetRevision, string AssetName, string Kind,
    string FilePath, string FileSha256, object Document);
