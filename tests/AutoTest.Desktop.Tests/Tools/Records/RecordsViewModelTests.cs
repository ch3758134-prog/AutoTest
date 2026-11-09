using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Desktop.Tools.Records;
using AutoTest.Records.Operations;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Records;

public sealed class RecordsViewModelTests
{
    [Fact]
    public async Task CountsAreNotLabeledRealShipmentsAndSnapshotRemainsFrozen()
    {
        var vm = new RecordsViewModel(new FakeHistory());
        await vm.RefreshAsync();
        Assert.Contains("不同 ECU 1", vm.Summary);
        Assert.Contains("不是实物出厂放行数量", vm.Summary);
        vm.Selected = Assert.Single(vm.Runs);
        Assert.Contains("rev42", vm.Snapshot);
    }

    [Fact]
    public async Task NoRunSelectedCannotExportPretendSuccess()
    {
        var vm = new RecordsViewModel(new FakeHistory()) { ReportPath = "report.json" };
        await vm.ExportReportAsync(false);
        Assert.Contains("先选择", vm.Error);
    }

    [Fact]
    public async Task StepFactViewerReadsFrozenExpectedActualAndFiltersStepId()
    {
        var vm = new RecordsViewModel(new FakeHistory());
        await vm.RefreshAsync();
        vm.Selected = Assert.Single(vm.Runs);
        vm.StepSearch = "step-a";
        await vm.InspectStepFactsAsync();
        Assert.Contains("expect-a", vm.StepFacts);
        Assert.Contains("actual-a", vm.StepFacts);
        Assert.DoesNotContain("expect-b", vm.StepFacts);
        Assert.Contains("1 条", vm.Status);
    }

    [Fact]
    public async Task StepFactViewerRejectsUnselectedRun()
    {
        var vm = new RecordsViewModel(new FakeHistory());
        await vm.InspectStepFactsAsync();
        Assert.Contains("先选择", vm.Error);
    }

    [Fact]
    public async Task ArchiveIntegrityIsExplicitlyNotShipmentStatus()
    {
        var vm = new RecordsViewModel(new FakeHistory());
        await vm.RefreshAsync();
        vm.Selected = Assert.Single(vm.Runs);
        await vm.InspectArchiveAsync();
        Assert.Contains("EvidenceMissing", vm.ArchiveIntegrity);
        Assert.Contains("NOT_EVALUATED_NO_RELEASE_CONTRACT", vm.ArchiveIntegrity);
        Assert.Contains("出厂放行状态未评估", vm.Status);
    }

    [Fact]
    public async Task ArchiveInspectionRequiresSelectedRun()
    {
        var vm = new RecordsViewModel(new FakeHistory());
        await vm.InspectArchiveAsync();
        Assert.Contains("先选择 RunId", vm.Error);
    }

    private sealed class FakeHistory : IRecordHistoryToolSource
    {
        public Task<IReadOnlyList<RecordHistoryToolRow>> QueryAsync(string? dutId, string? mode,
            int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecordHistoryToolRow>>(new[]
            {
                new RecordHistoryToolRow(Guid.NewGuid().ToString("D"), "ECU-1", "Development",
                    DateTimeOffset.UtcNow, "Committed", "Inconclusive", 3, 1, null, "{\"revision\":\"rev42\"}")
            });
        public Task<RecordToolSummary> SummaryAsync(string mode, CancellationToken ct = default) =>
            Task.FromResult(new RecordToolSummary(2, 1, 2, 1, 1, 0, 0));
        public Task<RunArchiveIntegrityResult> InspectArchiveAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(new RunArchiveIntegrityResult(RunId.Create(Guid.Parse(runId)),
                RunArchiveIntegrityStatus.EvidenceMissing, RecordRunMode.Development,
                RecordPersistenceState.Committed, "Fail", 3, 1, 0, 1, 0,
                DateTimeOffset.UtcNow,
                new[] { new RunArchiveIntegrityIssue("EVIDENCE_MISSING", "Missing evidence", true) }));
        public Task<string> ExportFullReportJsonAsync(string runId, CancellationToken ct = default) => Task.FromResult("{\"stepFacts\":[{\"stepId\":\"step-a\",\"expected\":\"expect-a\",\"actual\":\"actual-a\",\"evidenceQuality\":\"Unknown\"},{\"stepId\":\"step-b\",\"expected\":\"expect-b\",\"actual\":\"actual-b\"}]}");
        public Task<string> ExportFullReportHtmlAsync(string runId, CancellationToken ct = default) => Task.FromResult("<p>Mock</p>");
        public Task ExportReportAsync(string runId, string destination, bool html, CancellationToken ct = default) => Task.CompletedTask;
    }
}
