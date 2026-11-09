using System;
using System.Linq;
using AutoTest.Desktop.Shell.R1;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class R1DiagnosticsPanelTests
{
    [Fact]
    public void EmptyPanelHasNoInventedTraceAndCopyIsEmpty()
    {
        var panel = new R1DiagnosticsPanel();
        Assert.Empty(panel.AllEvents);
        Assert.Empty(panel.VisibleEvents);
        Assert.Empty(panel.CopySelected());
        Assert.Equal(0, panel.DroppedCount);
        Assert.Contains("专业工具", panel.IntegrityLabel);
    }

    [Fact]
    public void KindFilteringAndSearchRetainOriginalEvidenceWithoutChangingOrigin()
    {
        var panel = new R1DiagnosticsPanel();
        panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
            WorkspaceEventKind.Trace, WorkspaceEventLevel.Info, "DEVICE", "CAN", "rx 0x321",
            "P03", "corr-8", "trace-88", null, "AA BB", "2"));
        panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
            WorkspaceEventKind.Problem, WorkspaceEventLevel.Error, "UDS", "ISO-TP", "sequence mismatch",
            "P08", "corr-9", "trace-99", "SEQ_ERROR", "21 FF", "2"));
        Assert.Equal(2, panel.AllEvents.Count);
        Assert.Empty(panel.VisibleEvents); // Output selected by default
        panel.SetTab(WorkspaceEventKind.Trace);
        var trace = Assert.Single(panel.VisibleEvents);
        Assert.Equal("DEVICE", trace.Source);
        panel.Search = "trace-88";
        Assert.Single(panel.VisibleEvents);
        panel.Search = "seq_error";
        Assert.Empty(panel.VisibleEvents);
        panel.SetTab(WorkspaceEventKind.Problem);
        Assert.Single(panel.VisibleEvents);
        Assert.Equal("UDS", panel.VisibleEvents[0].Source);
        Assert.Equal(2, panel.AllEvents.Count);
    }

    [Fact]
    public void SelectedEventCopiesOnlySelectedRawEvidenceAndEscapesNewlines()
    {
        var panel = new R1DiagnosticsPanel();
        panel.SetTab(WorkspaceEventKind.Trace);
        panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
            WorkspaceEventKind.Trace, WorkspaceEventLevel.Warning, "DEVICE", "USB", "request",
            "P02", "corr-A", "trace-A", "WIN32:31", "01\t02\n03", "5"));
        panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
            WorkspaceEventKind.Trace, WorkspaceEventLevel.Info, "WAVE", "Replay", "other",
            "P07", "corr-B", "trace-B", null, "NOT_SELECTED", null));
        panel.SelectedEvent = panel.VisibleEvents.Single(x => x.CorrelationId == "corr-A");
        var text = panel.CopySelected();
        Assert.Contains("corr-A", text);
        Assert.Contains("WIN32:31", text);
        Assert.Contains(@"01\t02\n03", text);
        Assert.DoesNotContain("NOT_SELECTED", text);
        Assert.DoesNotContain("corr-B", text);
    }

    [Fact]
    public void FilterChangeInvalidatesHiddenSelectionAndCopy()
    {
        var panel = new R1DiagnosticsPanel();
        panel.SetTab(WorkspaceEventKind.Problem);
        panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
            WorkspaceEventKind.Problem, WorkspaceEventLevel.Error, "PROJECT", "Resource", "stale revision",
            ErrorCode: "REV_STALE"));
        panel.SelectedEvent = Assert.Single(panel.VisibleEvents);
        panel.Search = "unrelated";
        Assert.Null(panel.SelectedEvent);
        Assert.Equal("", panel.CopySelected());
        Assert.Single(panel.AllEvents);
    }

    [Fact]
    public void MemoryRetentionTruncationIsExplicitAndNeverClaimsFullExport()
    {
        var panel = new R1DiagnosticsPanel();
        for (var i = 0; i < R1DiagnosticsPanel.DisplayCapacity + 2; i++)
            panel.Accept(new WorkspaceDiagnosticEvent(DateTimeOffset.UtcNow,
                WorkspaceEventKind.Output, WorkspaceEventLevel.Info, "UI", "Navigation", i.ToString()));
        Assert.Equal(R1DiagnosticsPanel.DisplayCapacity, panel.AllEvents.Count);
        Assert.Equal(2, panel.DroppedCount);
        Assert.Contains("2 条", panel.IntegrityLabel);
        Assert.DoesNotContain(panel.AllEvents, x => x.Summary == "0");
        Assert.Contains(panel.AllEvents, x => x.Summary == (R1DiagnosticsPanel.DisplayCapacity + 1).ToString());
    }

    [Fact]
    public void NavigationFailuresAppearAsProblemsNotFakeTransportTrace()
    {
        var vm = new R1WorkspaceViewModel(new UnwiredHost());
        vm.Open(vm.Navigation.Single(x => x.Id == "P06"));
        Assert.Equal(1, vm.Diagnostics.ProblemCount);
        Assert.Equal(0, vm.Diagnostics.TraceCount);
        Assert.Empty(vm.OpenTabs);
        vm.ShowProblemsCommand.Execute();
        var problem = Assert.Single(vm.Diagnostics.VisibleEvents);
        Assert.Equal("R1 UI", problem.Source);
        Assert.Equal("P06", problem.WorkspaceId);
        Assert.Null(problem.TraceReference);
    }

    private sealed class UnwiredHost : IWorkspaceDocumentHost
    {
        public bool TryActivate(string workspaceId, out string failureReason)
        {
            failureReason = "unwired";
            return false;
        }
        public bool TryClose(string workspaceId, out string failureReason)
        {
            failureReason = "unwired";
            return false;
        }
    }
}
