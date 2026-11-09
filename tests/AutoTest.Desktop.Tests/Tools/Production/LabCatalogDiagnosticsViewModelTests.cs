using System;
using System.IO;
using System.Linq;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class LabCatalogDiagnosticsViewModelTests
{
    private static LabProductionToolViewModel NewVm() => new(
        new LabCatalogStore(Path.Combine(Path.GetTempPath(), "rule-diagnostics-" + Guid.NewGuid().ToString("N"))),
        new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));

    [Fact]
    public void DraftSamplesReplayWithoutStartingMockRunOrReleasingFactoryUnit()
    {
        var vm = NewVm();
        var published = vm.RuleRevision;
        vm.SampleReplayAtUtc = "2026-11-09T00:00:00Z";
        vm.ReplayDraftSamples();
        var row = Assert.Single(vm.SampleReplayRows);
        Assert.True(row.Passed);
        Assert.Equal("SAMPLE_PASSED", row.Code);
        Assert.Equal(published, vm.RuleRevision);
        Assert.Equal(0, vm.MockRunCount);
        Assert.Equal(0, vm.FactoryReleasedCount);
        Assert.Contains(vm.DiagnosticEvents, x => x.Stage == "SampleReplay" && x.Code == "SAMPLE_REPLAY_FINISHED");
    }

    [Fact]
    public void InvalidDraftShowsFailedSampleWithoutChangingPublishedRule()
    {
        var vm = NewVm();
        var oldRevision = vm.RuleRevision;
        vm.Rules[0].Pattern = "([";
        vm.ReplayDraftSamples();
        Assert.Equal("REGEX_INVALID", Assert.Single(vm.SampleReplayRows).Code);
        Assert.Equal(oldRevision, vm.RuleRevision);
        Assert.Contains("发布校验问题", vm.SampleReplayStatus);
        Assert.Equal(0, vm.MockRunCount);
    }

    [Fact]
    public void OverlapInspectionDistinguishesConfirmedConflictFromUnprovenSafety()
    {
        var vm = NewVm();
        vm.Rules.Add(new LabRuleRow
        {
            RuleId = "OVERLAP",
            Pattern = @"(?<serial>M[0-9]{6})",
            IdentityFieldsCsv = "serial"
        });
        vm.InspectDraftRuleOverlap();
        Assert.Equal("IDENTICAL_REGEX", Assert.Single(vm.RuleOverlapRows).Classification);
        Assert.Contains("已证实冲突对 1", vm.OverlapStatus);
        Assert.Contains(vm.DiagnosticEvents, x => x.Stage == "RuleOverlap" && x.Code == "CONFIRMED_RULE_OVERLAP");
        Assert.Equal(0, vm.FactoryReleasedCount);
    }

    [Fact]
    public void RevisionComparisonShowsEditedFieldsWithoutSavingOrPublishing()
    {
        var vm = NewVm();
        var revision = vm.RuleRevision;
        vm.Rules[0].Pattern = @"(?<serial>Z[0-9]{6})";
        vm.CompareDraftWithPublished();
        Assert.Contains(vm.RevisionChangeRows,
            x => x.EntityType == "Rule" && x.ChangedFields.Contains("Pattern"));
        Assert.Equal(revision, vm.RuleRevision);
        Assert.Contains("当前草稿", vm.RevisionCompareStatus);
        Assert.Equal(0, vm.MockRunCount);
    }

    [Fact]
    public void BadReplayTimestampIsVisibleAndDoesNotStartRun()
    {
        var vm = NewVm();
        vm.SampleReplayAtUtc = "bad-date";
        vm.ReplayDraftSamples();
        Assert.Empty(vm.SampleReplayRows);
        Assert.Contains("SAMPLE_DEFAULT_TIME_INVALID", vm.SampleReplayStatus);
        Assert.Contains(vm.DiagnosticEvents,
            x => x.Stage == "SampleReplay" && x.Code == "SAMPLE_DEFAULT_TIME_INVALID");
        Assert.Equal(0, vm.FactoryReleasedCount);
    }
}
