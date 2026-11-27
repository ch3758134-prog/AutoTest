using System.IO;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// The ONLY standard station-owned fixture plan. TEST/FLASH do not infer
/// clamp actions from a barcode, MCU GPIO readback or legacy station scripts.
/// No command is dispatched by this pure authoring/verification model.
/// </summary>
public sealed record StationClampAction(byte Clamp, bool Press);

public sealed class StationFixtureExecutionPlan
{
    public IReadOnlyList<StationClampAction> Press { get; }
    public IReadOnlyList<StationClampAction> Release { get; }
    public byte AllowedOutputMask { get; }
    public int MovementTimeoutMs { get; }
    public int SettleDelayMs { get; }
    private readonly bool _clamp1PressHigh, _clamp2PressHigh;

    /// <summary>The requested USB GPIO level for a logical press/release action.
    /// Does NOT prove any mechanical movement or physical position.</summary>
    public bool OutputLevelHigh(StationClampAction action) =>
        action.Press == (action.Clamp == 1 ? _clamp1PressHigh :
            action.Clamp == 2 ? _clamp2PressHigh :
            throw new ArgumentOutOfRangeException(nameof(action)));

    private StationFixtureExecutionPlan(
        IReadOnlyList<StationClampAction> press,
        IReadOnlyList<StationClampAction> release,
        byte mask, int timeoutMs, int settleMs,
        bool clamp1PressHigh, bool clamp2PressHigh)
    {
        Press = press; Release = release;
        AllowedOutputMask = mask; MovementTimeoutMs = timeoutMs;
        SettleDelayMs = settleMs;
        _clamp1PressHigh = clamp1PressHigh;
        _clamp2PressHigh = clamp2PressHigh;
    }

    /// <summary>
    /// This station has one fixed two-actuator fixture. Never ask an operator
    /// to enable outputs or choose the order on every ECU project.
    /// Main controller 0x18: logical 1=press, 0=release; the real external
    /// pneumatic action still requires physical observation.
    /// </summary>
    public static StationFixtureExecutionPlan From(ProductionFixtureConfiguration fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        fixture.Validate();
        return new(
            new[] { new StationClampAction(1, true), new StationClampAction(2, true) },
            new[] { new StationClampAction(2, false), new StationClampAction(1, false) },
            0x03, fixture.MovementTimeoutMs, fixture.SettleDelayMs,
            clamp1PressHigh: true, clamp2PressHigh: true);
    }
}
