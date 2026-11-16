using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Uds;
using AutoTest.FlashLab.Programming;

namespace AutoTest.FlashLab.Hardware;

public sealed record DidReadResult(Outcome Outcome,string Request,string Response,string Detail,byte? Nrc)
{
    public string Display => Outcome==Outcome.Accepted
        ? $"ECU 确认回复：{Response}"
        : Outcome==Outcome.Rejected
            ? $"ECU 拒绝读取：{Detail}"
            : $"结果不确定：{Detail}";
}

/// <summary>Composition only: one existing real WinUSB link, CAN1 receive bridge,
/// CAN lease and AutoTest UDS/ISO-TP. Never creates a second protocol stack.</summary>
public sealed class FlashLabDeviceSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _operation = new(1,1);
    private WinUsbByteLink? _link;
    private UsbSessionService? _usb;
    private CanFrameHub? _hub;
    private CanTransmitGate? _gate;
    private HardwareCanReceiveBridge? _bridge;
    private HardwareTraceRecorder? _recorder;
    private string? _lastEvidencePath;
    private bool _disposed;
    private Guid _activeFlashPermit;
    private int _sensitiveTrace;
    private int _programmingActive;

    public bool IsConnected => _usb?.State==UsbSessionState.Connected;
    public string? EvidencePath => _recorder?.FilePath ?? _lastEvidencePath;
    public event Action<string>? Message;
    private void Emit(string text)
    {
        try { Message?.Invoke($"{DateTimeOffset.Now:HH:mm:ss.fff}  {text}"); }
        catch { /* UI must not affect USB lifecycle */ }
    }
    public static IReadOnlyList<WinUsbCandidate> Discover() =>
        new WinUsbDiscovery().ListCandidates();

    public async Task ConnectAsync(WinUsbCandidate candidate,CancellationToken ct)
    {
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(_disposed) throw new ObjectDisposedException(nameof(FlashLabDeviceSession));
            if(_usb is not null) throw new InvalidOperationException("请先断开现有 USB 会话。");
            if(!MainControllerUsbIdentity.IsControllerInstance(candidate.DeviceInstanceId))
                throw new UnauthorizedAccessException("只允许已识别的 AutoTest 主控板。");

            string folder=Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),"AutoTest","FlashLab","Evidence");
            string file=Path.Combine(folder,$"FlashLab_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.jsonl");
            var recorder=new HardwareTraceRecorder(file);
            _recorder=recorder;
            _lastEvidencePath=recorder.FilePath;
            var link=new WinUsbByteLink(candidate.DeviceInstanceId);
            _link=link;
            link.OperationRecorded += e=>
            {
                recorder.Record(e);
                if(e.Operation=="CONNECT" || e.Outcome is "FAILED" or "BLOCKED")
                    Emit($"WINUSB {e.Operation}/{e.Stage} {e.Outcome} {e.NativeErrorHex} {e.Detail}");
            };
            var usb=new UsbSessionService(link);
            _usb=usb;
            usb.TraceRecorded += e =>
            {
                // SecurityAccess seed/key must never be persisted in raw USB traces.
                recorder.Record(Volatile.Read(ref _sensitiveTrace)!=0
                    ? e with { RawHex="[REDACTED_SECURITYACCESS]", Detail="SecurityAccess bytes redacted" }
                    : e);
                if(e.Kind is "REQUEST_TIMEOUT" or "REQUEST_ERROR" or "CONNECT_ERROR" or "READ_FAULT")
                    Emit($"USB {e.Kind} {e.Detail}");
            };
            recorder.RecordMessage("F1_SCOPE","WinUSB CAN1 read-only DID; F2 write prohibited.");
            Emit("打开主控：" +candidate.DisplayIdentity);
            await usb.ConnectAsync(ct).ConfigureAwait(false);
            var pong=await usb.RequestAsync(MainControllerCodec.Ping,ReadOnlyMemory<byte>.Empty,
                TimeSpan.FromSeconds(3),ct).ConfigureAwait(false);
            if(!FirmwareReadOnlyProtocol.ValidReadOnlyPayload(MainControllerCodec.Ping,pong.Payload))
                throw new IOException("主控 PING 响应内容不匹配，拒绝进入诊断状态。");
            var gate=new CanTransmitGate(usb.Generation);
            _gate=gate;
            var transmitter=new Can1ReadOnlyDiagnosticTransmitter(usb,gate);
            var hub=new CanFrameHub(async (tx,lease,token) =>
            {
                // F2 uses EXACT one-frame permits, never the broad TEST RUN whitelist.
                Guid flashPermit=_activeFlashPermit;
                if(flashPermit!=Guid.Empty)
                {
                    if(!gate.Validate(BusRole.DutCan,lease) ||
                        lease.SessionGeneration!=usb.Generation)
                        return OperationResult<CanTxReceipt>.Rejected("F2_LEASE_INVALID",
                            "Current-generation CAN1 programming lease required.");
                    var framePayload=CanFrameCodec.ToUsbPayload(tx.Frame,
                        new BusRoleMap(default,DutPort:1,KeypadPort:0));
                    link.PrepareExactFlashFrame(flashPermit,framePayload);
                }
                return await transmitter.TransmitAsync(tx,lease,token).ConfigureAwait(false);
            });
            _hub=hub;
            var bridge=new HardwareCanReceiveBridge(usb,hub);
            _bridge=bridge;
            bridge.Trace += evt=>
            {
                recorder.Record(Volatile.Read(ref _sensitiveTrace)!=0
                    ? evt with { RawUsbPayloadHex="[REDACTED_SECURITYACCESS]" }
                    : evt);
                if(evt.PhysicalPort==1 && evt.Valid && Volatile.Read(ref _sensitiveTrace)==0 &&
                    Volatile.Read(ref _programmingActive)==0)
                    Emit($"CAN1 RX 原始帧 {evt.RawUsbPayloadHex} [HostArrival]");
                else if(!evt.Valid) Emit("CAN RX 数据异常：" +evt.Detail);
            };
            Emit("主控 PING 正常。CAN1=DUT；仅 RX 监听，尚无 DID 物理授权。");
        }
        catch
        {
            await CloseCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally { _operation.Release(); }
    }

    /// <summary>Separate on-site, exact typed operator approval is consumed by
    /// the physical USB fence. No approval is inferred from F1 implementation.</summary>
    public async Task<DidReadResult> ReadOneDidAsync(uint requestId,uint responseId,ushort did,
        string typedConfirmation,CancellationToken ct)
    {
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(_usb?.State!=UsbSessionState.Connected || _link is null ||
                _hub is null || _gate is null)
                throw new IOException("请先连接真实主控板。");
            var endpoint=new UdsEndpointProfile(BusRole.DutCan,requestId,responseId,
                TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(5),"Normal");
            UdsEndpointProfileValidator.ValidateAddressing(endpoint);
            string expected=PhysicalCommandFence.ReadDidConfirmation(requestId,responseId,did);
            if(!string.Equals(typedConfirmation,expected,StringComparison.Ordinal))
                throw new UnauthorizedAccessException("现场诊断未获本次精确授权；没有发送 CAN 帧。");
            _link.AuthorizeReadOnlyDid(requestId,responseId,did,typedConfirmation);
            _recorder?.RecordMessage("DID_ONSITE_CONSENT",expected);
            Emit($"现场授权：CAN1 TX=0x{requestId:X} RX=0x{responseId:X} DID=0x{did:X4}，限一次读取。");
            try
            {
                int listenersBefore=_hub.SubscriberCount;
                await using var uds=await new UdsSessionService(_gate,_hub)
                    .StartSessionAsync(endpoint,"FlashLab-0x22-DID",ct).ConfigureAwait(false);
                // UDS link starts its RX pump asynchronously. Do not transmit before
                // the real CAN1 subscription exists or fast ECU frames can be lost.
                for(int attempts=0;attempts<100 && _hub.SubscriberCount<=listenersBefore;attempts++)
                    await Task.Delay(10,ct).ConfigureAwait(false);
                if(_hub.SubscriberCount<=listenersBefore)
                    throw new IOException("CAN1 UDS 接收订阅未就绪；未发送 DID。");
                byte[] pdu=[0x22,(byte)(did>>8),(byte)did];
                var result=await uds.RequestAsync(pdu,ct).ConfigureAwait(false);
                foreach(var trace in uds.Client.Trace.Snapshot())
                    _recorder?.RecordMessage("UDS_"+trace.Direction,
                        Convert.ToHexString(trace.Data)+(trace.Note=="REQUEST"||trace.Note=="RESPONSE"
                            ? "" : " "+trace.Note));
                string rx=Convert.ToHexString(result.ResponsePdu.ToArray());
                if(result.Outcome==Outcome.Accepted)
                {
                    if(result.ResponsePdu.Length<3 || result.ResponsePdu[0]!=0x62 ||
                        result.ResponsePdu[1]!=(byte)(did>>8) || result.ResponsePdu[2]!=(byte)did)
                    {
                        Emit("UDS 0x62 DID 回显不匹配，禁止判成功。");
                        return new(Outcome.Uncertain,Convert.ToHexString(pdu),rx,
                            "0x62 正响应 DID 回显不匹配",null);
                    }
                    Emit("DID 读取收到 ECU 正响应："+rx);
                    return new(Outcome.Accepted,Convert.ToHexString(pdu),rx,
                        "ECU 0x62 正响应（主机观测）",null);
                }
                string detail=result.NegativeResponseCode is byte nrc
                    ? $"NRC 0x{nrc:X2}" : "响应超时、链路异常或客户端拒绝；查看原始 USB/CAN/UDS 日志";
                Emit("DID 未成功："+detail);
                return new(result.Outcome,Convert.ToHexString(pdu),rx,detail,
                    result.NegativeResponseCode);
            }
            finally { _link.RevokeReadOnlyDid(); }
        }
        finally { _operation.Release(); }
    }


    /// <summary>One explicitly consented, immutable CanTool programming job on
    /// physical CAN1. Not invoked by importing XML or choosing a HEX file.</summary>
    public async Task<FlashExecutionResult> ProgramCanToolAsync(CanToolFlashJob job,
        string typedConsent,bool stationBitrateVerified,
        Action<long,long> progress,CancellationToken ct)
    {
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        Guid permit=Guid.Empty;
        try
        {
            if(_usb?.State!=UsbSessionState.Connected || _link is null ||
                _hub is null || _gate is null || _recorder is null)
                throw new IOException("必须先建立独占 WinUSB/CAN1 主控会话。");
            if(!stationBitrateVerified)
                throw new UnauthorizedAccessException(
                    "主控 CAN1 实际比特率未被现场确认与 XML 一致，禁止擦写。");
            if(!string.Equals(typedConsent,job.Consent,StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "必须由现场人员输入与完整配置/固件/DLL SHA256 绑定的准确授权字符串。");
            if(!string.Equals(Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(job.FirmwarePath))),
                    job.FirmwareSha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("烧录镜像在创建冻结任务后改变。");
            var key=NativeZlgKeyProvider.Inspect(job.SecurityLibraryPath);
            if(!string.Equals(key.Sha256,job.SecuritySha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Key DLL 在创建冻结任务后改变。");
            long frames=(job.PlannedBytes/6L)*3L+5000L+job.Blocks.Length*50L;
            if(frames is < 1 or > 500000L)
                throw new FormatException("计划 CAN 帧预算超限；需拆分镜像和重新审批。");
            permit=_link.AuthorizeFlashJob(job.Fingerprint,typedConsent,
                job.App.RequestCanId,job.Boot.RequestCanId,checked((int)frames));
            _activeFlashPermit=permit;
            Interlocked.Exchange(ref _programmingActive,1);
            _recorder.RecordMessage("FLASH_PLAN_APPROVED",
                $"SHA={job.Fingerprint} target={job.TargetLabel} image={job.FirmwareSha256} "+
                $"keyDllSha={job.SecuritySha256} CAN1 bitrate=operator_verified "+
                $"erase=0x{job.FlashStart:X8}..0x{job.FlashEndInclusive:X8}");
            Emit($"真实烧录许可已生效 SHA={job.Fingerprint}；CAN1；禁自动副作用重试。");
            try
            {
                var work=new CanToolFlashWorkflow(_hub,_gate,job,
                    line =>
                    {
                        // Protocol stage log intentionally excludes Seed/Key bytes.
                        _recorder?.RecordMessage("FLASH_STAGE",line);
                        Emit(line);
                    },
                    (tx,verified) =>
                    {
                        _recorder?.RecordMessage("FLASH_PROGRESS",
                            $"TransferAcked={tx} Verified={verified} Planned={job.PlannedBytes}");
                        progress(tx,verified);
                    },
                    isSensitive => Interlocked.Exchange(ref _sensitiveTrace,isSensitive?1:0));
                var outcome=await work.ExecuteAsync(ct).ConfigureAwait(false);
                _recorder.RecordMessage("FLASH_TERMINAL",outcome.Outcome);
                return outcome;
            }
            catch(Exception ex)
            {
                _recorder?.RecordMessage("FLASH_UNCERTAIN",
                    ex.GetType().Name+" "+ex.Message+
                    "；可能已发生 ECU 擦写。禁止未经审查重复烧录。");
                Emit("烧录中止/失败，物理效果可能未知："+ex.Message);
                throw;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sensitiveTrace,0);
            Interlocked.Exchange(ref _programmingActive,0);
            _activeFlashPermit=Guid.Empty;
            if(permit!=Guid.Empty)_link?.RevokeFlashJob(permit);
            _operation.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try { await CloseCoreAsync().ConfigureAwait(false); }
        finally { _operation.Release(); }
    }
    private async Task CloseCoreAsync()
    {
        _link?.RevokeReadOnlyDid();
        if(_activeFlashPermit!=Guid.Empty) _link?.RevokeFlashJob(_activeFlashPermit);
        _activeFlashPermit=Guid.Empty;
        Interlocked.Exchange(ref _programmingActive,0);
        Interlocked.Exchange(ref _sensitiveTrace,0);
        if(_bridge is not null)
        {
            await _bridge.DisposeAsync().ConfigureAwait(false);
            _bridge=null;
        }
        _hub?.Dispose();_hub=null;_gate=null;
        if(_usb is not null)
        {
            try { await _usb.DisposeAsync().ConfigureAwait(false); }
            finally { _usb=null;_link=null; }
        }
        if(_recorder is not null)
        {
            _recorder.RecordMessage("DISCONNECTED","DID grants revoked and station USB session closed.");
            await _recorder.DisposeAsync().ConfigureAwait(false);
            Emit("原始证据已保存：" +_recorder.FilePath);
            _recorder=null;
        }
        Emit("USB 已断开，CAN1 发送许可撤销。");
    }
    public async ValueTask DisposeAsync()
    {
        if(_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed=true;_operation.Dispose();
    }
}
