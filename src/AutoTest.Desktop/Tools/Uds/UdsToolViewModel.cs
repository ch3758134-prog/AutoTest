using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using AutoTest.Specialists.Uds.Workbench;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Uds;

// INTEGRATION registers the View/VM in the shared Prism Shell; this VM never
// creates a physical CAN/USB session. Standalone instance is explicitly MOCK.
public sealed class UdsToolViewModel : BindableBase
{
    private readonly UdsDiagnosticWorkspace _backend;
    private readonly UdsReplayWorkspace _replay = new();
    private bool _isBusy, _injectPending, _noResponse, _injectNrc;
    private CancellationTokenSource? _activeRequest;
    private string _diagnosticPath="", _traceFilter="", _traceQuality="NOT_CAPTURED";
    private Guid _lastCorrelationId;
    private bool _lastRequestSensitive;
    private bool _lastRequestVin;
    private int _droppedTraceEvents;
    private const int MaxUiDiagnosticEvents=10000;
    private string _profileName="Mock ECU", _mockIdentity="MOCK_ECU_A",
        _txId="7E0", _rxId="7E8", _p2Ms="1000", _p2StarMs="5000",
        _requestHex="22 F1 90", _expectedHex="", _serviceName="Read VIN (Mock)",
        _documentPath="", _status="MOCK_READY", _responseHex="—",
        _failureLayer="NONE", _diagnosticDetail="",
        _nrc="—", _expectedMatch="NOT_SPECIFIED", _error="",
        _faultSid="22", _faultCode="31", _session="Default / Unknown",
        _sourceMode="Mock", _replayPath="";
    private UdsServiceDefinition? _selectedService;
    public UdsToolViewModel() : this(new UdsDiagnosticWorkspace()) { }
    public UdsToolViewModel(UdsDiagnosticWorkspace backend)
    {
        _backend = backend;
        Services.Add(new("Read VIN (Mock)","22 F1 90",""));
        Services.Add(new("Read DTC (Mock)","19 02 FF",""));
        Services.Add(new("Tester Present (Mock)","3E 00",""));
        Services.Add(new("Default session (Mock)","10 01",""));
        SendCommand = new DelegateCommand(() => _ = SendAsync(), () => !IsBusy);
        ResetCommand = new DelegateCommand(() => _ = ResetAsync(), () => !IsBusy);
        SaveServiceCommand = new DelegateCommand(SaveService, () => !IsBusy);
        DeleteServiceCommand = new DelegateCommand(DeleteService, () => !IsBusy);
        SaveDocumentCommand = new DelegateCommand(() => _ = SaveDocumentAsync(), () => !IsBusy);
        LoadDocumentCommand = new DelegateCommand(() => _ = LoadDocumentAsync(), () => !IsBusy);
        LoadReplayCommand = new DelegateCommand(() => _ = LoadReplayAsync(), () => !IsBusy);
        CancelRequestCommand = new DelegateCommand(CancelRequest, () => IsBusy && _activeRequest is not null);
        ExportDiagnosticCommand = new DelegateCommand(() => _ = ExportDiagnosticAsync(), () => !IsBusy && DiagnosticEvents.Count > 0);
        CopyTraceCommand = new DelegateCommand(CopyTrace, () => DiagnosticEvents.Count > 0);
    }
    public ObservableCollection<UdsServiceDefinition> Services { get; } = new();
    public ObservableCollection<UdsCanTraceRow> TraceRows { get; } = new();
    public ObservableCollection<UdsDiagnosticEvent> DiagnosticEvents { get; } = new();
    public ObservableCollection<UdsDiagnosticEvent> VisibleEvents { get; } = new();
    public DelegateCommand SendCommand { get; }
    public DelegateCommand ResetCommand { get; }
    public DelegateCommand SaveServiceCommand { get; }
    public DelegateCommand DeleteServiceCommand { get; }
    public DelegateCommand SaveDocumentCommand { get; }
    public DelegateCommand LoadDocumentCommand { get; }
    public DelegateCommand LoadReplayCommand { get; }
    public DelegateCommand CancelRequestCommand { get; }
    public DelegateCommand ExportDiagnosticCommand { get; }
    public DelegateCommand CopyTraceCommand { get; }
    public IReadOnlyList<string> SourceModes { get; } = ["Mock", "Replay"];
    public bool PhysicalTxEnabled => false;
    public string SourceLabel => SourceMode=="Replay" ? "REPLAY / NO PHYSICAL CAN TX" : "MOCK / NO PHYSICAL CAN TX";
    public bool IsBusy { get => _isBusy; private set
        { if (SetProperty(ref _isBusy,value))
          foreach(var command in Commands()) command.RaiseCanExecuteChanged(); } }
    private IEnumerable<DelegateCommand> Commands() => [SendCommand, ResetCommand,
        SaveServiceCommand, DeleteServiceCommand, SaveDocumentCommand, LoadDocumentCommand, LoadReplayCommand,
        CancelRequestCommand, ExportDiagnosticCommand];
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName,value); }
    public string MockIdentity { get => _mockIdentity; set => SetProperty(ref _mockIdentity,value); }
    public string AddressingDescription
    {
        get
        {
            try
            {
                uint tx=CanId(TxId), rx=CanId(RxId);
                if (tx>0x1FFFFFFF || rx>0x1FFFFFFF) return "INVALID CAN ID";
                if ((tx>0x7FF)!=(rx>0x7FF)) return "MIXED 11/29-BIT: NOT SUPPORTED";
                return tx>0x7FF ? "ISO-TP NORMAL / 29-BIT CAN ID" :
                    "ISO-TP NORMAL / 11-BIT CAN ID";
            }
            catch (FormatException) { return "INVALID CAN ID"; }
            catch (OverflowException) { return "INVALID CAN ID"; }
        }
    }
    public string TxId { get => _txId; set { if (SetProperty(ref _txId,value)) RaisePropertyChanged(nameof(AddressingDescription)); } }
    public string RxId { get => _rxId; set { if (SetProperty(ref _rxId,value)) RaisePropertyChanged(nameof(AddressingDescription)); } }
    public string P2Ms { get => _p2Ms; set => SetProperty(ref _p2Ms,value); }
    public string P2StarMs { get => _p2StarMs; set => SetProperty(ref _p2StarMs,value); }
    public string RequestHex { get => _requestHex; set => SetProperty(ref _requestHex,value); }
    public string ExpectedHex { get => _expectedHex; set => SetProperty(ref _expectedHex,value); }
    public string ServiceName { get => _serviceName; set => SetProperty(ref _serviceName,value); }
    public string DocumentPath { get => _documentPath; set => SetProperty(ref _documentPath,value); }
    public string SourceMode { get => _sourceMode; set { if(SetProperty(ref _sourceMode,value)) RaisePropertyChanged(nameof(SourceLabel)); } }
    public string ReplayPath { get => _replayPath; set => SetProperty(ref _replayPath,value); }
    public string DiagnosticPath { get => _diagnosticPath; set => SetProperty(ref _diagnosticPath,value); }
    public string TraceQuality { get => _traceQuality; private set => SetProperty(ref _traceQuality,value); }
    public int DroppedTraceEvents => _droppedTraceEvents;
    public string LastCorrelationId => _lastCorrelationId == Guid.Empty ? "—" : _lastCorrelationId.ToString("D");
    public string TraceFilter { get => _traceFilter; set { if (SetProperty(ref _traceFilter,value)) RefreshFilter(); } }
    public string FaultSid { get => _faultSid; set => SetProperty(ref _faultSid,value); }
    public string FaultCode { get => _faultCode; set => SetProperty(ref _faultCode,value); }
    public bool InjectNrc { get => _injectNrc; set => SetProperty(ref _injectNrc,value); }
    public string Session { get => _session; private set => SetProperty(ref _session,value); }
    public bool InjectPending { get => _injectPending; set => SetProperty(ref _injectPending,value); }
    public bool NoResponse { get => _noResponse; set => SetProperty(ref _noResponse,value); }
    public string Status { get => _status; private set => SetProperty(ref _status,value); }
    public string ResponseHex { get => _responseHex; private set => SetProperty(ref _responseHex,value); }
    public string Nrc { get => _nrc; private set => SetProperty(ref _nrc,value); }
    public string ExpectedMatch { get => _expectedMatch; private set => SetProperty(ref _expectedMatch,value); }
    public string Error { get => _error; private set => SetProperty(ref _error,value); }
    public string FailureLayer { get => _failureLayer; private set => SetProperty(ref _failureLayer,value); }
    public string DiagnosticDetail { get => _diagnosticDetail; private set => SetProperty(ref _diagnosticDetail,value); }
    public UdsServiceDefinition? SelectedService
    {
        get => _selectedService;
        set
        {
            if (!SetProperty(ref _selectedService,value) || value is null) return;
            ServiceName=value.Name;
            RequestHex=value.RequestHex;
            ExpectedHex=value.ExpectedHex;
        }
    }
    private static uint CanId(string value) => uint.Parse(value.Trim().Replace("0x","",StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber,CultureInfo.InvariantCulture);
    private static int Ms(string value) => int.Parse(value,CultureInfo.InvariantCulture);
    private UdsToolProfile Profile() => new(ProfileName,MockIdentity,CanId(TxId),CanId(RxId),
        Ms(P2Ms),Ms(P2StarMs));
    private UdsToolDocument Document() => new(1,Profile(),Services.ToList());
    private void Append(string source, string layer, string direction, Guid correlation,
        string rawHex, string detail, string error = "")
    {
        if (DiagnosticEvents.Count >= MaxUiDiagnosticEvents)
        {
            _droppedTraceEvents++;
            RaisePropertyChanged(nameof(DroppedTraceEvents));
            TraceQuality="INCOMPLETE_UI_TRACE_OVERFLOW";
            return;
        }
        bool sensitive=_lastRequestSensitive ||
            (_lastRequestVin && (layer=="ISOTP" ||
                (layer=="UDS" && direction is "POSITIVE" or "RESPONSE")));
        var item = new UdsDiagnosticEvent(DateTimeOffset.UtcNow, source, layer, direction,
            correlation == Guid.Empty ? "" : correlation.ToString("D"),
            UdsWorkbenchEvidence.SafeHex(_lastRequestSensitive,rawHex), detail,error,
            sensitive);
        DiagnosticEvents.Add(item);
        if (MatchesFilter(item)) VisibleEvents.Add(item);
        ExportDiagnosticCommand.RaiseCanExecuteChanged();
        CopyTraceCommand.RaiseCanExecuteChanged();
    }
    private bool MatchesFilter(UdsDiagnosticEvent item) =>
        string.IsNullOrWhiteSpace(TraceFilter) ||
        new[] {item.Source,item.Layer,item.Direction,item.CorrelationId,
            item.RawHex,item.DecodedSummary,item.ErrorCode}.Any(x =>
                x.Contains(TraceFilter,StringComparison.OrdinalIgnoreCase));
    private void RefreshFilter()
    {
        VisibleEvents.Clear();
        foreach (var entry in DiagnosticEvents.Where(MatchesFilter)) VisibleEvents.Add(entry);
    }
    public void CancelRequest()
    {
        if (!IsBusy || _activeRequest is null) return;
        Append(SourceMode,"CONTROL","CANCEL_REQUEST",_lastCorrelationId,"",
            "User requested cancellation; not a confirmed ECU stop.");
        _activeRequest.Cancel();
    }
    public void CopyTrace()
    {
        try
        {
            if (DiagnosticEvents.Count == 0) return;
            Clipboard.SetText(string.Join(Environment.NewLine,VisibleEvents.Select(x =>
                $"{x.TimestampUtc:O} | {x.CorrelationId} | {x.Layer} | {x.Direction} | "+
                $"{UdsWorkbenchEvidence.SafeHex(x.Sensitive,x.RawHex)} | {x.DecodedSummary} | {x.ErrorCode}")));
            Status = "TRACE_COPIED";
        }
        catch (Exception ex) { Status="COPY_ERROR"; Error=ex.Message; }
    }
    public async Task ExportDiagnosticAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await UdsWorkbenchEvidence.ExportAsync(DiagnosticPath,SourceMode,ProfileName,
                _droppedTraceEvents>0 ? "INCOMPLETE_UI_TRACE_OVERFLOW; DROPPED="+
                    _droppedTraceEvents : TraceQuality,DiagnosticEvents.ToArray());
            Status="DIAGNOSTIC_EXPORTED"; Error="";
        }
        catch (Exception ex) { Status="DIAGNOSTIC_EXPORT_ERROR"; Error=ex.Message; }
        finally { IsBusy=false; }
    }
    public async Task SendAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeRequest=linked;
        _lastCorrelationId=Guid.NewGuid();
        RaisePropertyChanged(nameof(LastCorrelationId));
        IsBusy=true; CancelRequestCommand.RaiseCanExecuteChanged();
        _lastRequestSensitive=false;
        _lastRequestVin=false;
        TraceQuality=_droppedTraceEvents>0 ? "INCOMPLETE_UI_TRACE_OVERFLOW" : "NOT_CAPTURED";
        Status="REQUESTING"; Error=""; Nrc="—"; ResponseHex="—";
        FailureLayer="NONE"; DiagnosticDetail="";
        var watch=Stopwatch.StartNew();
        string source=SourceMode;
        try
        {
            var request=UdsHex.Parse(RequestHex);
            _lastRequestSensitive=UdsWorkbenchEvidence.RequiresRedaction(request);
            _lastRequestVin=request.Length>=3 && request[0]==0x22 &&
                request[1]==0xF1 && request[2]==0x90;
            if (source is not ("Mock" or "Replay"))
            {
                Append(source,"POLICY","REJECTED",_lastCorrelationId,"",
                    "Physical UDS transport is not connected or authorized.",
                    "PHYSICAL_UDS_NOT_AUTHORIZED");
                throw new InvalidOperationException("Physical UDS Tx is not configured or authorized.");
            }
            Append(source,"UDS","REQUEST",_lastCorrelationId,UdsHex.Format(request),
                "Requested "+source+" operation; no physical Tx; TxId="+TxId+" RxId="+RxId+
                " P2="+P2Ms+"ms P2*="+P2StarMs+"ms");
            UdsToolExchange exchange;
            if (source=="Replay") exchange=_replay.Consume(RequestHex,ExpectedHex,_lastCorrelationId);
            else if (source=="Mock")
            {
                _backend.SetNoResponse(NoResponse);
                if (InjectPending && InjectNrc) throw new InvalidOperationException("Select NRC fault OR pending, not both.");
                if (InjectPending) _backend.InjectNextPending(request[0]);
                if (InjectNrc) _backend.InjectNextNrc(checked((byte)CanId(FaultSid)),
                    checked((byte)CanId(FaultCode)));
                exchange=await _backend.SendMockAsync(Profile(),RequestHex,ExpectedHex,linked.Token,_lastCorrelationId);
            }
            else throw new InvalidOperationException("Physical UDS Tx is not configured or authorized.");
            if (exchange.CorrelationId != _lastCorrelationId)
                throw new InvalidOperationException("UDS_CORRELATION_ID_MISMATCH");
            ApplyExchange(exchange,request);
            TraceQuality=source=="Mock" ? "MOCK_GROUPED_FRAMES_NO_INTERLEAVED_TIMESTAMPS"
                : "REPLAY_SOURCE_TIMING_NOT_VERIFIED";
            foreach (string responseText in exchange.ResponseSequence)
            {
                byte[] pdu=UdsHex.Parse(responseText);
                bool pending=pdu.Length==3 && pdu[0]==0x7F && pdu[1]==request[0] && pdu[2]==0x78;
                string stage=pending ? "RESPONSE_PENDING" : (pdu[0]==0x7F ? "NEGATIVE" : "POSITIVE");
                Append(source,"UDS",stage,_lastCorrelationId,responseText,
                    pending ? "NRC=0x78; subsequent response uses configured P2*="+P2StarMs+
                        "ms (actual ECU timing unavailable)"
                        : "UDS response PDU from "+source);
            }
            Append(source,"UDS","RESPONSE",_lastCorrelationId,exchange.ResponseHex,
                "Outcome="+exchange.Outcome+" NRC="+exchange.Nrc+
                " layer="+exchange.FailureLayer+
                " elapsed(host)="+watch.ElapsedMilliseconds+"ms; this is not ECU P2 timing",
                exchange.ErrorCode);
            foreach(var frame in exchange.Frames)
                Append(source,"ISOTP",frame.Direction,_lastCorrelationId,frame.Hex,
                    UdsWorkbenchEvidence.DescribeFrame(frame.Hex));
            if (_droppedTraceEvents>0) TraceQuality="INCOMPLETE_UI_TRACE_OVERFLOW";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            Status="CANCELLED"; Error="REQUEST_CANCELLED";
            FailureLayer="UDS_REQUEST_CANCELLED";
            DiagnosticDetail="Cancellation requested locally; remote effects unconfirmed.";
            Append(source,"CONTROL","CANCELLED",_lastCorrelationId,"",
                "Request was cancelled; any remote effect is unconfirmed.",Error);
        }
        catch (Exception ex)
        {
            Status="WORKBENCH_ERROR"; Error=ex.GetType().Name+": "+ex.Message;
            FailureLayer=ex switch
            {
                FormatException or ArgumentException => "UI_INPUT",
                EndOfStreamException => "REPLAY_EXHAUSTED",
                InvalidDataException => "REPLAY_DOCUMENT",
                InvalidOperationException when source=="Replay" => "REPLAY_REQUEST",
                InvalidOperationException when source=="Physical" => "SAFETY_POLICY",
                _ => "WORKBENCH_UNISOLATED"
            };
            DiagnosticDetail=ex.Message;
            Append(source,"ERROR","FAILED",_lastCorrelationId,"",
                "Exception at diagnostic workbench; elapsed="+watch.ElapsedMilliseconds+"ms",Error);
        }
        finally
        {
            _activeRequest=null;
            IsBusy=false;
            CancelRequestCommand.RaiseCanExecuteChanged();
        }
    }
    private void ApplyExchange(UdsToolExchange exchange,byte[] request)
    {
        ResponseHex=string.IsNullOrEmpty(exchange.ResponseHex) ? "(no confirmed response)" :
            UdsWorkbenchEvidence.SafeHex(_lastRequestSensitive,exchange.ResponseHex);
        Status=exchange.Source.ToUpperInvariant()+"_"+
            (exchange.ExpectedMatched==false ? "EXPECTED_MISMATCH" : exchange.Outcome.ToUpperInvariant());
        Nrc=exchange.Nrc;
        ExpectedMatch=exchange.ExpectedMatched is null ? "NOT_SPECIFIED" :
            exchange.ExpectedMatched.Value ? "MATCH" : "MISMATCH";
        Error=exchange.ErrorCode;
        FailureLayer=exchange.FailureLayer;
        DiagnosticDetail=exchange.DiagnosticDetail;
        if (exchange.Outcome=="Accepted" && request.Length>=2 && request[0]==0x10)
            Session=request[1] switch {
                0x01=>"Default ("+exchange.Source+")",
                0x02=>"Programming ("+exchange.Source+")",
                _=>"Other ("+exchange.Source+")" };
        foreach(var frame in exchange.Frames)
        {
            // Legacy raw frame collection remains bounded too, even though the new
            // UI uses DiagnosticEvents. Avoid silent unbounded memory growth.
            if (TraceRows.Count >= MaxUiDiagnosticEvents)
            {
                TraceRows.RemoveAt(0);
                _droppedTraceEvents++;
                RaisePropertyChanged(nameof(DroppedTraceEvents));
                TraceQuality="INCOMPLETE_UI_TRACE_OVERFLOW";
            }
            TraceRows.Add(_lastRequestSensitive ?
                frame with { Hex="[REDACTED: SENSITIVE UDS SERVICE]" } : frame);
        }
    }
    public async Task LoadReplayAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try { await _replay.LoadAsync(ReplayPath); SourceMode="Replay";
            Status="REPLAY_LOADED_"+_replay.Count; Error=""; TraceRows.Clear(); }
        catch(Exception ex) { Status="REPLAY_ERROR"; Error=ex.Message; }
        finally { IsBusy=false; }
    }
    public async Task ResetAsync()
    {
        if (IsBusy) return;
        IsBusy=true; Error="";
        try { await _backend.ResetMockAsync(MockIdentity); TraceRows.Clear(); Status="MOCK_RESET";
            ResponseHex="—"; Nrc="—"; ExpectedMatch="NOT_SPECIFIED"; Session="Default / Unknown"; }
        catch(Exception ex) { Status="MOCK_ERROR"; Error=ex.Message; }
        finally { IsBusy=false; }
    }
    public void SaveService()
    {
        if (IsBusy) return;
        try
        {
            if (string.IsNullOrWhiteSpace(ServiceName)) throw new ArgumentException("Service name required.");
            var definition=new UdsServiceDefinition(ServiceName,RequestHex,ExpectedHex);
            var catalog=new AutoTest.Specialists.Uds.UdsServiceCatalog();
            var request=UdsHex.Parse(RequestHex);
            if (request.Length < catalog.Get(request[0]).MinimumRequestLength)
                throw new ArgumentException("UDS request is shorter than minimum service length.");
            if (!string.IsNullOrWhiteSpace(ExpectedHex)) _=UdsHex.Parse(ExpectedHex);
            int index=Services.ToList().FindIndex(x=>x.Name.Equals(ServiceName,StringComparison.OrdinalIgnoreCase));
            if (index>=0) Services[index]=definition; else Services.Add(definition);
            Status="SERVICE_SAVED"; Error="";
        }
        catch(Exception ex) { Status="VALIDATION_FAILED"; Error=ex.Message; }
    }
    public void DeleteService()
    {
        if (IsBusy || SelectedService is null) return;
        Services.Remove(SelectedService); SelectedService=null; Status="SERVICE_REMOVED";
    }
    public async Task SaveDocumentAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try { await UdsToolDocumentStore.SaveAsync(DocumentPath,Document());
            Status="DOCUMENT_SAVED"; Error=""; }
        catch(Exception ex) { Status="DOCUMENT_ERROR"; Error=ex.Message; }
        finally { IsBusy=false; }
    }
    public async Task LoadDocumentAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try
        {
            var doc=await UdsToolDocumentStore.LoadAsync(DocumentPath);
            ProfileName=doc.Profile.Name; MockIdentity=doc.Profile.MockIdentity;
            TxId=doc.Profile.TxId.ToString("X"); RxId=doc.Profile.RxId.ToString("X");
            P2Ms=doc.Profile.P2Ms.ToString(CultureInfo.InvariantCulture);
            P2StarMs=doc.Profile.P2StarMs.ToString(CultureInfo.InvariantCulture);
            Services.Clear(); foreach(var item in doc.Services) Services.Add(item);
            await _backend.ResetMockAsync(MockIdentity);
            TraceRows.Clear(); Status="DOCUMENT_LOADED"; Error="";
        }
        catch(Exception ex) { Status="DOCUMENT_ERROR"; Error=ex.Message; }
        finally { IsBusy=false; }
    }
}
