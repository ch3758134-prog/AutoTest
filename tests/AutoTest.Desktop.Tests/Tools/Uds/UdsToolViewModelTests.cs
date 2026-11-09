using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;
using AutoTest.Specialists.Uds.Workbench;
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
    // Test-only provider. No real USB/CAN is opened by this xUnit fake.
    private sealed class FakePhysicalProvider : IReadOnlyPhysicalUdsSessionProvider
    {
        public int Opens;
        public bool Pending;
        public bool MissingRx;
        public ReadOnlyPhysicalConnection? Current {get;set;} = new(
            "FAKE_P08_STATION",17,
            new UdsEndpointProfile(BusRole.DutCan,0x7E0,0x7E8,
                TimeSpan.FromMilliseconds(1000),TimeSpan.FromMilliseconds(5000),"Normal"),
            true,true,DateTimeOffset.UtcNow.AddMinutes(5));
        public ValueTask<IReadOnlyPhysicalUdsSession> OpenAsync(
            ReadOnlyDiagnosticSessionSpec spec,CancellationToken ct)
        {
            Opens++;
            if (Current is null) throw new InvalidOperationException("No fake station.");
            return ValueTask.FromResult<IReadOnlyPhysicalUdsSession>(
                new FakeSession(Current,Pending,MissingRx));
        }
    }

    private sealed class FakeChannel(bool pending) : IUdsMessageChannel
    {
        private int _received;
        public ValueTask SendPduAsync(ReadOnlyMemory<byte> pdu,CancellationToken ct) =>
            ValueTask.CompletedTask;
        public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout,CancellationToken ct)
        {
            if (pending && _received++==0)
                return ValueTask.FromResult<byte[]>([0x7F,0x22,0x78]);
            return ValueTask.FromResult<byte[]>([0x62,0xF1,0x90,0x41]);
        }
    }
    private sealed class FakeSession : IReadOnlyPhysicalUdsSession
    {
        private readonly UdsClient _client;
        private readonly ReadOnlyPhysicalConnection _context;
        private readonly bool _missingRx;
        private readonly bool _pending;
        public FakeSession(ReadOnlyPhysicalConnection context,bool pending,bool missingRx)
        {
            _context=context;
            _missingRx=missingRx;
            _pending=pending;
            _client=new UdsClient(new FakeChannel(pending));
        }
        public ReadOnlyPhysicalConnection Connection => _context;
        public IUdsClient Client => _client;
        public IReadOnlyList<UdsTraceEntry> SnapshotUds(Guid id) =>
            _client.Trace.Snapshot().Where(x=>x.CorrelationId==id).ToArray();
        public IReadOnlyList<PhysicalUdsBusEvent> SnapshotCan(Guid id)
        {
            var time=DateTimeOffset.UtcNow;
            var frames=new List<PhysicalUdsBusEvent> {
                new(time,"TX","CAN",BusRole.DutCan,0x7E0,false,
                    "03 22 F1 90","FAKE_USB_CAN_TX",_context.SessionGeneration,0,true)
            };
            if(!_missingRx)
            {
                if(_pending)
                    frames.Add(new(time,"RX","CAN",BusRole.DutCan,0x7E8,false,
                        "03 7F 22 78","FAKE_USB_CAN_PENDING",_context.SessionGeneration,0));
                frames.Add(new(time,"RX","CAN",BusRole.DutCan,0x7E8,false,
                    "04 62 F1 90 41","FAKE_USB_CAN_RX",_context.SessionGeneration,0));
            }
            return frames;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AuthorizedFakePhysicalSessionShowsBoundStationRawCanAndExportProvenance()
    {
        var provider=new FakePhysicalProvider();
        var vm=new UdsToolViewModel(new UdsDiagnosticWorkspace(),provider);
        Assert.True(vm.PhysicalTxEnabled);
        Assert.Contains("Physical",vm.SourceModes);
        vm.SourceMode="Physical";
        var output=Path.Combine(Path.GetTempPath(),
            "fake_p1_uds_"+Guid.NewGuid().ToString("N")+".json");
        vm.DiagnosticPath=output;
        try
        {
            await vm.SendAsync();
            Assert.Equal("PHYSICAL_ACCEPTED",vm.Status);
            Assert.Contains("Generation=17",vm.PhysicalStationStatus);
            Assert.Contains(vm.DiagnosticEvents,x=>x.Source=="Physical" &&
                x.Layer=="CAN" && x.Direction=="TX" && x.PhysicalEvidenceVerified &&
                x.CanId==0x7E0 && x.SessionGeneration==17);
            Assert.Contains(vm.DiagnosticEvents,x=>x.Source=="Physical" &&
                x.Layer=="CAN" && x.Direction=="RX" && x.TraceRef=="FAKE_USB_CAN_RX");
            Assert.Equal(1,provider.Opens);
            await vm.ExportDiagnosticAsync();
            var json=await File.ReadAllTextAsync(output);
            Assert.Contains("PhysicalTxVerified",json);
            Assert.Contains("FAKE_USB_CAN_RX",json);
            Assert.Contains("PHYSICAL_DEVICE_HOST_ARRIVAL_ONLY",json);
            Assert.Contains("REDACTED",json); // 0x22 F190 VIN privacy.
        }
        finally { if(File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public async Task PhysicalProgrammingSidDeniedBeforeProviderOpenAndBeforeTxClaim()
    {
        var provider=new FakePhysicalProvider();
        var vm=new UdsToolViewModel(new UdsDiagnosticWorkspace(),provider)
        {
            SourceMode="Physical", RequestHex="34 00 44"
        };
        await vm.SendAsync();
        Assert.Equal(0,provider.Opens);
        Assert.Equal("PHYSICAL_READONLY_DENIED",vm.FailureLayer);
        Assert.DoesNotContain(vm.DiagnosticEvents,x=>x.Layer=="UDS" &&
            x.Direction=="REQUEST" && x.Source=="Physical");
        Assert.False(vm.DiagnosticEvents.Any(x=>x.Layer=="CAN"));
    }

    [Fact]
    public async Task NoProviderOrUnapprovedStationLeavesPhysicalUnavailable()
    {
        var defaultVm=new UdsToolViewModel();
        Assert.DoesNotContain("Physical",defaultVm.SourceModes);
        Assert.False(defaultVm.PhysicalTxEnabled);

        var provider=new FakePhysicalProvider();
        provider.Current=provider.Current! with { ReadOnlyTxAuthorized=false };
        var vm=new UdsToolViewModel(new UdsDiagnosticWorkspace(),provider);
        Assert.False(vm.PhysicalTxEnabled);
        Assert.DoesNotContain("Physical",vm.SourceModes);
        vm.SourceMode="Physical";
        await vm.SendAsync();
        Assert.Equal(0,provider.Opens);
        Assert.Contains(vm.DiagnosticEvents,x=>x.Source=="Physical" &&
            x.Layer=="POLICY");
    }

    [Fact]
    public async Task FakePhysicalPendingIsVisibleAsNrc78BeforeFinalResponse()
    {
        var provider=new FakePhysicalProvider { Pending=true };
        var vm=new UdsToolViewModel(new UdsDiagnosticWorkspace(),provider)
        {
            SourceMode="Physical",P2StarMs="5000"
        };
        await vm.SendAsync();
        Assert.Equal("PHYSICAL_ACCEPTED",vm.Status);
        Assert.Contains(vm.DiagnosticEvents,e=>e.Source=="Physical" &&
            e.Layer=="UDS" && e.Direction=="RESPONSE_PENDING" &&
            e.DecodedSummary.Contains("P2*=5000"));
        Assert.Contains(vm.DiagnosticEvents,e=>e.Source=="Physical" &&
            e.Layer=="UDS" && e.Direction=="RESPONSE");
    }

    [Fact]
    public async Task FakePhysicalWithoutObservedCanRxNeverDisplaysAcceptedStatus()
    {
        var provider=new FakePhysicalProvider { MissingRx=true };
        var vm=new UdsToolViewModel(new UdsDiagnosticWorkspace(),provider)
        {
            SourceMode="Physical"
        };
        await vm.SendAsync();
        Assert.Equal("PHYSICAL_UNCERTAIN",vm.Status);
        Assert.Contains("INCOMPLETE",vm.TraceQuality);
        Assert.Contains("PHYSICAL_RAW_CAN_EVIDENCE_INCOMPLETE",vm.Error);
    }

}
