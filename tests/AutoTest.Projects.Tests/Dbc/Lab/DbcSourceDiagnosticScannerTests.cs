using AutoTest.Projects.Dbc.Lab;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc.Lab;

public sealed class DbcSourceDiagnosticScannerTests
{
    [Fact]
    public void BrokenSignal_HasRealSourceLine()
    {
        var text = "VERSION \"\"\nNS_ :\nBS_:\nBO_ 291 Status: 8 ECU\n SG_ Oops : BAD\n";
        var issues = DbcSourceDiagnosticScanner.Inspect(text);
        Assert.Contains(issues, i => i.Line == 5 && i.Code == "DBC_SG_SYNTAX" &&
            i.Severity == "ERROR");
    }

    [Fact]
    public void UnsupportedDirective_IsWarning_NotDestructivelyEdited()
    {
        var text = "VERSION \"\"\nNS_ :\nBS_:\nBU_: ECU\nBO_ 1 Msg: 1 ECU\n SG_ A : 0|8@1+ (1,0) [0|255] \"\" ECU\nCM_ \"keep me\";\n";
        var issues = DbcSourceDiagnosticScanner.Inspect(text);
        Assert.Contains(issues, i => i.Line == 7 && i.Code == "DBC_READONLY_DIRECTIVE" &&
            i.Severity == "WARNING");
        Assert.DoesNotContain(issues, i => i.Severity == "ERROR");
    }

    [Fact]
    public void OrphanSignal_IsDiagnosedAtOriginalLine()
    {
        var text = "VERSION \"\"\nBS_:\nSG_ A : 0|8@1+ (1,0) [0|255] \"\" ECU\n";
        var issues = DbcSourceDiagnosticScanner.Inspect(text);
        Assert.Contains(issues, i => i.Line == 3 && i.Code == "DBC_SG_ORPHAN");
    }

    [Fact]
    public void NumericSemanticFailure_IsReportedWithoutFakeLineNumber()
    {
        var text = "VERSION \"\"\nBS_:\nBO_ 1 Msg: 1 ECU\n SG_ A : 0|8@1+ (0,0) [0|255] \"\" ECU\n";
        var issues = DbcSourceDiagnosticScanner.Inspect(text);
        Assert.Contains(issues, i => i.Code == "DBC_PARSE" && i.Line == 0);
    }
}
