using AutoTest.Desktop.Tools.Uds;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Uds;

public sealed class UdsToolViewModelTests
{
    [Fact]
    public async Task PresetCanExecuteAndDisplaysMockPositiveResponse()
    {
        var vm=new UdsToolViewModel();
        Assert.False(vm.PhysicalTxEnabled);
        Assert.True(vm.SendCommand.CanExecute());
        await vm.SendAsync();
        Assert.Equal("MOCK_ACCEPTED",vm.Status);
        Assert.StartsWith("62 F1 90",vm.ResponseHex);
        Assert.NotEmpty(vm.TraceRows);
    }

    [Fact]
    public async Task UnknownDidDoesNotAppearAsSuccessfulRead()
    {
        var vm=new UdsToolViewModel { RequestHex="22 AA BB" };
        await vm.SendAsync();
        Assert.Equal("MOCK_REJECTED",vm.Status);
        Assert.Equal("0x31",vm.Nrc);
    }

    [Fact]
    public void CanEditAndDeleteSavedServices()
    {
        var vm=new UdsToolViewModel { ServiceName="New DID",RequestHex="22 12 34" };
        int before=vm.Services.Count;
        vm.SaveService();
        Assert.Equal(before+1,vm.Services.Count);
        vm.SelectedService=Assert.Single(vm.Services, x => x.Name == "New DID");
        vm.DeleteService();
        Assert.Equal(before,vm.Services.Count);
    }

