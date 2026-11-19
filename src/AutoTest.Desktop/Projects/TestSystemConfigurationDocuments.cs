using AutoTest.Projects.Project;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Functional SUT description (ecu.test TCF role). References project assets by immutable
/// revision so swapping a CAN database or key configuration is an explicit user decision.
/// </summary>
public sealed record TestSystemConfigurationDocument(
    int SchemaVersion, string Name, string EcuModel, string HardwareRevision,
    Guid? DbcAssetId = null, int? DbcRevision = null, string? DbcFingerprint = null,
    Guid? ButtonAssetId = null, int? ButtonRevision = null, string? ButtonFingerprint = null)
{
    public static TestSystemConfigurationDocument Create(string name) => new(1, name, "", "");

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            EcuModel is null || EcuModel.Length > 120 ||
            HardwareRevision is null || HardwareRevision.Length > 120)
            throw new ProjectDataException("TCF_IDENTITY", "被测系统配置名称、型号或硬件版本无效。");
        if ((DbcAssetId.HasValue && !ProjectConfigurationCodec.ValidPin(DbcRevision, DbcFingerprint)) ||
            (ButtonAssetId.HasValue && !ProjectConfigurationCodec.ValidPin(ButtonRevision, ButtonFingerprint)) ||
            (!DbcAssetId.HasValue && (DbcRevision.HasValue || DbcFingerprint is not null)) ||
            (!ButtonAssetId.HasValue && (ButtonRevision.HasValue || ButtonFingerprint is not null)))
            throw new ProjectDataException("TCF_PIN", "TCF 资源引用不完整或未绑定有效修订。");
    }
}

/// <summary>
/// This release has exactly one fixed physical station. TBC is a declarative
/// description, not a way to remap physical ports or authorize output commands.
/// </summary>
public sealed record TestBenchConfigurationDocument(
    int SchemaVersion, string Name, string StationLabel, string Notes)
{
    public const string StationType = "AutoTest WinUSB MainController";
    public const int KeySimCanPort = 0;
    public const int DutCanPort = 1;
    public static TestBenchConfigurationDocument Create(string name) =>
        new(1, name, "AutoTest 固定台架", "");

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            StationLabel is null || StationLabel.Length > 120 ||
            Notes is null || Notes.Length > 2000)
            throw new ProjectDataException("TBC_IDENTITY", "固定台架配置的名称或备注无效。");
    }
}
