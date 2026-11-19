// Integration: place in the existing WPF-capable UI tests project, add a Desktop project reference.
using System;
using AutoTest.LabLinkGui.LabLink;
using Xunit;

namespace AutoTest.Desktop.Tests;

public class LabLinkLogUtilitiesTests
{
    private static LabLinkLogEntry Entry(LabLinkActivityKind kind, string message, int? win32 = null) =>
        new(new LabLinkUiEvent(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), kind,
            message, "VID_1234&PID_5678", "Initialize WinUSB interface", win32, "detail"));

    [Fact] public void ErrorFilterIncludesFailuresButNotRequests()
    {
        Assert.True(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.Failure, "failed"), LabLogFilter.Errors, null));
        Assert.False(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.Request, "TX"), LabLogFilter.Errors, null));
    }
    [Fact] public void CommunicationFilterIncludesRequestResponseTrace()
    {
        Assert.True(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.Request, "TX"), LabLogFilter.Communications, "tx"));
        Assert.True(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.Response, "RX"), LabLogFilter.Communications, null));
        Assert.True(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.Trace, "trace"), LabLogFilter.Communications, null));
        Assert.False(LabLinkLogUtilities.Matches(Entry(LabLinkActivityKind.UserAction, "click"), LabLogFilter.Communications, null));
    }
    [Fact] public void SearchMatchesWin32CodeAndStage()
    {
        var e = Entry(LabLinkActivityKind.Error, "native error", 5);
        Assert.True(LabLinkLogUtilities.Matches(e, LabLogFilter.All, "0x00000005"));
        Assert.True(LabLinkLogUtilities.Matches(e, LabLogFilter.All, "winusb"));
    }
    [Fact] public void ExportPreservesDiagnosticIdentityAndEscapesMultiline()
    {
        var output = LabLinkLogUtilities.Export(new[] { Entry(LabLinkActivityKind.Failure, "line1\nline2", 5) });
        Assert.Contains("VID_1234&PID_5678", output);
        Assert.Contains("0x00000005", output);
        Assert.Contains("line1\\nline2", output);
    }
}


