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
    [Fact]
    public void BlankDbcIsAnEditableFirstClassDocument()
    {
        var text = DbcParserAdapter.Export(DbcDatabase.Create("HydEPB"));
        var document = DbcWorkbenchDocument.Import("HydEPB", text);
        Assert.Empty(document.Model.Messages);
        Assert.True(document.CanEdit);
        Assert.False(document.IsSourcePreserving);

        document.AddMessage(new DbcMessage(
            0x701, "Debug_701", 8, "ECU",
            System.Collections.Immutable.ImmutableArray<DbcSignal>.Empty));
        var reloaded = DbcWorkbenchDocument.Import("HydEPB", document.ExportText());
        Assert.Single(reloaded.Model.Messages);
        Assert.Equal("Debug_701", reloaded.Model.Messages[0].Name);
    }

    [Fact]
    public void NonsenseWithoutDbcHeaderStillCannotBeImportedAsBlankDatabase()
    {
        Assert.Throws<ProjectDataException>(() =>
            DbcWorkbenchDocument.Import("bad", "this is not a DBC"));
    }

    [Fact]
    public void HistoricalDraftCanUndoAndRedoAcrossAnExportedRevisionWithoutDroppingOriginalComments()
    {
        var original = Basic + "\nCM_ SG_ 291 Speed \"原定义\";\n";
        var doc = DbcWorkbenchDocument.Import("history", original);
        var before = doc.CaptureCheckpoint();
        doc.UpdateSignal(291, "Speed", doc.Model.FindSignal(291, "Speed") with { Factor = 0.25 });
        doc.SetSignalComment(291, "Speed", "修改后的定义");
        var saved = DbcWorkbenchDocument.Import("history", doc.ExportText());
        var after = saved.CaptureCheckpoint();
        var undone = DbcWorkbenchDocument.RecreateHistoricalDraft(before);
        Assert.True(undone.IsDirty);
        var undoRoundTrip = DbcWorkbenchDocument.Import("history", undone.ExportText());
        Assert.Equal(0.1, undoRoundTrip.Model.FindSignal(291, "Speed").Factor);
        Assert.Equal("原定义", undoRoundTrip.DisplayNames.SignalDescription(291, "Speed"));
        var redone = DbcWorkbenchDocument.RecreateHistoricalDraft(after);
        var redoRoundTrip = DbcWorkbenchDocument.Import("history", redone.ExportText());
        Assert.Equal(0.25, redoRoundTrip.Model.FindSignal(291, "Speed").Factor);
        Assert.Equal("修改后的定义", redoRoundTrip.DisplayNames.SignalDescription(291, "Speed"));
    }

    [Fact]
    public void EnumReplacementIsAtomicAndChecksRawRange()
    {
        var doc = DbcWorkbenchDocument.Import("enum", Basic);
        Assert.Throws<ProjectDataException>(() => doc.ReplaceValueDescriptions(291, "Speed",
            new Dictionary<long, string> { [0] = "Stop", [65536] = "Invalid" }));
        Assert.Equal(2, doc.ValueTables.Single().Labels.Count);
        doc.ReplaceValueDescriptions(291, "Speed", new Dictionary<long, string> { [0] = "静止", [1] = "移动" });
        var loaded = DbcWorkbenchDocument.Import("enum", doc.ExportText());
        Assert.Equal("移动", loaded.ValueTables.Single().Labels[1]);
        Assert.False(loaded.ValueTables.Single().Labels.ContainsKey(100));
        loaded.ReplaceValueDescriptions(291, "Speed", new Dictionary<long, string>());
        Assert.Empty(DbcWorkbenchDocument.Import("enum", loaded.ExportText()).ValueTables);
    }

    [Fact]
    public void SourceProtectedEnumReplacementDoesNotModifySource()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"保留\";\n";
        var doc = DbcWorkbenchDocument.Import("protected", source);
        Assert.Throws<ProjectDataException>(() => doc.ReplaceValueDescriptions(291, "Speed",
            new Dictionary<long, string> { [0] = "New" }));
        Assert.Equal(source, doc.ExportText());
    }

    [Fact]
    public void RenamingAndDeletingSupportedSignalKeepsOwnedMetadataInSync()
    {
        var doc = DbcWorkbenchDocument.Import("rename", Basic);
        doc.SetSignalAlias(291, "Temp", "温度");
        doc.UpdateSignal(291, "Temp", doc.Model.FindSignal(291, "Temp") with { Name = "Temperature" });
        Assert.Equal("温度", doc.DisplayNames.SignalAlias(291, "Temperature"));
        Assert.Equal("", doc.DisplayNames.SignalAlias(291, "Temp"));
        doc.DeleteSignal(291, "Temperature");
        var loaded = DbcWorkbenchDocument.Import("rename", doc.ExportText());
        Assert.Single(loaded.Model.FindMessage(291).Signals);
        Assert.Equal("", loaded.DisplayNames.SignalAlias(291, "Temperature"));
    }

    [Fact] public void NoEdit_PreservesEveryOriginalByteOfText()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"comment\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        Assert.True(doc.CanEdit);
        Assert.True(doc.IsSourcePreserving);
        Assert.Equal(source, doc.ExportText());
    }
    [Fact] public void SourcePreservingDbc_CanAddSignalWithoutLosingExistingAttributes()
    {
        var source = Basic + "\nBA_ \"GenMsgCycleTime\" BO_ 291 10;\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        Assert.True(doc.IsSourcePreserving);
        doc.AddSignal(291,
            new DbcSignal("New", 40, 8, true, false, 1, 0, 0, 255, "", "Tester"));
        var saved = doc.ExportText();
        Assert.Contains("BA_ \"GenMsgCycleTime\" BO_ 291 10;", saved);
        var opened = DbcWorkbenchDocument.Import("example", saved);
        Assert.Equal(3, opened.Model.FindMessage(291).Signals.Length);
    }

    [Fact] public void SourcePreservingDbc_CanDeleteUnreferencedSignalButNotReferenced()
    {
        var source = Basic + "\nBA_ \"GenMsgCycleTime\" BO_ 291 10;\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        doc.DeleteSignal(291, "Temp");
        var saved = DbcWorkbenchDocument.Import("example", doc.ExportText());
        Assert.Single(saved.Model.FindMessage(291).Signals);
        Assert.Equal("Speed", saved.Model.FindMessage(291).Signals.Single().Name);
        Assert.Contains("BA_ \"GenMsgCycleTime\" BO_ 291 10;", saved.ExportText());

        var guarded = DbcWorkbenchDocument.Import("example", source);
        var error = Assert.Throws<ProjectDataException>(() => guarded.DeleteSignal(291, "Speed"));
        Assert.Equal("DBC_SOURCE_PRESERVE", error.Code);
        Assert.Equal(source, guarded.ExportText());
    }

    [Fact] public void SourcePreservingDbc_CanAddMessageAndRetainOriginalComments()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"Original comment\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        doc.AddMessage(new DbcMessage(0x701, "Added", 8, "ECU",
            System.Collections.Immutable.ImmutableArray<DbcSignal>.Empty));
        var saved = doc.ExportText();
        Assert.Contains("CM_ SG_ 291 Speed \"Original comment\";", saved);
        Assert.Contains("BO_ 1793 Added: 8 ECU", saved);
        Assert.Equal(2, DbcWorkbenchDocument.Import("example", saved).Model.Messages.Length);
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
    [Fact] public void ChineseAliases_RoundTripWithoutChangingCanIdentifiersOrExistingComments()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"原来的车速说明\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        Assert.Equal("原来的车速说明", doc.DisplayNames.SignalAlias(291, "Speed"));
        doc.SetMessageAlias(291, "车辆状态");
        doc.SetSignalAlias(291, "Speed", "车速");
        var edited = doc.ExportText();
        Assert.Contains("CM_ SG_ 291 Speed \"原来的车速说明\";", edited);
        Assert.Contains("AutoTestMessageAlias", edited);
        Assert.Contains("AutoTestSignalAlias", edited);
        var reopened = DbcWorkbenchDocument.Import("example", edited);
        Assert.Equal("车辆状态", reopened.DisplayNames.MessageAlias(291));
        Assert.Equal("车速", reopened.DisplayNames.SignalAlias(291, "Speed"));
        Assert.Equal(doc.Model.FindMessage(291).Signals.ToArray(),
            reopened.Model.FindMessage(291).Signals.ToArray());
        Assert.Equal("Speed", reopened.Model.FindSignal(291, "Speed").Name);
    }


    [Fact] public void NativeMessageAndSignalComments_CanEditMultilineAndRoundTrip()
    {
        var original = Basic +
            "\nCM_ BO_ 291 \"原来的报文注释\";\n" +
            "CM_ SG_ 291 Speed \"原来的信号注释\";\n" +
            "BA_ \"GenMsgCycleTime\" BO_ 291 65;\n";
        var doc = DbcWorkbenchDocument.Import("comments", original);
        var messageComment = "用于调试时的电压采样说明。\n注意引用的单位、采样时间和失效条件。\n含引号：\"定义\"。";
        var signalComment = "车速反馈值\n0 = 停止\n其他值依照实际 DBC 定义。";
        doc.SetMessageComment(291, messageComment);
        doc.SetSignalComment(291, "Speed", signalComment);
        var text = doc.ExportText();
        Assert.Contains("CM_ BO_ 291 \"", text);
        Assert.Contains(@"注意引用的单位、采样时间和失效条件。\n", text);
        Assert.Contains("含引号：\\\"定义\\\"", text);
        Assert.Contains("BA_ \"GenMsgCycleTime\" BO_ 291 65;", text);
        Assert.DoesNotContain("AutoTestMessageSummary", text);
        Assert.DoesNotContain("AutoTestSignalSummary", text);
        var reopened = DbcWorkbenchDocument.Import("comments", text);
        Assert.Equal(messageComment, reopened.DisplayNames.MessageDescription(291));
        Assert.Equal(signalComment, reopened.DisplayNames.SignalDescription(291, "Speed"));
        Assert.Equal(doc.Model.FindMessage(291).Signals.ToArray(),
            reopened.Model.FindMessage(291).Signals.ToArray());
    }

    [Fact] public void AddingComment_DoesNotRewriteExistingSignalOrValueTable()
    {
        var source = Basic + "\nCM_ SG_ 291 Temp \"保留原温度定义\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        doc.SetMessageComment(291, "新报文说明：数值依据实际车辆标定。");
        var result = doc.ExportText();
        Assert.Contains("CM_ BO_ 291 \"新报文说明：数值依据实际车辆标定。\";", result);
        Assert.Contains("CM_ SG_ 291 Temp \"保留原温度定义\";", result);
        Assert.Contains("VAL_ 291 Speed 0 \"Stopped\" 100 \"Moving\" ;", result);
        var reopened = DbcWorkbenchDocument.Import("example", result);
        Assert.Equal("新报文说明：数值依据实际车辆标定。",
            reopened.DisplayNames.MessageDescription(291));
    }

    [Fact] public void CommentEdit_RejectsDuplicateSourceDefinitionsRatherThanLosingOne()
    {
        var source = Basic + "\nCM_ BO_ 291 \"first\";\nCM_ BO_ 291 \"second\";\n";
        var doc = DbcWorkbenchDocument.Import("duplicate", source);
        doc.SetMessageComment(291, "edited");
        var error = Assert.Throws<ProjectDataException>(() => doc.ExportText());
        Assert.Equal("DBC_COMMENT_DUPLICATE", error.Code);
    }

    [Fact] public void CommentEdit_RejectsUnsupportedMultilineRawSource()
    {
        var source = Basic + "\nCM_ BO_ 291 \"raw\" trailing;\n";
        var doc = DbcWorkbenchDocument.Import("raw", source);
        doc.SetMessageComment(291, "new definition");
        var error = Assert.Throws<ProjectDataException>(() => doc.ExportText());
        Assert.Equal("DBC_COMMENT_UNSUPPORTED", error.Code);
    }

    [Fact] public void ShortSummaries_RoundTripIndependentlyFromNamesAndRawDefinitions()
    {
        var source = Basic + "\nCM_ SG_ 291 Speed \"原始定义包含单位、工况和计算依据；不应被摘要覆盖。\";\n";
        var doc = DbcWorkbenchDocument.Import("example", source);
        doc.SetMessageAlias(291, "车辆状态");
        doc.SetMessageSummary(291, "车辆当前行驶反馈");
        doc.SetSignalAlias(291, "Speed", "车速");
        doc.SetSignalSummary(291, "Speed", "用于车速解析和判定");
        var text = doc.ExportText();
        Assert.Contains("AutoTestMessageSummary", text);
        Assert.Contains("AutoTestSignalSummary", text);
        Assert.Contains("CM_ SG_ 291 Speed \"原始定义包含单位、工况和计算依据；不应被摘要覆盖。\";", text);
        var loaded = DbcWorkbenchDocument.Import("example", text);
        Assert.Equal("车辆状态", loaded.DisplayNames.MessageAlias(291));
        Assert.Equal("车辆当前行驶反馈", loaded.DisplayNames.MessageSummary(291));
        Assert.Equal("用于车速解析和判定", loaded.DisplayNames.SignalSummary(291, "Speed"));
        Assert.Contains("计算依据", loaded.DisplayNames.SignalDescription(291, "Speed"));
        Assert.Equal(doc.Model.FindMessage(291).Signals.ToArray(),
            loaded.Model.FindMessage(291).Signals.ToArray());
    }

    [Fact] public void AliasOnlyEdit_PreservesUnsupportedDBCContent()
    {
        var extra = "\nSG_MUL_VAL_ 291 Speed 0 1-2 ;\n";
        var doc = DbcWorkbenchDocument.Import("example", Basic + extra);
        Assert.False(doc.CanEdit);
        doc.SetSignalAlias(291, "Speed", "车辆速度");
        Assert.True(doc.CanSave);
        var saved = doc.ExportText();
        Assert.Contains("SG_MUL_VAL_ 291 Speed 0 1-2 ;", saved);
        var reopened = DbcWorkbenchDocument.Import("example", saved);
        Assert.Equal("车辆速度", reopened.DisplayNames.SignalAlias(291, "Speed"));
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
