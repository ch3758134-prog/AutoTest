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

    public static StationFixtureExecutionPlan From(ProductionFixtureConfiguration fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        fixture.Validate();
        var order = new List<byte>(2);
        if (fixture.UseClamp1 && fixture.UseClamp2)
        {
            // No silent default for two mechanical actuators: the order must
            // have been explicitly authored in station configuration.
            if (fixture.FirstClamp is not (1 or 2))
                throw new InvalidDataException("双夹具必须明确配置压紧顺序。");
            order.Add((byte)fixture.FirstClamp);
            order.Add((byte)(3 - fixture.FirstClamp));
        }
        else if (fixture.UseClamp1) order.Add(1);
        else if (fixture.UseClamp2) order.Add(2);
        byte mask = (byte)((fixture.UseClamp1 ? 1 : 0) |
                           (fixture.UseClamp2 ? 2 : 0));
        return new(
            order.Select(x => new StationClampAction(x, true)).ToArray(),
            order.AsEnumerable().Reverse()
                .Select(x => new StationClampAction(x, false)).ToArray(),
            mask, fixture.MovementTimeoutMs, fixture.SettleDelayMs,
            fixture.Clamp1PressHigh, fixture.Clamp2PressHigh);
    }
}
