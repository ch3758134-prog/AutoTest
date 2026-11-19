using System.Collections.Immutable;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc;

public sealed class DbcCanMessageEncoderTests
{
    private static DbcMessage Simple() => new(0x123, "Inputs", 2, "Tester",
        ImmutableArray.Create(
            new DbcSignal("Mode", 0, 8, true, false, 1, 0, 0, 255, "", "ECU"),
            new DbcSignal("Demand", 8, 8, true, false, 0.5, 0, 0, 127.5, "A", "ECU")));

    [Fact]
    public void EncodesCompletePhysicalSignalSet()
    {
        var payload = DbcCanMessageEncoder.Encode(Simple(), "Mode=3;Demand=12.5");
        Assert.Equal(new byte[] { 3, 25 }, payload);
        Assert.Equal(12.5,
            DbcSignalCodec.DecodePhysical(Simple().Signals[1], payload));
    }

    [Fact]
    public void AllowsExactFullRawDlcWithoutChangingBytes()
    {
        Assert.Equal(new byte[] { 0xAA, 0x55 },
            DbcCanMessageEncoder.Encode(Simple(), "HEX:AA55"));
        Assert.Equal(new byte[] { 0xAA, 0x55 },
            DbcCanMessageEncoder.Encode(Simple(), "AA 55"));
    }

    [Fact]
    public void MissingOrUnknownSignalCannotSilentlyTransmit()
    {
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(Simple(), "Mode=3"));
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(Simple(), "Mode=3;Unknown=1"));
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(Simple(), "Mode=3;Mode=4;Demand=1"));
    }

    [Fact]
    public void RejectsOutOfRangeAndUnrepresentablePhysicalValues()
    {
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(Simple(), "Mode=256;Demand=12.5"));
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(Simple(), "Mode=1;Demand=0.1"));
    }

    [Fact]
    public void RejectsUnverifiedMultiplexedSignalEncoding()
    {
        var message = new DbcMessage(0x124, "Mux", 1, "Tester",
            ImmutableArray.Create(
                new DbcSignal("Selector", 0, 4, true, false, 1, 0, 0, 15, "", "ECU", "M")));
        Assert.Throws<ProjectDataException>(() =>
            DbcCanMessageEncoder.Encode(message, "Selector=1"));
    }
}
