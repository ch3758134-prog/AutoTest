using System.Collections.Immutable;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc.Lab;

public sealed class DbcBitLayoutProjectionTests
{
    [Fact]
    public void Preview_DoesNotInventObservedBits_AndUsesIntelBitIndex()
    {
        var signal = new DbcSignal("Counter", 0, 4, true, false, 1, 0, 0, 15, "", "ECU");
        var message = new DbcMessage(0x100, "Status", 1, "ECU", ImmutableArray.Create(signal));
        var preview = DbcBitLayoutProjection.Preview(message);
        var row = Assert.Single(preview.Bytes);
        Assert.Equal("??", row.RawHex);
        Assert.Equal("Counter:?", row.Bit0);
        Assert.Equal("Counter:?", row.Bit3);
        Assert.Equal("·:?", row.Bit4);
        Assert.Equal("DEFINITION_ONLY (no observed bits)", preview.Quality);
        Assert.Equal("0,1,2,3", Assert.Single(preview.Signals).BitPositions);
    }

    [Fact]
    public void Replay_MapsMotorolaSawtoothAcrossByteBoundary()
    {
        var signal = new DbcSignal("Torque", 7, 12, false, false, 1, 0, 0, 4095, "Nm", "ECU");
        var message = new DbcMessage(0x122, "Motorola", 2, "ECU", ImmutableArray.Create(signal));
        var layout = DbcBitLayoutProjection.FromReplay(message, new byte[] { 0xA5, 0xC0 });
        Assert.Equal("REPLAY", layout.Source);
        Assert.Equal("REPLAY_INPUT (not physical CAN RX)", layout.Quality);
        Assert.Equal("A5", layout.Bytes[0].RawHex);
        Assert.Equal("Torque:1", layout.Bytes[0].Bit7);
        Assert.Equal("Torque:0", layout.Bytes[0].Bit6);
        Assert.Equal("Torque:1", layout.Bytes[0].Bit0);
        Assert.Equal("Torque:1", layout.Bytes[1].Bit7);
        Assert.Equal("Torque:0", layout.Bytes[1].Bit4);
        Assert.Equal("·:0", layout.Bytes[1].Bit3);
        Assert.Equal("7,6,5,4,3,2,1,0,15,14,13,12", Assert.Single(layout.Signals).BitPositions);
    }

    [Fact]
    public void MuxLayoutIsMarkedUnresolved_NotAnActiveBranchPrediction()
    {
        var select = new DbcSignal("Select", 0, 4, true, false, 1, 0, 0, 15, "", "ECU", "M");
        var branch = new DbcSignal("BranchOne", 4, 4, true, false, 1, 0, 0, 15, "", "ECU", "m1");
        var message = new DbcMessage(0x111, "Mux", 1, "ECU", ImmutableArray.Create(select, branch));
        var projection = DbcBitLayoutProjection.FromReplay(message, new byte[] { 0x12 });
        Assert.StartsWith("MUX_BRANCH_UNRESOLVED", projection.Quality);
        Assert.Contains("BranchOne(m1)", projection.Bytes[0].Bit4);
        Assert.Equal("REPLAY", projection.Source);
    }

    [Fact]
    public void IncorrectDlcRefused_BeforeAnyRawBitIsClaimed()
    {
        var message = new DbcMessage(0x123, "Empty", 8, "ECU", ImmutableArray<DbcSignal>.Empty);
        var ex = Assert.Throws<ProjectDataException>(() => DbcBitLayoutProjection.FromReplay(message, new byte[] { 0x11 }));
        Assert.Equal("DBC_REPLAY_DLC", ex.Code);
    }
}
