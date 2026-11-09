using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Desktop.Bench;
using Xunit;

namespace AutoTest.Desktop.Tests.Bench;

public sealed class BenchEvidenceExportTests
{
    [Fact]
    public void JsonExportPreservesSourceAndUnconfirmedPhysicalFacts()
    {
        var entry = new BenchEvidenceEntry(
            new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
            BenchEvidencePhase.TransportAccepted, BenchEvidenceSource.Mock,
            "revision-A", "controller", "state", "mainboard", 4,
            "op", "corr", "12 34", "56 78", "trace-4", null, null,
            "Mock ACK; NOT a physical observation");
        var json = BenchEvidenceExport.ToJson(new[] { entry });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(BenchEvidenceExport.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Mock", doc.RootElement.GetProperty("entries")[0].GetProperty("Source").GetString());
        Assert.Contains("PhysicalObserved", doc.RootElement.GetProperty("disclaimer").GetString());
        Assert.Equal("12 34", doc.RootElement.GetProperty("entries")[0].GetProperty("RequestHex").GetString());
    }

    [Fact]
    public void TsvExportEscapesCellsWithoutTruncatingCorrelationsOrTrace()
    {
        var entry = new BenchEvidenceEntry(DateTimeOffset.UtcNow,
            BenchEvidencePhase.Error, BenchEvidenceSource.Physical, "rev",
            null, null, "dev", 12, "op", "corr", "AA", "BB", "trace-22",
            "WinUsb_Initialize", "Win32:31", "first line\nsecond\tline");
        var tsv = BenchEvidenceExport.ToTsv(new[] { entry });
        Assert.Contains("WinUsb_Initialize", tsv);
        Assert.Contains("Win32:31", tsv);
        Assert.Contains("trace-22", tsv);
        Assert.Contains(@"first line\nsecond\tline", tsv);
        Assert.Equal(2, tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task AcceptedMockRequestProducesTwoEventsButNoPhysicalVerdict()
    {
        var port = new EvidencePort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States.Single());

        Assert.Contains(vm.Evidence, e => e.Phase == BenchEvidencePhase.Requested);
        var accepted = Assert.Single(vm.Evidence.Where(e => e.Phase == BenchEvidencePhase.TransportAccepted));
        Assert.Equal("corr-1", accepted.CorrelationId);
        Assert.Equal("01", accepted.RequestHex);
        Assert.Equal("02", accepted.ResponseHex);
        Assert.Equal(BenchEvidenceSource.Mock, accepted.Source);
        Assert.Contains("UNKNOWN", vm.PhysicalFact);
        Assert.Contains("UNKNOWN", vm.EcuFact);
        Assert.Contains("corr-1", vm.ExportEvidenceJson());
    }

    [Fact]
    public async Task FilteringDoesNotRemoveEventsFromFullExport()
    {
        var vm = new BenchWorkbenchViewModel(new EvidencePort());
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States.Single());
        Assert.True(vm.Evidence.Count >= 2);
        vm.EvidencePhaseFilter = "请求";
        Assert.All(vm.VisibleEvidence, e => Assert.Equal(BenchEvidencePhase.Requested, e.Phase));
        Assert.Contains("corr-1", vm.ExportEvidenceTsv());
        vm.EvidenceSearch = "missing-correlation";
        Assert.Empty(vm.VisibleEvidence);
        Assert.Contains("corr-1", vm.ExportEvidenceTsv());
    }

    [Fact]
    public async Task NativeFailureKeepsStageAndDeviceIdentityInExport()
    {
        var vm = new BenchWorkbenchViewModel(new EvidencePort { ConnectFails = true });
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal("连接失败", vm.ConnectionStatus);
        var failure = Assert.Single(vm.Evidence.Where(e => e.FailureStage == "WinUsb_Initialize"));
        Assert.Equal("Win32:31", failure.ErrorCode);
        Assert.Equal("mainboard", failure.DeviceIdentity);
        Assert.Contains("WinUsb_Initialize", vm.ExportEvidenceTsv());
    }

    [Fact]
    public async Task ManualVoltageIsExplicitlySeparatedFromPhysicalObservation()
    {
        var port = new EvidencePort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States.Single());
        vm.OperatorName = "operator-local-only";
        vm.Voltages[0].Value = "2.5";
        await vm.SaveMeasurementAsync();
        Assert.NotNull(port.Saved);
        Assert.Equal(2.5m, port.Saved!.Voltages[0].Volts);
        var saved = Assert.Single(vm.Evidence.Where(e => e.Phase == BenchEvidencePhase.ManualMeasurement));
        Assert.Contains("CH1=2.5V", saved.Summary);
        Assert.DoesNotContain("operator-local-only", vm.ExportEvidenceJson());
        Assert.Contains("UNKNOWN", vm.PhysicalFact);
    }

    private sealed class EvidencePort : IBenchWorkbenchPort
    {
        public bool ConnectFails { get; init; }
        public BenchManualMeasurement? Saved { get; private set; }
        public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<BenchConfiguration?>(new BenchConfiguration("Project", "ECU", "rev-1",
                new[] { new BenchControllerDefinition("c", "Control",
                    new[] { new BenchStateDefinition("s", "On", 0x01, BenchInteraction.Latching, null) }) }));
        public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected, null, null, null, null, null));
        public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BenchDevice>>(ConnectFails
                ? new[] { new BenchDevice("mainboard", "Mainboard") }
                : Array.Empty<BenchDevice>());
        public Task<BenchConnectionSnapshot> ConnectAsync(string identity, CancellationToken cancellationToken) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Faulted, identity, 7,
                "WinUsb_Initialize", 31, "Native interface init failed"));
        public Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No hardware");
        public Task<BenchOperationResult> ApplyStateAsync(string revision, string controllerId, string stateId,
            BenchEvidenceSource source, CancellationToken cancellationToken) =>
            Task.FromResult(new BenchOperationResult(true, "op-1", "corr-1", 2,
                "01", "02", "trace-2", null, null, source, "Mock ACK"));
        public Task SaveManualMeasurementAsync(BenchManualMeasurement measurement, CancellationToken cancellationToken)
        {
            Saved = measurement;
            return Task.CompletedTask;
        }
    }
}