    [Fact]
    public async Task DiagnosticTimelineCanBeFilteredAndExportedAsMockOnly()
    {
        var vm=new UdsToolViewModel { DiagnosticPath=Path.Combine(Path.GetTempPath(),
            "r0_uds_"+Guid.NewGuid().ToString("N")+".json") };
        try
        {
            await vm.SendAsync();
            Assert.NotEmpty(vm.DiagnosticEvents);
            Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="ISOTP" && x.Direction=="HOST TX");
            Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="UDS" && x.Direction=="RESPONSE");
            Assert.Equal("MOCK_GROUPED_FRAMES_NO_INTERLEAVED_TIMESTAMPS",vm.TraceQuality);
            vm.TraceFilter="ISOTP";
            Assert.NotEmpty(vm.VisibleEvents);
            Assert.All(vm.VisibleEvents,x=>Assert.Equal("ISOTP",x.Layer));
            await vm.ExportDiagnosticAsync();
            var json=await File.ReadAllTextAsync(vm.DiagnosticPath);
            Assert.Contains("PhysicalTxVerified",json);
            Assert.Contains("false",json);
            Assert.Contains("CorrelationId",json);
            Assert.Contains("HOST_UI_OBSERVATION",json);
        }
        finally { if(File.Exists(vm.DiagnosticPath)) File.Delete(vm.DiagnosticPath); }
    }

    [Fact]
    public async Task SecurityAccessRawDataIsRedactedFromDiagnosticExport()
    {
        var vm=new UdsToolViewModel { RequestHex="27 01",
            DiagnosticPath=Path.Combine(Path.GetTempPath(),
                "r0_uds_sensitive_"+Guid.NewGuid().ToString("N")+".json") };
        try
        {
            await vm.SendAsync();
            Assert.Contains("REDACTED",vm.ResponseHex);
            Assert.All(vm.DiagnosticEvents.Where(x=>x.Layer=="ISOTP"),
                x=>Assert.Contains("REDACTED",x.RawHex));
            await vm.ExportDiagnosticAsync();
            string json=await File.ReadAllTextAsync(vm.DiagnosticPath);
            Assert.Contains("REDACTED",json);
            Assert.DoesNotContain("27 01",json);
            Assert.DoesNotContain("67 01",json);
        }
        finally { if(File.Exists(vm.DiagnosticPath)) File.Delete(vm.DiagnosticPath); }
    }

    [Fact]
    public async Task PendingFrameIsDecodedAsNrc78WithoutClaimingPreciseBusTiming()
    {
        var vm = new UdsToolViewModel { InjectPending = true };
        await vm.SendAsync();
        Assert.Equal("MOCK_ACCEPTED",vm.Status);
        Assert.Contains(vm.DiagnosticEvents, e=>e.Layer=="ISOTP" &&
            e.DecodedSummary.Contains("RESPONSE_PENDING"));
        Assert.Contains("NO_INTERLEAVED_TIMESTAMPS",vm.TraceQuality);
    }

    [Fact]
    public async Task InvalidSourceNeverCreatesPhysicalTransmitEvidence()
    {
        var vm = new UdsToolViewModel { SourceMode = "Physical" };
        await vm.SendAsync();
        Assert.Equal("WORKBENCH_ERROR",vm.Status);
        Assert.False(vm.PhysicalTxEnabled);
        Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="ERROR");
    }

    [Fact]
    public async Task PendingResponseEventsHaveOneCorrelationIdAndP2StarDisclosure()
    {
        var vm=new UdsToolViewModel { InjectPending=true, P2StarMs="7500" };
        await vm.SendAsync();
        var correlation=vm.LastCorrelationId;
        Assert.NotEqual("—",correlation);
        Assert.All(vm.DiagnosticEvents,x=>Assert.Equal(correlation,x.CorrelationId));
        Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="UDS" &&
            x.Direction=="RESPONSE_PENDING" && x.RawHex=="7F 22 78" &&
            x.DecodedSummary.Contains("7500"));
        Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="UDS" &&
            x.Direction=="POSITIVE");
    }

    [Fact]
    public async Task PhysicalModeRejectedBeforeARequestIsReportedAsIssued()
    {
        var vm=new UdsToolViewModel { SourceMode="Physical" };
        await vm.SendAsync();
        Assert.False(vm.PhysicalTxEnabled);
        Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="POLICY" &&
            x.Direction=="REJECTED");
        Assert.DoesNotContain(vm.DiagnosticEvents,x=>x.Layer=="UDS" &&
            x.Direction=="REQUEST");
    }

    [Fact]
    public async Task MockTimeoutShowsLayerAndUncertainStatusInConsole()
    {
        var vm=new UdsToolViewModel { NoResponse=true, P2Ms="25" };
        await vm.SendAsync();
        Assert.Equal("MOCK_UNCERTAIN",vm.Status);
        Assert.Equal("ISOTP_TIMEOUT",vm.FailureLayer);
        Assert.Contains("ISO-TP frame/flow-control timeout",vm.DiagnosticDetail);
        Assert.False(vm.PhysicalTxEnabled);
    }

    [Fact]
    public async Task VinResponseIsRedactedFromExportButRemainVisibleLocally()
    {
        var path=Path.Combine(Path.GetTempPath(),"r0_uds_vin_"+
            Guid.NewGuid().ToString("N")+".json");
        var vm=new UdsToolViewModel { DiagnosticPath=path };
        try
        {
            await vm.SendAsync();
            Assert.StartsWith("62 F1 90",vm.ResponseHex);
            Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="UDS" &&
                x.Direction=="POSITIVE" && x.Sensitive);
            await vm.ExportDiagnosticAsync();
            string json=await File.ReadAllTextAsync(path);
            Assert.Contains("REDACTED",json);
            Assert.DoesNotContain("4D 4F 43 4B 5F 45 43 55 5F 41",json);
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task MockTimeoutBlocksFurtherSendUntilResetAndDoesNotFabricateNewTx()
    {
        var vm=new UdsToolViewModel { NoResponse=true, P2Ms="25" };
        await vm.SendAsync();
        Assert.Equal("MOCK_UNCERTAIN",vm.Status);
        Assert.True(vm.MockResetRequired);
        int attempted=vm.DiagnosticEvents.Count(x=>x.Layer=="UDS" && x.Direction=="REQUEST");
        vm.NoResponse=false;
        await vm.SendAsync();
        Assert.Equal("MOCK_SESSION_REQUIRES_RESET",vm.FailureLayer);
        Assert.Equal(attempted,
            vm.DiagnosticEvents.Count(x=>x.Layer=="UDS" && x.Direction=="REQUEST"));
        Assert.Contains(vm.DiagnosticEvents,x=>x.Layer=="POLICY" &&
            x.ErrorCode=="MOCK_CHANNEL_UNCERTAIN_RESET_REQUIRED");
        await vm.ResetAsync();
        Assert.False(vm.MockResetRequired);
        await vm.SendAsync();
        Assert.Equal("MOCK_ACCEPTED",vm.Status);
    }
}
