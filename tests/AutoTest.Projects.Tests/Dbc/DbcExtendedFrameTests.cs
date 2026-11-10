using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc;

public sealed class DbcExtendedFrameTests
{
    private const string Extended = """
        VERSION ""
        NS_ :
        BS_:
        BU_: VCU EPB
        BO_ 2566869031 ExampleExtended: 8 VCU
         SG_ Counter : 8|4@1+ (1,0) [0|15] "" EPB
        BO_ 2365466448 AnotherExtended: 8 EPB
         SG_ Status : 0|8@1+ (1,0) [0|255] "" VCU
        BO_ 1793 DebugStandard: 8 EPB
         SG_ Voltage : 40|8@1+ (1,0) [0|255] "V" VCU
        VAL_ 2566869031 Counter 0 "Zero" 1 "One" ;
        CM_ BO_ 2566869031 "comments must not disappear";
        """;

    [Fact]
    public void ExtendedFrameFlag_IsNotPartOfPhysicalCanId()
    {
        var doc = DbcWorkbenchDocument.Import("synthetic", Extended);
        Assert.Equal(3, doc.Model.Messages.Length);
        Assert.Equal("ExampleExtended", doc.Model.FindMessage(0x18FF5027,true).Name);
        Assert.Equal(0x0CFE2750u, doc.Model.FindMessage(0x0CFE2750,true).Id);
        Assert.Equal("DebugStandard", doc.Model.FindMessage(0x701,false).Name);
        Assert.False(doc.CanEdit);
        Assert.Equal(Extended, doc.ExportText());
        Assert.Contains(doc.ValueTables, x=>x.MessageId == 0x18FF5027 &&
                                             x.IsExtended && x.SignalName == "Counter");
    }

    [Fact]
    public void ExtendedFrameRoundtrip_PreservesRawDbcId()
    {
        var text = Extended.Replace("CM_ BO_ 2566869031 \"comments must not disappear\";", "");
        var db = DbcParserAdapter.Parse(text);
        var exported = DbcParserAdapter.Export(db);
        Assert.Contains("BO_ 2566869031 ExampleExtended", exported);
        Assert.Equal(0x18FF5027u, DbcParserAdapter.Parse(exported).FindMessage(0x18FF5027,true).Id);
    }

    [Fact]
    public void SameNumericId_StandardAndExtended_AreSeparateKeys()
    {
        const string text = """
            BO_ 291 StandardFrame: 1 ECU
             SG_ A : 0|8@1+ (1,0) [0|255] "" ECU
            BO_ 2147483939 ExtendedFrame: 1 ECU
             SG_ B : 0|8@1+ (1,0) [0|255] "" ECU
            """;
        var db = DbcParserAdapter.Parse(text);
        Assert.False(db.FindMessage(0x123,false).IsExtended);
        Assert.True(db.FindMessage(0x123,true).IsExtended);
        Assert.Throws<ProjectDataException>(()=>db.FindMessage(0x123));
    }

    [Fact]
    public void RejectUnflaggedOutOfRangeStandardId()
    {
        Assert.Throws<ProjectDataException>(()=>
            DbcParserAdapter.Parse("BO_ 3072 Invalid: 8 ECU\n SG_ X : 0|8@1+ (1,0) [0|255] \"\" ECU"));
    }
}
