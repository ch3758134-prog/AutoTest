using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Flash.Workbench;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Flash;

public sealed class FlashToolViewModel : BindableBase
{
    private readonly FlashDryRunWorkspace _backend;
    private CanToolConfigurationSnapshot? _canTool;
    private string _configStatus="尚未导入 CanTool Configuration.xml";
    private string _physicalPreflight="未做真实 ECU 烧录条件检查";
    private bool _isBusy;
    private string _imagePath="",_binAddress="1000",_target="MOCK_TARGET_A",
        _expectedIdentity="MOCK_ECU_A",_observedIdentity="MOCK_ECU_A",
        _allowedStart="1000",_allowedEnd="11000",_mockVoltage="12.4",
        _voltageMin="11.0",_voltageMax="15.0",_chunkSize="64",
        _faultSid="",_faultNrc="",_dropBlock="",_auditPath="",_recoveryAdvice="—",_status="SELECT_IMAGE",
        _failure="",_format="—",_hash="—",_bytes="0",_preflight="NOT_RUN",
        _job="—",_progress="0%";
    private AutoTest.Contracts.ProgrammingJobId? _runningJob;
    public FlashToolViewModel() : this(new FlashDryRunWorkspace()) { }
    public FlashToolViewModel(FlashDryRunWorkspace backend)
    {
        _backend=backend;
        LoadCommand = new DelegateCommand(() => _ = LoadFromPathAsync(), () => !IsBusy);
        PreflightCommand = new DelegateCommand(() => _ = PreflightAsync(), () => !IsBusy);
        StartCommand = new DelegateCommand(() => _ = RunDryAsync(), () => !IsBusy);
        CancelCommand = new DelegateCommand(Cancel, () => IsBusy && _runningJob is not null);
        ExportAuditCommand = new DelegateCommand(() => _ = ExportAuditAsync(), () => !IsBusy && _backend.CurrentJob is not null);
    }
    public ObservableCollection<FlashSegmentDetail> Segments { get; } = new();
    public ObservableCollection<CanToolConfigurationField> ConfigurationFields { get; } = new();
    public ObservableCollection<FlashPreflightItem> PhysicalChecks { get; } = new();
    public string ConfigurationStatus { get => _configStatus; private set => SetProperty(ref _configStatus,value); }
    public string PhysicalPreflight { get => _physicalPreflight; private set => SetProperty(ref _physicalPreflight,value); }
    public ObservableCollection<string> Stages { get; } = new();
    public DelegateCommand LoadCommand { get; }
    public DelegateCommand PreflightCommand { get; }
    public DelegateCommand StartCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand ExportAuditCommand { get; }
    public bool PhysicalProgrammingEnabled => false;

    public void ImportCanToolConfiguration(string fileName)
    {
        if (IsBusy) throw new InvalidOperationException("Cannot modify Flash Profile during an active job.");
        var candidate=CanToolConfigurationImporter.LoadFile(fileName);
        _canTool=candidate;
        ConfigurationFields.Clear();
        foreach(var field in candidate.Fields) ConfigurationFields.Add(field);
        AllowedStart=candidate.FlashStart.ToString("X8");
        AllowedEnd=candidate.EndExclusive.ToString("X");
        ConfigurationStatus=$"配置已解析：{candidate.SourceName}; " +
            $"SHA256={candidate.SourceSha256}; APP TX/RX 0x{candidate.AppRequestId:X}/0x{candidate.AppResponseId:X}; "+
            $"Boot TX/RX 0x{candidate.BootRequestId:X}/0x{candidate.BootResponseId:X}; "+
            $"CAN={candidate.BaudRate}(XML声明，非有效速率)。未加载 DLL，未向 ECU 发送。";
        PhysicalPreflight="XML 已载入；必须检查镜像地址、当前主控代次、ECU 身份和 OEM 编程合同。";
    }

