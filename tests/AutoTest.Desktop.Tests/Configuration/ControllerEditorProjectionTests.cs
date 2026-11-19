using AutoTest.Desktop.Configuration;
using Xunit;

namespace AutoTest.Desktop.Tests.Configuration;

/// <summary>
/// Pure UI projection tests. These do not create mock hardware or simulate
/// a successful ECU response. The canonical Controller model is unchanged.
/// </summary>
public sealed class ControllerEditorProjectionTests
{
    [Fact]
    public void KeyTilesCoverExactly20IndependentSwitchBits()
    {
        var switches = Enumerable.Range(1, 20)
            .Select(n => new ControllerKeyTile(n, false, true)).ToArray();
        Assert.Equal(20, switches.Length);
        Assert.Equal(20, switches.Select(tile => tile.Bit).Distinct().Count());
        Assert.Equal(0xFFFFFu, switches.Aggregate(0u, (mask, tile) => mask | tile.Bit));
        switches[0].IsSelected = true;
        switches[18].IsSelected = true;
        Assert.Equal(0x40001u, switches.Where(tile => tile.IsSelected)
            .Aggregate(0u, (mask, tile) => mask | tile.Bit));
    }

    [Fact]
    public void VoltageRowReferencesCanonicalEditableFieldsRatherThanCopyingValues()
    {
        var target = new ConfigurationField("target3", "目标", "2.3", "");
        var minimum = new ConfigurationField("min3", "下限", "2.1", "");
        var maximum = new ConfigurationField("max3", "上限", "2.5", "");
        var row = new ControllerVoltageRow(3, target, minimum, maximum);
        Assert.Equal("CH3", row.ChannelLabel);
        row.Target.Value = "2.4";
        Assert.Same(target, row.Target);
        Assert.Equal("2.4", target.Value);
        Assert.Same(minimum, row.Minimum);
        Assert.Same(maximum, row.Maximum);
    }
}
