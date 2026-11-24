using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.KeyActions;

/// <summary>
/// A KEY set step requests one exact 20-switch mask, then waits HoldMs
/// before the next action. It NEVER releases at the end of the timer.
/// WaitOnly is a distinct, explicit "no CAN write" step: zero mask on its own
/// always means an explicit all-open request, not an idle wait.
/// StepId is an engineering identity, not an operator-configurable parameter.
/// </summary>
public sealed record KeyActionStep(Guid StepId, uint KeyMask, int HoldMs, bool WaitOnly = false)
{
    public static KeyActionStep Create(uint mask = 0, int holdMs = 100) =>
        new KeyActionStep(Guid.NewGuid(), mask, holdMs).Validate();
    public KeyActionStep Validate()
    {
        if (StepId == Guid.Empty || KeyMask > 0xFFFFF || HoldMs is < 0 or > 600_000 ||
            WaitOnly && (KeyMask != 0 || HoldMs == 0))
            throw new ProjectDataException("KEY_ACTION_STEP",
                "KEY 步骤要求有效 ID、20-bit 组合、0～600000ms 等待；纯等待须为零掩码且时间大于 0。");
        return this;
    }
}

public sealed record KeyActionDefinition(Guid ActionId, string Name, int Order,
    ImmutableArray<KeyActionStep> Steps)
{
    public static KeyActionDefinition Create(string name, int order) =>
        new KeyActionDefinition(Guid.NewGuid(), name, order,
            ImmutableArray.Create(KeyActionStep.Create())).Validate();

    public KeyActionDefinition Validate()
    {
        if (ActionId == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name != Name.Trim() ||
            Name.Length > 100 || Order < 0 || Steps.IsDefault || Steps.Length is < 1 or > 512)
            throw new ProjectDataException("KEY_ACTION", "动作名称、顺序或步骤数量无效。");
        foreach (var step in Steps)
        {
            if (step is null) throw new ProjectDataException("KEY_ACTION_STEP", "按键步骤不能为空。");
            step.Validate();
        }
        if (Steps.Select(x => x.StepId).Distinct().Count() != Steps.Length ||
            Steps.Sum(s => (long)s.HoldMs) > 1_800_000)
            throw new ProjectDataException("KEY_ACTION_LIMIT", "步骤 ID 重复或总保持时间超过 30 分钟。");
        return this;
    }
}

/// <summary>Optional legacy hardware restriction pin. Without a pin, exact 20-bit masks
/// may execute through the separately authorized KEY CAN physical command fence.
/// A present pin always requires revision- and policy-matched hardware validation.</summary>
public sealed record KeyActionControllerPin(Guid AssetId, int Revision, string Sha256)
{
    public KeyActionControllerPin Validate()
    {
        if (AssetId == Guid.Empty || Revision < 1 || Sha256 is null ||
            Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new ProjectDataException("KEY_ACTION_CONTROLLER", "按键硬件配置引用无效。");
        return this;
    }
}

public sealed record KeyActionCatalog(Guid CatalogId, int SchemaVersion,
    KeyActionControllerPin? Controller,
    ImmutableArray<KeyActionDefinition> Actions)
{
    public const int CurrentSchema = 1;
    public static KeyActionCatalog Create() =>
        new(Guid.NewGuid(), CurrentSchema, null, ImmutableArray<KeyActionDefinition>.Empty);

    public KeyActionCatalog Validate()
    {
        if (CatalogId == Guid.Empty || SchemaVersion != CurrentSchema ||
            Actions.IsDefault || Actions.Length > 256)
            throw new ProjectDataException("KEY_CATALOG", "按键动作库版本、身份或数量无效。");
        Controller?.Validate();
        foreach (var action in Actions)
        {
            if (action is null) throw new ProjectDataException("KEY_ACTION", "存在空动作。");
            action.Validate();
        }
        if (Actions.Select(a => a.ActionId).Distinct().Count() != Actions.Length ||
            Actions.Select(a => a.Order).Distinct().Count() != Actions.Length ||
            Actions.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Actions.Length)
            throw new ProjectDataException("KEY_ACTION_DUPLICATE", "动作名称、ID 或顺序重复。");
        return this;
    }
}

public static class KeyActionCatalogCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false
    };
    private const int MaxBytes = 2 * 1024 * 1024;

    public static byte[] Encode(KeyActionCatalog catalog)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog.Validate(), Json);
        if (bytes.Length > MaxBytes)
            throw new ProjectDataException("KEY_CATALOG_SIZE", "按键动作库文件超过 2MB。");
        return bytes;
    }

    public static KeyActionCatalog Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaxBytes)
            throw new ProjectDataException("KEY_CATALOG_SIZE", "按键动作库文件长度无效。");
        try
        {
            var catalog = JsonSerializer.Deserialize<KeyActionCatalog>(bytes);
            return (catalog ?? throw new ProjectDataException("KEY_CATALOG",
                "按键动作库为空。")).Validate();
        }
        catch (JsonException ex)
        {
            throw new ProjectDataException("KEY_CATALOG_JSON", ex.Message);
        }
    }
}
