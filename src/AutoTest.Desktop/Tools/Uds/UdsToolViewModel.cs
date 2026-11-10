using System.Collections.ObjectModel;
using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Flash;
using System.Globalization;
using AutoTest.Specialists.Uds;
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
    private CancellationTokenSource? _sendStop;
    private Func<HardwareBenchController?>? _physicalProvider;
    private Func<string,bool>? _confirmPhysical;
    private CanToolConfigurationSnapshot? _canToolProfile;
    private bool _isBusy, _injectPending, _noResponse, _injectNrc;
    private string _profileName="Mock ECU", _mockIdentity="MOCK_ECU_A",
        _txId="7E0", _rxId="7E8", _p2Ms="1000", _p2StarMs="5000",
        _requestHex="22 F1 90", _expectedHex="", _serviceName="Read VIN (Mock)",
        _documentPath="", _status="MOCK_READY", _responseHex="—",
        _nrc="—", _expectedMatch="NOT_SPECIFIED", _error="", _responseSummary="尚未收到诊断响应。",
        _faultSid="22", _faultCode="31", _session="Default / Unknown",
        _sourceMode="Mock", _replayPath="", _didParameter="F190", _dtcMaskParameter="FF";
    private UdsServiceDefinition? _selectedService;
    public UdsToolViewModel() : this(new UdsDiagnosticWorkspace()) { }
    public UdsToolViewModel(UdsDiagnosticWorkspace backend)
    {
        _backend = backend;
        Services.Add(new("Read VIN (Mock)","22 F1 90",""));
        Services.Add(new("Read DTC (Mock)","19 02 FF",""));
        Services.Add(new("Tester Present (Mock)","3E 00",""));
        Services.Add(new("Default session (Mock)","10 01",""));
        PrepareReadDidCommand = new DelegateCommand(() =>
            PrepareTypedRequest(() => UdsTypedRequestBuilder.ReadDataByIdentifier(
                checked((ushort)CanId(DidParameter))),"Read DID"), () => !IsBusy);
        PrepareReadDtcCommand = new DelegateCommand(() =>
            PrepareTypedRequest(() => UdsTypedRequestBuilder.ReadDtcByStatus(
                checked((byte)CanId(DtcMaskParameter))),"Read DTC"), () => !IsBusy);
        PrepareTesterPresentCommand = new DelegateCommand(() =>
            PrepareTypedRequest(UdsTypedRequestBuilder.TesterPresent,"Tester Present"), () => !IsBusy);
        SendCommand = new DelegateCommand(() => _ = SendBoundedAsync(), () => !IsBusy);
        CancelCommand = new DelegateCommand(() => _sendStop?.Cancel(), () => IsBusy && _sendStop is not null);
        ResetCommand = new DelegateCommand(() => _ = ResetAsync(), () => !IsBusy);
        SaveServiceCommand = new DelegateCommand(SaveService, () => !IsBusy);
        DeleteServiceCommand = new DelegateCommand(DeleteService, () => !IsBusy);
        SaveDocumentCommand = new DelegateCommand(() => _ = SaveDocumentAsync(), () => !IsBusy);
        LoadDocumentCommand = new DelegateCommand(() => _ = LoadDocumentAsync(), () => !IsBusy);
        LoadReplayCommand = new DelegateCommand(() => _ = LoadReplayAsync(), () => !IsBusy);
    }
    public ObservableCollection<UdsServiceDefinition> Services { get; } = new();
    public ObservableCollection<UdsCanTraceRow> TraceRows { get; } = new();
    public DelegateCommand PrepareReadDidCommand { get; }
    public DelegateCommand PrepareReadDtcCommand { get; }
    public DelegateCommand PrepareTesterPresentCommand { get; }
    public DelegateCommand SendCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand ResetCommand { get; }
    public DelegateCommand SaveServiceCommand { get; }
    public DelegateCommand DeleteServiceCommand { get; }
    public DelegateCommand SaveDocumentCommand { get; }
    public DelegateCommand LoadDocumentCommand { get; }
    public DelegateCommand LoadReplayCommand { get; }
    public IReadOnlyList<string> SourceModes { get; } = ["Mock", "Replay", "Physical Read"];
    public bool PhysicalTxEnabled =>
        _physicalProvider?.Invoke() is { State: UsbSessionState.Connected, ProtocolVersionVerified: true };
    public string SourceLabel => SourceMode switch
    {
        "Physical Read" => "PHYSICAL CAN1 / ONLY 0x19 + 0x22",
        "Replay" => "REPLAY / NO PHYSICAL CAN TX",
        _ => "MOCK / NO PHYSICAL CAN TX"
    };

    public void AttachPhysicalLink(Func<HardwareBenchController?> device,
        Func<string,bool> operatorConsent)
    {
        _physicalProvider = device ?? throw new ArgumentNullException(nameof(device));
        _confirmPhysical = operatorConsent ?? throw new ArgumentNullException(nameof(operatorConsent));
        RaisePropertyChanged(nameof(PhysicalTxEnabled));
        RaisePropertyChanged(nameof(SourceLabel));
    }

    public void ImportCanToolConfiguration(string localXmlFile)
    {
        if (IsBusy) throw new InvalidOperationException("Wait until the current UDS request ends.");
        _canToolProfile = CanToolConfigurationImporter.LoadFile(localXmlFile);
        UseCanToolEndpoint(boot:false);
    }

    public void UseCanToolEndpoint(bool boot)
    {
        if (_canToolProfile is null)
        {
            Status="PROFILE_NOT_LOADED";
            Error="请先导入现有的 CanTool Configuration.xml";
            return;
        }
        var c=_canToolProfile;
        TxId=(boot ? c.BootRequestId : c.AppRequestId).ToString("X");
        RxId=(boot ? c.BootResponseId : c.AppResponseId).ToString("X");
        ProfileName=$"CanTool-{(boot?"BOOT":"APP")}-{c.SourceSha256[..12]}";
        Status="PROFILE_CONFIG_LOADED_NOT_VERIFIED";
        Error="";
        ResponseSummary=$"配置样本 {c.SourceName}：已填入 {(boot?"Bootloader":"APP")} 端点 " +
            $"TX=0x{TxId} / RX=0x{RxId}，SHA256={c.SourceSha256}. " +
            "此操作未发送 CAN；必须独立确认与连接的 ECU 物理身份和速率相符。";
    }
    public bool IsBusy { get => _isBusy; private set
        { if (SetProperty(ref _isBusy,value))
          foreach(var command in Commands()) command.RaiseCanExecuteChanged(); } }
    private IEnumerable<DelegateCommand> Commands() => [SendCommand, CancelCommand, ResetCommand,
        SaveServiceCommand, DeleteServiceCommand, SaveDocumentCommand, LoadDocumentCommand, LoadReplayCommand,
        PrepareReadDidCommand, PrepareReadDtcCommand, PrepareTesterPresentCommand];
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName,value); }
    public string MockIdentity { get => _mockIdentity; set => SetProperty(ref _mockIdentity,value); }
    public string TxId { get => _txId; set => SetProperty(ref _txId,value); }
    public string RxId { get => _rxId; set => SetProperty(ref _rxId,value); }
    public string P2Ms { get => _p2Ms; set => SetProperty(ref _p2Ms,value); }
    public string P2StarMs { get => _p2StarMs; set => SetProperty(ref _p2StarMs,value); }
    public string RequestHex { get => _requestHex; set => SetProperty(ref _requestHex,value); }
    public string DidParameter { get => _didParameter; set => SetProperty(ref _didParameter,value); }
    public string DtcMaskParameter { get => _dtcMaskParameter; set => SetProperty(ref _dtcMaskParameter,value); }
    public string ExpectedHex { get => _expectedHex; set => SetProperty(ref _expectedHex,value); }
    public string ServiceName { get => _serviceName; set => SetProperty(ref _serviceName,value); }
    public string DocumentPath { get => _documentPath; set => SetProperty(ref _documentPath,value); }
    public string SourceMode { get => _sourceMode; set { if(SetProperty(ref _sourceMode,value))
        { RaisePropertyChanged(nameof(SourceLabel)); RaisePropertyChanged(nameof(PhysicalTxEnabled)); } } }
    public string ReplayPath { get => _replayPath; set => SetProperty(ref _replayPath,value); }
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
    public string ResponseSummary { get => _responseSummary; private set => SetProperty(ref _responseSummary,value); }
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
    private void PrepareTypedRequest(Func<UdsPreparedRequest> builder, string label)
    {
        if (IsBusy) return;
        try
        {
            var request=builder();
            RequestHex=request.PduHex;
            ServiceName=label;
            ExpectedHex="";
            Status="PREPARED_NOT_SENT";
            ResponseHex="—";
            ResponseSummary=$"已构造 {request.Service.Name}，参数：{request.ParameterSummary}；" +
                (request.IsReadOnly ? "服务类别：只读。" : "服务会改变 ECU 状态；物理发送仍被后端禁止。") +
                " 尚未向任何设备发送。";
            Error="";
        }
        catch(Exception ex)
        {
            Status="PARAMETER_INVALID";
            Error=ex.GetType().Name+": "+ex.Message;
            ResponseSummary="未生成 UDS PDU，请修正参数后重新构造。";
        }
    }

    private static uint CanId(string value) => uint.Parse(value.Trim().Replace("0x","",StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber,CultureInfo.InvariantCulture);
    private static int Ms(string value) => int.Parse(value,CultureInfo.InvariantCulture);
    private UdsToolProfile Profile() => new(ProfileName,MockIdentity,CanId(TxId),CanId(RxId),
        Ms(P2Ms),Ms(P2StarMs));
    private UdsToolDocument Document() => new(1,Profile(),Services.ToList());
    private async Task SendBoundedAsync()
    {
        if (IsBusy) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _sendStop=stop;
        try { await SendAsync(stop.Token); }
        finally
        {
            _sendStop=null;
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task SendAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy=true;
        Status=SourceMode switch { "Replay"=>"REPLAY_REQUEST","Physical Read"=>"PHYSICAL_READ_REQUEST",_=>"MOCK_SENDING" };
        Error=""; Nrc="—"; ResponseHex="—";
        ResponseSummary="诊断请求执行中；等待明确的请求/响应证据。";
        try
        {
            var request=UdsHex.Parse(RequestHex);
            if (SourceMode=="Physical Read")
            {
                await SendPhysicalReadAsync(request,ct);
                return;
            }
            if (SourceMode=="Replay")
            {
                var playback=_replay.Consume(RequestHex,ExpectedHex);
                ApplyExchange(playback,request);
                return;
            }
            if (SourceMode!="Mock") throw new InvalidOperationException("Source not supported.");
            _backend.SetNoResponse(NoResponse);
            if (InjectPending && InjectNrc) throw new InvalidOperationException("Select NRC fault OR pending, not both.");
            if (InjectPending) _backend.InjectNextPending(request[0]);
            if (InjectNrc) _backend.InjectNextNrc(checked((byte)CanId(FaultSid)), checked((byte)CanId(FaultCode)));
            var exchange=await _backend.SendMockAsync(Profile(),RequestHex,ExpectedHex,ct);
            ApplyExchange(exchange,request);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { Status="CANCELLED"; Error="REQUEST_CANCELLED"; ResponseSummary="已取消，不能判定任何 ECU 响应成功。"; }
        catch (Exception ex)
        { Status="WORKBENCH_ERROR"; Error=ex.GetType().Name+": "+ex.Message;
          ResponseSummary="请求处理错误，不提供已确认响应。"; }
        finally { IsBusy=false; }
    }
    private async Task SendPhysicalReadAsync(byte[] request,CancellationToken ct)
    {
        if (request.Length is < 2 or > 4095 || request[0] is not (0x19 or 0x22))
            throw new UnauthorizedAccessException(
                "物理 CAN1 当前只允许 ReadDID 0x22 与 ReadDTC 0x19；会话切换/复位/安全访问/写入/烧录不可由此接口发送。");
        var controller = _physicalProvider?.Invoke() ??
            throw new InvalidOperationException("请先在 P02 设备页连接主控板，UDS 共用该 WinUSB / CAN1 会话。");
        if (!PhysicalTxEnabled || controller.Hub is null)
            throw new InvalidOperationException("主控板 USB 未连接或固件版本未通过校验，不能发送真实 UDS。");
        var sourceProfile=Profile();
        if (sourceProfile.Name.StartsWith("Mock",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不能使用 Mock ECU Profile 对真实 ECU 发送，请导入或指定当前 ECU 的有效配置。");
        var endpoint = UdsToolDocumentStore.ToEndpoint(sourceProfile);
        var physical = new ReadOnlyUdsPhysicalEndpoint(sourceProfile.Name,
            endpoint.RequestCanId,endpoint.ResponseCanId,endpoint.RequestCanId>0x7FF);
        physical.Validate();
        var reqId=Guid.NewGuid();
        var text=$"一次性真实 ECU 只读诊断\n\n" +
            $"CAN1 DUT · 当前 USB Generation {controller.Generation}\n" +
            $"ECU Profile：{sourceProfile.Name}\n" +
            $"TX CAN ID：0x{physical.RequestCanId:X}\n" +
            $"RX CAN ID：0x{physical.ResponseCanId:X}\n" +
            $"UDS PDU：{Convert.ToHexString(request)}\n" +
            $"服务：0x{request[0]:X2} / ReadOnly\n\n" +
            "必须确认当前 ECU 与端点身份相符。只允许此次读取，不会进行复位或烧录。\n\n确认发送吗？";
        if (_confirmPhysical is null ||
            !_confirmPhysical(text))
        {
            Status="PHYSICAL_NOT_SENT";
            Error="";
            ResponseSummary="操作者取消请求；没有发送 CAN 帧。";
            return;
        }
        TraceRows.Add(new UdsCanTraceRow("PHYSICAL UDS TX PDU", UdsHex.Format(request)));
        var authorization=HardwareBenchController.PhysicalReadOnlyUdsConfirmation(reqId,physical,request[0]);
        var response=await PhysicalReadOnlyUds.RunAsync(controller,physical,endpoint,
            request,reqId,authorization,ct);
        ResponseHex=response.ResponsePdu.IsDefaultOrEmpty ? "(no ECU response)" :
            UdsHex.Format(response.ResponsePdu);
        if (!response.ResponsePdu.IsDefaultOrEmpty)
            TraceRows.Add(new UdsCanTraceRow("PHYSICAL ECU RX PDU",
                UdsHex.Format(response.ResponsePdu)));
        else
            TraceRows.Add(new UdsCanTraceRow("PHYSICAL NO VALIDATED ECU RESPONSE",
                $"Outcome={response.Outcome}"));
        Status="PHYSICAL_"+response.Outcome.ToString().ToUpperInvariant();
        Nrc=response.NegativeResponseCode.HasValue
            ? $"0x{response.NegativeResponseCode.Value:X2}" : "—";
        ExpectedMatch=string.IsNullOrWhiteSpace(ExpectedHex)
            ? "NOT_SPECIFIED"
            : response.ResponsePdu.IsDefaultOrEmpty ? "UNKNOWN" :
                UdsHex.Parse(ExpectedHex).AsSpan().SequenceEqual(response.ResponsePdu.AsSpan())
                ? "MATCH" : "MISMATCH";
        ResponseSummary=UdsDiagnosticResponseFormatter.Explain(request,response.ResponsePdu.AsSpan())+
            $"\n会话 Generation={controller.Generation}; Host UTC={response.CompletedAt.Utc:O}."+
            " 原始 CAN1 RX / USB 主控 ACK 在同一个 DEVICE Trace 记录；ACK 不等于 ECU 正响应。";
        Error=response.Outcome switch
        {
            Outcome.Accepted => "",
            Outcome.Rejected => "ECU 返回负响应或本次请求被拒绝，请查看 NRC/Trace。",
            _ => "通信状态不确定；不能推断 ECU 已执行或未执行。"
        };
        while(TraceRows.Count>2000) TraceRows.RemoveAt(0);
    }

    private void ApplyExchange(UdsToolExchange exchange,byte[] request)
    {
            ResponseHex=string.IsNullOrEmpty(exchange.ResponseHex) ? "(no confirmed response)" : exchange.ResponseHex;
            try
            {
                ResponseSummary=string.IsNullOrWhiteSpace(exchange.ResponseHex)
                    ? "没有 ECU 诊断 PDU，不能把本地操作成功当作 ECU 应答。"
                    : AutoTest.Specialists.Uds.UdsDiagnosticResponseFormatter.Explain(
                        request,UdsHex.Parse(exchange.ResponseHex));
            }
            catch (Exception ex)
            { ResponseSummary="响应解析未完成："+ex.Message+"；请查看原始 PDU。"; }
            Status=exchange.Source.ToUpperInvariant()+"_"+
                (exchange.ExpectedMatched==false ? "EXPECTED_MISMATCH" : exchange.Outcome.ToUpperInvariant());
            Nrc=exchange.Nrc;
            ExpectedMatch=exchange.ExpectedMatched is null ? "NOT_SPECIFIED" :
                exchange.ExpectedMatched.Value ? "MATCH" : "MISMATCH";
            Error=exchange.ErrorCode;
            if (exchange.Outcome=="Accepted" && request.Length >= 2 && request[0]==0x10)
                Session=request[1] switch { 0x01=>"Default ("+exchange.Source+")",0x02=>"Programming ("+exchange.Source+")",_=>"Other ("+exchange.Source+")" };
            foreach(var frame in exchange.Frames)
                TraceRows.Add(frame);
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
            ResponseSummary="Mock ECU 已重置；不代表任何真实 ECU 状态发生变化。";
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
