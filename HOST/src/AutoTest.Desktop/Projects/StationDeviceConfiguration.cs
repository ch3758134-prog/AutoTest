using System.IO;

namespace AutoTest.Desktop.Projects;

/// <summary>One DUT position with two optional clamp outputs. An output ACK
/// is not evidence of mechanical closure. START is not a safety interlock.</summary>
public sealed record ProductionFixtureConfiguration(
    string StationSlotId, bool UseClamp1, bool UseClamp2,
    string Clamp1Output, string Clamp2Output, string ClampClosedFeedback,
    string ClampOpenFeedback, string StartInput, string StopInput,
    string InterlockInput, int MovementTimeoutMs, string ReleasePolicy,
    int FirstClamp = 0,
    bool Clamp1PressHigh = true, bool Clamp2PressHigh = true,
    int SettleDelayMs = 0, bool AutoClampEnabled = true)
{
    public static ProductionFixtureConfiguration Draft => new(
        "SLOT-01", false, false, "MCU:PTB1/J1-B5",
        "MCU:PTC2/J1-B4", "NOT_WIRED", "NOT_WIRED",
        "NOT_WIRED", "NOT_WIRED", "NOT_WIRED", 3000,
        "AFTER_DURABLE_RECORD_AND_SAFE_RELEASE_CONFIRMATION",
        0, true, true, 0, false); // New station drafts never implicitly energize a fixture.

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StationSlotId) ||
            StationSlotId.Length > 64 ||
            Clamp1Output != "MCU:PTB1/J1-B5" ||
            Clamp2Output != "MCU:PTC2/J1-B4" ||
            MovementTimeoutMs is < 100 or > 120000 ||
            SettleDelayMs is < 0 or > 30000 ||
            ReleasePolicy != "AFTER_DURABLE_RECORD_AND_SAFE_RELEASE_CONFIRMATION")
            throw new InvalidDataException("工位槽位、夹具通道或安全释放合同无效。");
        if (FirstClamp is < 0 or > 2 ||
            (AutoClampEnabled && UseClamp1 && UseClamp2 && FirstClamp == 0))
            throw new InvalidDataException("同时启用两路夹具时，必须明确选择先操作哪一路（1 或 2）。");
        if (StartInput is null || StopInput is null ||
            InterlockInput is null || ClampClosedFeedback is null ||
            ClampOpenFeedback is null)
            throw new InvalidDataException("工位反馈和实体输入不能为 null；未接线请明确使用 NOT_WIRED。");
        if (StartInput.Length > 128 || StopInput.Length > 128 ||
            InterlockInput.Length > 128 || ClampClosedFeedback.Length > 128 ||
            ClampOpenFeedback.Length > 128)
            throw new InvalidDataException("工位输入来源标识长度无效。");
    }

    public IReadOnlyList<string> PhysicalBlockers()
    {
        var issues = new List<string>
        {
            "工位输入配置只登记来源，不构成机械到位、实体 START 或互锁的实测证据；正式生产需可信固件/传感器适配和现场确认"
        };
        if (StartInput == "NOT_WIRED") issues.Add("实体 START 输入未接入并验证");
        if (StopInput == "NOT_WIRED") issues.Add("实体 STOP 输入未接入并验证");
        if (InterlockInput == "NOT_WIRED") issues.Add("安全互锁输入未接入并验证");
        if (AutoClampEnabled && (UseClamp1 || UseClamp2) && ClampClosedFeedback == "NOT_WIRED")
            issues.Add("夹具压紧机械反馈尚未取得可信来源");
        if (AutoClampEnabled && (UseClamp1 || UseClamp2) && ClampOpenFeedback == "NOT_WIRED")
            issues.Add("夹具释放机械反馈尚未取得可信来源");
        return issues;
    }
}


/// <summary>
/// Station equipment ONLY. The production project catalog decides which
/// installed TEST/FLASH package matches the barcode. No barcode rules,
/// product binding or production recipe can be saved in this file.
/// </summary>
public sealed record StationDeviceConfiguration(
    int SchemaVersion, int Revision, ProductionFixtureConfiguration Fixture)
{
    public const int CurrentSchema = 3;
    public static StationDeviceConfiguration New() =>
        new(CurrentSchema, 1, ProductionFixtureConfiguration.Draft);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema || Revision < 1 || Fixture is null)
            throw new InvalidDataException(
                "工位设备设置格式无效；请核对实际接线后重新配置。");
        Fixture.Validate();
    }
}