    public void EvaluatePhysicalPreflight(HardwareBenchController? device)
    {
        if (IsBusy) throw new InvalidOperationException("Cannot check new configuration during active job.");
        var healthy=device is {State:UsbSessionState.Connected,ProtocolVersionVerified:true,Generation:>0};
        var evidence=new FlashFieldEvidence(
            UsbConnected: healthy,
            ControllerVersionVerified: device?.ProtocolVersionVerified == true,
            UsbGeneration: device?.Generation ?? 0,
            LiveUdsReadAccepted: false,
            LiveUdsEndpointMatches: false,
            CanBitrateVerified: false,
            EcuIdentityMatched: false,
            MeasuredPowerInRange: false,
            SecurityProviderValidated: false,
            OemProgrammingContractValidated: false,
            RecoveryProcedureValidated: false);
        var checklist=FlashFieldPreflightEvaluator.Evaluate(_canTool,_backend.ParsedImage,evidence);
        PhysicalChecks.Clear();
        foreach(var item in checklist.Items) PhysicalChecks.Add(item);
        PhysicalPreflight=$"配置/设备/地址证据：{checklist.Items.Count(x=>x.Passed)} 项通过，"+
            $"{checklist.Items.Count(x=>!x.Passed)} 项阻断。真实擦写始终 LOCKED，"+
            "未获授权不得复位、解锁、擦除或 TransferData。";
    }
    public string SourceLabel => "MOCK DRY-RUN ONLY / PHYSICAL FLASH DISABLED";
    public bool IsBusy { get => _isBusy; private set
        { if (SetProperty(ref _isBusy,value))
          { LoadCommand.RaiseCanExecuteChanged(); PreflightCommand.RaiseCanExecuteChanged();
            StartCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged();
            ExportAuditCommand.RaiseCanExecuteChanged(); } } }
    public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath,value); }
    public string BinAddress { get => _binAddress; set => SetProperty(ref _binAddress,value); }
    public string Target { get => _target; set => SetProperty(ref _target,value); }
    public string ExpectedIdentity { get => _expectedIdentity; set => SetProperty(ref _expectedIdentity,value); }
    public string ObservedIdentity { get => _observedIdentity; set => SetProperty(ref _observedIdentity,value); }
    public string AllowedStart { get => _allowedStart; set => SetProperty(ref _allowedStart,value); }
    public string AllowedEnd { get => _allowedEnd; set => SetProperty(ref _allowedEnd,value); }
    public string MockVoltage { get => _mockVoltage; set => SetProperty(ref _mockVoltage,value); }
    public string VoltageMin { get => _voltageMin; set => SetProperty(ref _voltageMin,value); }
    public string VoltageMax { get => _voltageMax; set => SetProperty(ref _voltageMax,value); }
    public string ChunkSize { get => _chunkSize; set => SetProperty(ref _chunkSize,value); }
    public string AuditPath { get => _auditPath; set => SetProperty(ref _auditPath,value); }
    public string RecoveryAdvice { get => _recoveryAdvice; private set => SetProperty(ref _recoveryAdvice,value); }
    public string FaultSid { get => _faultSid; set => SetProperty(ref _faultSid,value); }
    public string FaultNrc { get => _faultNrc; set => SetProperty(ref _faultNrc,value); }
    public string DropBlock { get => _dropBlock; set => SetProperty(ref _dropBlock,value); }
    public string Status { get => _status; private set => SetProperty(ref _status,value); }
    public string Failure { get => _failure; private set => SetProperty(ref _failure,value); }
    public string Format { get => _format; private set => SetProperty(ref _format,value); }
    public string Hash { get => _hash; private set => SetProperty(ref _hash,value); }
    public string Bytes { get => _bytes; private set => SetProperty(ref _bytes,value); }
    public string Preflight { get => _preflight; private set => SetProperty(ref _preflight,value); }
    public string Job { get => _job; private set => SetProperty(ref _job,value); }
    public string Progress { get => _progress; private set => SetProperty(ref _progress,value); }
    private static uint Hex(string value) => uint.Parse(value.Trim().Replace("0x","",StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber,CultureInfo.InvariantCulture);
    private static ulong HexLong(string value) => ulong.Parse(value.Trim().Replace("0x","",StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber,CultureInfo.InvariantCulture);
    private static double Number(string value) => double.Parse(value,CultureInfo.InvariantCulture);
    private FlashDryRunConfiguration Configuration() => new(Target,ExpectedIdentity,ObservedIdentity,
        Hex(AllowedStart),HexLong(AllowedEnd),Number(MockVoltage),Number(VoltageMin),Number(VoltageMax),
        int.Parse(ChunkSize,CultureInfo.InvariantCulture));
    public async Task LoadFromPathAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try
        {
            string path=ImagePath.Trim();
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Select an image first.");
            var file=new FileInfo(path);
            if (!file.Exists || file.Length > FirmwareImageParser.MaxImageBytes || file.Length==0)
                throw new InvalidDataException("Firmware file missing, empty or exceeds 16 MiB.");
            byte[] bytes=await File.ReadAllBytesAsync(path);
            LoadBytes(Path.GetFileName(path),bytes,Hex(BinAddress));
        }
        catch(Exception ex) { Status="IMAGE_ERROR"; Failure=ex.GetType().Name+": "+ex.Message; }
        finally { IsBusy=false; }
    }
    public void LoadBytes(string fileName, byte[] content, uint address)
    {
        var detail=_backend.LoadImage(fileName,content,address);
        Segments.Clear(); foreach(var s in detail.Segments) Segments.Add(s);
        Format=detail.Format; Hash=detail.Sha256;
        Bytes=detail.Bytes.ToString(CultureInfo.InvariantCulture);
        Preflight="NOT_RUN"; Status="IMAGE_LOADED"; Failure="";
        Stages.Clear(); Job="—"; Progress="0%"; RecoveryAdvice="—";
    }
    public async Task PreflightAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try
        {
            var report=await _backend.PreflightAsync(Configuration());
            Preflight=report.Ready ? "MOCK_READY" : "REJECTED";
            Failure=report.Ready ? "" : string.Join("; ",report.Blockers);
            Status=report.Ready ? "PREFLIGHT_PASS_MOCK" : "PREFLIGHT_BLOCKED";
        }
        catch(Exception ex) { Status="PREFLIGHT_ERROR"; Failure=ex.Message; }
        finally { IsBusy=false; }
    }
    public async Task RunDryAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy=true; Failure=""; Stages.Clear(); Progress="0%";
        try
        {
            var report=await _backend.PreflightAsync(Configuration(),ct);
            Preflight=report.Ready ? "MOCK_READY" : "REJECTED";
            if (!report.Ready) { Status="PREFLIGHT_BLOCKED"; Failure=string.Join("; ",report.Blockers); return; }
            byte? sid=string.IsNullOrWhiteSpace(FaultSid)?null:checked((byte)Hex(FaultSid));
            byte? nrc=string.IsNullOrWhiteSpace(FaultNrc)?null:checked((byte)Hex(FaultNrc));
            int drop=string.IsNullOrWhiteSpace(DropBlock)?-1:int.Parse(DropBlock,CultureInfo.InvariantCulture);
            var job=await _backend.StartDryRunAsync(Configuration(),sid,nrc,drop,ct);
            _runningJob=job; CancelCommand.RaiseCanExecuteChanged();
            Job=job.Value.ToString("D"); Status="MOCK_PROGRAMMING";
            while (true)
            {
                var state=_backend.Observe(job);
                Update(state);
                if (state.State is ProgrammingJobState.Completed or ProgrammingJobState.Failed or
                    ProgrammingJobState.Cancelled or ProgrammingJobState.Uncertain) break;
                await Task.Delay(80,ct);
            }
            Update(await _backend.WaitAsync(job,ct));
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        { if (_runningJob is { } id) _backend.Cancel(id); Status="CANCEL_REQUESTED"; }
        catch(Exception ex) { Status="DRY_RUN_ERROR"; Failure=ex.GetType().Name+": "+ex.Message; }
        finally { _runningJob=null; IsBusy=false; }
    }
    private void Update(ProgrammingJobSnapshot snapshot)
    {
        Status="MOCK_"+snapshot.State.ToString().ToUpperInvariant();
        Failure=snapshot.FailureCode??"";
        RecoveryAdvice=snapshot.State switch
        {
            ProgrammingJobState.Completed => "仅模拟验证完成；不能当作真实 ECU 编程证据。",
            ProgrammingJobState.Failed or ProgrammingJobState.Cancelled or ProgrammingJobState.Uncertain =>
                "模拟作业不完整。不得自动续写；重新检查镜像/配置及故障后启动新 Mock 作业。",
            _ => "Mock 正在执行；断连或取消可能留下部分模拟写入。"
        };
        Stages.Clear();
        foreach(var f in snapshot.Facts)
            Stages.Add($"{f.Utc:HH:mm:ss.fff}  {f.Stage,-20} {f.Status,-8} {f.ConfirmedBytes,8}B  {f.Detail}");
        long max=snapshot.Facts.Length==0?0:snapshot.Facts.Max(x=>x.ConfirmedBytes);
        long total=_backend.Image?.Bytes??0;
        Progress=total==0?"0%":$"{Math.Min(100,100.0*max/total):F1}% ({max}/{total} bytes, MOCK)";
    }
    public async Task ExportAuditAsync()
    {
        if (IsBusy) return;
        IsBusy=true;
        try { await _backend.ExportMockAuditAsync(AuditPath);
            Status="MOCK_AUDIT_EXPORTED"; Failure=""; }
        catch(Exception ex) { Status="MOCK_AUDIT_ERROR"; Failure=ex.Message; }
        finally { IsBusy=false; }
    }
    public void Cancel()
    {
        if (_runningJob is { } id && _backend.Cancel(id)) Status="MOCK_CANCEL_REQUESTED";
    }
}
