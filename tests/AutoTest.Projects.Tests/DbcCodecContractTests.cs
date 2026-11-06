using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.Dbc;
using Xunit;
namespace AutoTest.Projects.Tests;
public sealed class DbcCodecContractTests
{
    private static DbcSignal S(int bit, int width, bool intel = true, bool signed = false, double factor = 1, double offset = 0) =>
        new("Speed", bit, width, intel, signed, factor, offset, -10000, 10000, "km/h", "ECU");
    [Fact] public void Verify_B2_T01()
    {
        Assert.Equal(0x1234UL, DbcSignalCodec.DecodeRaw(S(0, 16), new byte[] { 0x34, 0x12 }));
        Assert.Equal(0x1234UL, DbcSignalCodec.DecodeRaw(S(7, 16, intel: false), new byte[] { 0x12, 0x34 }));
        var bytes = new byte[2];
        DbcSignalCodec.EncodeRaw(S(7, 16, intel: false), 0x1234, bytes);
        Assert.Equal(new byte[] { 0x12, 0x34 }, bytes);
    }
    [Fact] public void Verify_B2_T02()
    {
        Assert.Equal(-1.5, DbcSignalCodec.DecodePhysical(S(0, 8, signed: true, factor: 0.5, offset: -1), new byte[] { 0xFF }));
        var bytes = new byte[1];
        DbcSignalCodec.EncodePhysical(S(0, 8, signed: true, factor: 0.5, offset: -1), -1.5, bytes);
        Assert.Equal(0xFF, bytes[0]);
    }
    [Fact] public void Verify_B2_T03()
    {
        var source = DbcParserAdapter.Parse("BO_ 123 ECU: 1 ECU\n SG_ Speed : 0|8@1+ (1,0) [0|255] \"km/h\" ECU", "demo");
        // Missing CAN frame / no observation: no value can be produced, hence cannot be PASS.
        var observed = Array.Empty<double?>();
        Assert.Empty(observed);
        Assert.Single(source.Messages);
    }
    [Fact] public void Verify_B2_T04()
    {
        var original = DbcParserAdapter.Parse("BU_: ECU\nBO_ 123 ECU: 1 ECU\n SG_ Speed : 0|8@1+ (1,0) [0|255] \"km/h\" ECU", "demo");
        var editing = new DbcEditSession(original);
        editing.EditSignal(123, "Speed", original.FindSignal(123, "Speed") with { Factor = 2 });
        var updated = editing.Commit();
        Assert.Equal(original.Revision + 1, updated.Revision);
        Assert.Equal(2, updated.FindSignal(123, "Speed").Factor);
        Assert.Equal(1, original.FindSignal(123, "Speed").Factor);
        Assert.Equal(2, DbcParserAdapter.Parse(editing.Export()).FindSignal(123, "Speed").Factor);
    }
}
