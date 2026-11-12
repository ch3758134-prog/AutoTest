using System.Collections.Immutable;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc;

public sealed class DbcWorkbenchTests
{
    private const string Basic = """
        VERSION ""

        NS_ :

        BS_:

        BU_: ECU Tester

        BO_ 291 Status: 8 ECU
         SG_ Speed : 0|16@1+ (0.1,0) [0|6553.5] "km/h" Tester
         SG_ Temp : 23|16@0- (0.5,-40) [-16424|16343.5] "degC" Tester

        VAL_ 291 Speed 0 "Stopped" 100 "Moving" ;
        """;
    [Fact] public void NoEdit_PreservesEveryOriginalByteOfText()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"comment\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        Assert.False(doc.CanEdit);
        Assert.Equal(source, doc.ExportText());
    }
    [Fact] public void UnknownSyntax_RefusesDestructiveEdits()
    {
        var doc = DbcWorkbenchDocument.Import("example", Basic + "\nBA_ \"GenMsgCycleTime\" BO_ 291 10;\n");
        var error = Assert.Throws<ProjectDataException>(() => doc.AddSignal(291,
            new DbcSignal("New", 40, 8, true, false, 1, 0, 0, 255, "", "Tester")));
        Assert.Equal("DBC_UNSUPPORTED_READONLY", error.Code);
    }
    [Fact] public void EditingSignalsAndValueTables_RoundTripsSemantics()
    {
        var doc = DbcWorkbenchDocument.Import("example", Basic);
        Assert.True(doc.CanEdit);
        doc.AddSignal(291, new DbcSignal("Valid", 48, 8, true, false, 1, 0, 0, 255, "", "Tester"));
        doc.SetValueDescription(291, "Valid", 1, "Yes");
        var saved = doc.ExportText();
        var reopened = DbcWorkbenchDocument.Import("example", saved);
        Assert.True(reopened.CanEdit);
        Assert.Equal(3, reopened.Model.FindMessage(291).Signals.Length);
        Assert.Contains(reopened.ValueTables, t => t.SignalName == "Valid" && t.Labels[1] == "Yes");
    }
    [Fact] public void OverlapAndIllegalField_AreRejected()
    {
        var doc = DbcWorkbenchDocument.Import("example", Basic);
        Assert.Throws<ProjectDataException>(() => doc.AddSignal(291, new DbcSignal("Overlap", 1, 8,
            true, false, 1, 0, 0, 255, "", "Tester")));
        Assert.Throws<ProjectDataException>(() => doc.AddSignal(291, new DbcSignal("Overflow", 63, 2,
            true, false, 1, 0, 0, 255, "", "Tester")));
    }
    [Fact] public void Replay_CanDecodeIntelAndMotorolaWithValueDescription()
    {
        var doc = DbcWorkbenchDocument.Import("example", Basic);
        var bytes = new byte[] { 100, 0, 0, 0, 0, 0, 0, 0 };
        var result = DbcReplayDecoder.Decode(doc, 291, bytes, DateTimeOffset.UtcNow);
        var speed = Assert.Single(result.Signals.Where(x => x.SignalName == "Speed"));
        Assert.Equal(10, speed.PhysicalValue, precision: 5);
        Assert.Equal("Moving", speed.ValueDescription);
        Assert.Equal("Replay", result.Source);
        Assert.Equal("ReplayTimestamp", result.TimeQuality);
        Assert.Equal(0, result.LostCount);
    }
    [Theory] [InlineData("123")] [InlineData("GG")] [InlineData("00 01 02 03 04 05 06 07 08")]
    public void BadReplayHex_IsRejected(string bad) =>
        Assert.Throws<ProjectDataException>(() => DbcReplayDecoder.ParseHex(bad));
    [Fact] public void MuxReplay_FailsClosedRatherThanWrongSignal()
    {
        var source = """
            VERSION ""
            NS_ :
            BS_:
            BU_: ECU Tester
            BO_ 42 Test: 1 ECU
             SG_ Mux M : 0|4@1+ (1,0) [0|15] "" Tester
             SG_ Value m1 : 4|4@1+ (1,0) [0|15] "" Tester
            """;
        var doc = DbcWorkbenchDocument.Import("Mux", source);
        var error = Assert.Throws<ProjectDataException>(() => DbcReplayDecoder.Decode(doc, 42, new byte[] { 0x21 }, DateTimeOffset.UtcNow));
        Assert.Equal("DBC_MUX_REPLAY", error.Code);
    }
    [Fact] public async Task SaveReopen_RejectsExternalConflict()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Autotest_DBC_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.dbc");
        try
        {
            var doc = DbcWorkbenchDocument.Import("test", Basic);
            var first = await DbcDocumentStore.SaveAsync(doc, path, null);
            var opened = await DbcDocumentStore.OpenAsync(path);
            Assert.Equal(Basic, opened.Document.ExportText());
            var second = await DbcDocumentStore.SaveAsync(doc, path, first.Sha256);
            Assert.Equal(first.Sha256, second.Sha256);
            await File.AppendAllTextAsync(path, "\nCM_ \"unexpected\";");
            var error = await Assert.ThrowsAsync<ProjectDataException>(() => DbcDocumentStore.SaveAsync(doc, path, second.Sha256));
            Assert.Equal("DBC_FILE_CONFLICT", error.Code);
        }
        finally { Directory.Delete(directory, true); }
    }
}
