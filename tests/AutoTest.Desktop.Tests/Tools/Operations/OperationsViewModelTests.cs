using AutoTest.Desktop.Tools.Operations;
using AutoTest.Records.Operations;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Operations;

public sealed class OperationsViewModelTests
{
    [Fact]
    public async Task EmptyDatabaseIsHonestAndUiCanQueryWithoutConnectedDevice()
    {
        var vm = new OperationsViewModel(new FakeStore(), new FakeTraceReader(), new FakeExporter());
        await vm.RefreshAsync();
        Assert.Empty(vm.Entries);
        Assert.Contains("无匹配", vm.State);
        Assert.Equal("", vm.Error);
    }

    [Fact]
    public async Task NativeErrorAndTraceCanBeInspectedWithoutAssumingPhysicalSuccess()
    {
        var trace = new AuditTraceReference("trace-20261105.jsonl", 0, 20, new string('A',64));
        var evt = new OperationAuditEvent
        {
            EventId = Guid.NewGuid(), OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            OccurredUtc = DateTimeOffset.UtcNow, Kind = "NativeFailure", Summary = "USB init failed",
            Origin = AuditOrigin.Physical, Level = AuditLevel.Error,
            Outcome = AuditOutcome.Failed, FailureStage = "WinUsb_Initialize", NativeErrorCode = 31,
            Trace = trace
        };
        var vm = new OperationsViewModel(new FakeStore(evt), new FakeTraceReader(), new FakeExporter());
        await vm.RefreshAsync();
        vm.SelectedEvent = Assert.Single(vm.Entries);
        Assert.Contains("WinUsb_Initialize", vm.Detail);
        Assert.Contains("31", vm.Detail);
        await vm.ShowSelectedTraceAsync();
        Assert.Contains("RX", vm.TraceText);
        await vm.ShowTraceOwnersAsync();
        Assert.Contains(evt.EventId.ToString("D"), vm.TraceText);
    }

    [Fact]
    public async Task AuditQuerySupportsRunOperationAndDeviceFiltersWithoutHardware()
    {
        var store = new FakeStore();
        var operationId = Guid.NewGuid();
        var vm = new OperationsViewModel(store, new FakeTraceReader(), new FakeExporter())
        {
            RunFilter = "run-77", DeviceFilter = "main-board", OperationFilter = operationId.ToString("D"),
            OriginFilter = "Replay", OnlyFailures = true
        };
        await vm.RefreshAsync();
        Assert.Equal("run-77", store.LastQuery!.RunId);
        Assert.Equal("main-board", store.LastQuery.DeviceId);
        Assert.Equal(operationId, store.LastQuery.OperationId);
        Assert.Equal(AuditOrigin.Replay, store.LastQuery.Origin);
        Assert.True(store.LastQuery.FailuresOnly);
    }

    [Fact]
    public async Task EmptyEvidenceBundleIsExplicitlySoftwareOnlyAndCreatesPortableZip()
    {
        var filename = Path.Combine(Path.GetTempPath(), "autotest-operations-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var vm = new OperationsViewModel(new FakeStore(), new FakeTraceReader(), new FakeExporter())
            { ExportPath = filename };
            await vm.ExportBundleAsync();
            Assert.Equal("", vm.Error);
            Assert.Contains("0 条事件", vm.State);
            Assert.True(File.Exists(filename));
        }
        finally { if (File.Exists(filename)) File.Delete(filename); }
    }

    private sealed class FakeStore : IOperationAuditStore
    {
        private readonly OperationAuditEvent[] _events;
        public OperationAuditQuery? LastQuery { get; private set; }
        public FakeStore(params OperationAuditEvent[] events) => _events = events;
        public Task<AuditAppendReceipt> AppendAsync(OperationAuditEvent e, CancellationToken ct = default) =>
            Task.FromResult(new AuditAppendReceipt(e.EventId, true, false));
        public Task<OperationAuditEvent?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_events.FirstOrDefault(e => e.EventId == id));
        public Task<AuditPage> QueryAsync(OperationAuditQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new AuditPage(_events, null));
        }
        public Task<IReadOnlyList<OperationAuditEvent>> FindByTraceAsync(AuditTraceReference reference,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationAuditEvent>>(_events);
    }
    private sealed class FakeTraceReader : IOperationTraceReader
    {
        public Task<OperationRawTrace> ReadAsync(AuditTraceReference reference, CancellationToken ct = default) =>
            Task.FromResult(new OperationRawTrace
            {
                OperationId = Guid.NewGuid(), OccurredUtc = DateTimeOffset.UtcNow,
                Transport = "USB", Direction = "RX", Message = "failed"
            });
    }
    private sealed class FakeExporter : IOperationAuditExporter
    {
        public Task<int> ExportCsvAsync(OperationAuditQuery query, string destination, CancellationToken ct = default) => Task.FromResult(1);
        public Task<int> ExportJsonlAsync(OperationAuditQuery query, string destination, CancellationToken ct = default) => Task.FromResult(1);
    }
}
