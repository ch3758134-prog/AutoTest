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
        public Task<string> ExportFullReportJsonAsync(string runId, CancellationToken ct = default) => Task.FromResult("{}");
        public Task<string> ExportFullReportHtmlAsync(string runId, CancellationToken ct = default) => Task.FromResult("<p>Mock</p>");
        public Task ExportReportAsync(string runId, string destination, bool html, CancellationToken ct = default) => Task.CompletedTask;
    }
}
