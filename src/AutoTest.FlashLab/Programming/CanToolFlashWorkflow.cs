using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Specialists.Uds;

namespace AutoTest.FlashLab.Programming;

public sealed record FlashExecutionResult(long TransferAcknowledged,long VerifiedBytes,
    bool ResetAcknowledged,bool AppSessionObserved,string Outcome);

public sealed class FlashEcuNrcException(byte sid,byte nrc)
    : IOException($"ECU 拒绝 UDS 0x{sid:X2}：NRC 0x{nrc:X2}")
{
    public byte Sid { get; }=sid;
    public byte Nrc { get; }=nrc;
}

/// <summary>CanTool's real Boot/APP 10/11, 27, 31, 34/36/37, 31, 11
/// sequence over the EXISTING CanHubIsoTpLink + IsoTpConversation. No mock
/// transport, no silent key fallback, no automatic erase/write retry.</summary>
public sealed class CanToolFlashWorkflow(
    CanFrameHub hub, CanTransmitGate leases, CanToolFlashJob plan,
    Action<string> log, Action<long,long> confirmed,
    Action<bool> sensitive)
{
    private const int MaxPending=30;
    private sealed class EndpointSession(CanHubIsoTpLink link,IsoTpConversation iso)
        : IAsyncDisposable
    {
        public CanHubIsoTpLink Link { get; }=link;
        public IsoTpConversation Iso { get; }=iso;
        public ValueTask DisposeAsync()=>Link.DisposeAsync();
    }

    private async Task<EndpointSession> OpenAsync(UdsEndpointProfile endpoint,
        LeaseToken lease,CancellationToken ct)
    {
        int before=hub.SubscriberCount;
        var link=new CanHubIsoTpLink(hub,endpoint,lease);
        try
        {
            // Must subscribe to actual CAN RX before the ECU can answer.
            for(int i=0;i<100 && hub.SubscriberCount<=before;i++)
                await Task.Delay(10,ct).ConfigureAwait(false);
            if(hub.SubscriberCount<=before)
                throw new IOException("ISO-TP CAN1 接收订阅未就绪，禁止发送。");
            return new(link,new IsoTpConversation(link,new IsoTpOptions(TimeSpan.FromSeconds(2))));
        }
        catch { await link.DisposeAsync().ConfigureAwait(false);throw; }
    }

    private async Task<byte[]> RequestAsync(EndpointSession session,byte[] request,
        string step,TimeSpan timeout,CancellationToken ct)
    {
        byte sid=request[0];
        if(sid is not (0x10 or 0x11 or 0x27 or 0x31 or 0x34 or 0x36 or 0x37))
            throw new UnauthorizedAccessException($"F2 不允许其它 SID 0x{sid:X2}。");
        log($"UDS {step} 请求 SID=0x{sid:X2}，长度={request.Length}B");
        await session.Iso.SendPduAsync(request,ct).ConfigureAwait(false);
        return await ReceiveExpectedAsync(session,sid,step,timeout,ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReceiveExpectedAsync(EndpointSession session,byte sid,
        string step,TimeSpan timeout,CancellationToken ct)
    {
        for(int pending=0;;pending++)
        {
            if(pending>=MaxPending)
                throw new TimeoutException($"UDS {step} NRC 0x78 次数超限，效果未知。");
            byte[] response=await session.Iso.ReceivePduAsync(
                pending==0?timeout:TimeSpan.FromSeconds(5),ct).ConfigureAwait(false);
            if(response.Length==3 && response[0]==0x7F && response[1]==sid)
            {
                if(response[2]==0x78)
                {
                    log($"UDS {step} ECU ResponsePending 0x78 ({pending+1}/{MaxPending})");
                    continue;
                }
                throw new FlashEcuNrcException(sid,response[2]);
            }
            if(response.Length==0 || response[0]!=(byte)(sid+0x40))
                throw new InvalidDataException($"UDS {step} 应答 SID 不匹配。");
            log($"UDS {step} 正响应 SID=0x{response[0]:X2}，长度={response.Length}B");
            return response;
        }
    }

    // Successful trace: APP 51 01 -> Boot 10 02 within milliseconds;
    // a single second Boot 10 02 probe after ~30ms if the first times out.
    // Only session entry is retried, never an erase or write.
    private async Task<byte[]> EnterBootProgrammingSessionAsync(EndpointSession boot,
        TimeSpan p2,CancellationToken ct)
    {
        byte[] request=[0x10,0x02];
        log("S2 Boot 10 02：APP 复位后立即探测。");
        await boot.Iso.SendPduAsync(request,ct).ConfigureAwait(false);
        try
        {
            return await ReceiveExpectedAsync(boot,0x10,"Boot 10 02",
                TimeSpan.FromMilliseconds(30),ct).ConfigureAwait(false);
        }
        catch(TimeoutException) when(!ct.IsCancellationRequested)
        {
            log("S2 Boot 首次未应答；约30ms后再次发送10 02（限一次）。");
            await boot.Iso.SendPduAsync(request,ct).ConfigureAwait(false);
            return await ReceiveExpectedAsync(boot,0x10,"Boot 10 02 retry",
                p2,ct).ConfigureAwait(false);
        }
    }

    private async Task<FlashExecutionResult> ProgramCoreAsync(
        LeaseToken lease,CancellationToken ct)
    {
        long accepted=0,verified=0;
        TimeSpan p2=plan.Boot.P2;
        bool appObserved=false;
        bool resetAck=false;

        // Reopen the same inspected SafetyFile after operator approval but
        // before sending any CAN frames. No implicit native DLL fallback.
        var inspected=CanToolSecurityProvider.Inspect(plan.SecurityLibraryPath);
        if(!string.Equals(inspected.Sha256,plan.SecuritySha256,StringComparison.OrdinalIgnoreCase))
            throw new IOException("安全文件 SafetyFile.dll 在检查后发生修改。");
        if(!string.Equals(inspected.Kind.ToString(),plan.SecurityProviderKind,StringComparison.Ordinal))
            throw new IOException("算法库类型与冻结任务不一致。");
        using var keyProvider=CanToolSecurityProvider.Open(inspected);
        log("安全算法接口已验证："+inspected.Api+"；是否匹配 ECU 由 0x27 应答确认。");

        bool bootDetected=false;
        log("S1 检查 Boot：10 03");
        try
        {
            await using var bootProbe=await OpenAsync(plan.Boot,lease,ct).ConfigureAwait(false);
            var result=await RequestAsync(bootProbe,[0x10,0x03],"Boot 10 03",p2,ct)
                .ConfigureAwait(false);
            if(result.Length<2 || result[1]!=0x03)
                throw new InvalidDataException("Boot 10 03 的 50 03 响应不完整。");
            bootDetected=true;
        }
        catch(FlashEcuNrcException ex)
        {
            log("Boot 10 03 明确 NRC："+ex.Message+"；改用 APP 探测。");
        }
        catch(TimeoutException)
        {
            log("Boot 10 03 超时，模式未知；按 CanTool 参考转 APP 探测。");
        }
        if(!bootDetected)
        {
            log("S1/S2 APP：10 03 → 11 01 → 等待 Boot");
            await using(var app=await OpenAsync(plan.App,lease,ct).ConfigureAwait(false))
            {
                var session=await RequestAsync(app,[0x10,0x03],"APP 10 03",p2,ct)
                    .ConfigureAwait(false);
                if(session.Length<2 || session[1]!=0x03)
                    throw new InvalidDataException("APP 50 03 不完整。");
                var reset=await RequestAsync(app,[0x11,0x01],"APP 11 01",p2,ct)
                    .ConfigureAwait(false);
                if(reset.Length<2 || reset[1]!=0x01)
                    throw new InvalidDataException("APP 51 01 不完整。");
            }
            // Reference sequence enters Boot immediately after APP 51 01.
        }

        await using(var boot=await OpenAsync(plan.Boot,lease,ct).ConfigureAwait(false))
        {
            log("S2 进入 Boot 编程会话：10 02");
            var response=await EnterBootProgrammingSessionAsync(boot,p2,ct)
                .ConfigureAwait(false);
            if(response.Length<2 || response[1]!=0x02)
                throw new InvalidDataException("Boot 50 02 不完整。");

            log("S3 SecurityAccess：27 01 → CanTool 参考内置算法（4B Seed）→ 27 02；"+
                "算法 ECU 兼容性未提前证明");
            sensitive(true);
            try
            {
                var seedResponse=await RequestAsync(boot,[0x27,0x01],"Seed 27 01",p2,ct)
                    .ConfigureAwait(false);
                if(seedResponse.Length<6 || seedResponse[1]!=0x01)
                    throw new InvalidDataException("27 01 未返回有效 Seed（最少4字节）。");
                byte[] seed=seedResponse.AsSpan(2).ToArray();
                CryptographicOperations.ZeroMemory(seedResponse);
                byte[] key;
                try { key=keyProvider.Calculate(seed); }
                finally { CryptographicOperations.ZeroMemory(seed); }
                byte[] answer=new byte[2+key.Length];
                try
                {
                    answer[0]=0x27;answer[1]=0x02;key.CopyTo(answer,2);
                    var granted=await RequestAsync(boot,answer,"Key 27 02",p2,ct)
                        .ConfigureAwait(false);
                    if(granted.Length<2 || granted[1]!=0x02)
                        throw new InvalidDataException("27 02 未得到 67 02 解锁应答。");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(answer);
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            finally { sensitive(false); }
            log("S3 ECU 返回 67 02；不代表烧录完成。");

            log($"S4 擦除：0x{plan.FlashStart:X8}..0x{plan.FlashEndInclusive:X8} ({plan.EraseLength:N0}B)");
            byte[] erase=CanToolProgrammingProtocol.Erase(plan.FlashStart,
                checked((uint)plan.EraseLength));
            var erased=await RequestAsync(boot,erase,"Erase FF00",
                TimeSpan.FromSeconds(30),ct).ConfigureAwait(false);
            if(erased.Length<4 || erased[1]!=0x01 || erased[2]!=0xFF || erased[3]!=0x00)
                throw new InvalidDataException("Erase FF00 未确认 71 01 FF 00。");
            log("S4 擦除命令 ECU 正响应，开始按 CanTool 镜像对齐块下载。");

            int index=0;
            foreach(var block in plan.Blocks)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                log($"S5 Block {index}/{plan.Blocks.Length} 34 address=0x{block.Address:X8} len={block.Bytes.Length}");
                var dl=await RequestAsync(boot,
                    CanToolProgrammingProtocol.Download(block.Address,checked((uint)block.Bytes.Length)),
                    "Download 34",p2,ct).ConfigureAwait(false);
                uint max=CanToolProgrammingProtocol.ParseDownloadMaximum(dl);
                int size=checked((int)Math.Min(max-2,4093u));
                if(size<=0)throw new InvalidDataException("0x74 无有效 0x36 载荷。");
                log($"ECU 0x74 最大完整 0x36 PDU={max}B；当前数据片段上限={size}B");
                byte counter=1;
                int offset=0;
                while(offset<block.Bytes.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    int take=Math.Min(size,block.Bytes.Length-offset);
                    byte[] data=CanToolProgrammingProtocol.Transfer(counter,
                        block.Bytes.AsSpan().Slice(offset,take));
                    var ack=await RequestAsync(boot,data,"Transfer 36",p2,ct)
                        .ConfigureAwait(false);
                    if(ack.Length!=2 || ack[1]!=counter)
                        throw new InvalidDataException($"TransferData 0x76 BSC 不匹配: {counter:X2}");
                    offset+=take;
                    accepted+=take;
                    confirmed(accepted,verified);
                    unchecked { counter++; }
                }
                var exited=await RequestAsync(boot,
                    CanToolProgrammingProtocol.RequestDownloadExit(),
                    "TransferExit 37",p2,ct).ConfigureAwait(false);
                if(exited.Length<1 || exited[0]!=0x77)
                    throw new InvalidDataException("缺少 0x77 TransferExit 确认。");

                var check=await RequestAsync(boot,
                    CanToolProgrammingProtocol.Verify(block.Address,checked((uint)block.Bytes.Length)),
                    "Verify FF01",TimeSpan.FromSeconds(5),ct).ConfigureAwait(false);
                CanToolProgrammingProtocol.CheckVerify(check,block.Checksum);
                verified+=block.Bytes.Length;
                confirmed(accepted,verified);
                log($"S8 校验通过 block {index}/{plan.Blocks.Length}，ECU checksum=0x{block.Checksum:X4}");
            }

            log("S9 Boot ECUReset：11 01");
            var final=await RequestAsync(boot,
                CanToolProgrammingProtocol.UdsReset(),"Boot Reset 11 01",p2,ct)
                .ConfigureAwait(false);
            if(final.Length<2 || final[1]!=0x01)
                throw new InvalidDataException("Boot 51 01 复位正响应不完整。");
            resetAck=true;
        }
        // The complete CanTool run in the supplied 31,441-line capture
        // stops after Boot 51 01. Do not inject any extra APP 10 03 request
        // or make a guessed application session part of reference flashing.
        // Operator can independently verify APP identity/version afterwards.
        log("S10 参考 CanTool 烧录已结束（Boot 51 01）；"+
            "APP 实际启动、软件版本与身份未自动核验，请现场单独确认。");
        return new(accepted,verified,resetAck,false,
            "REFERENCE_FLASH_ACKED_POST_RESET_APP_UNVERIFIED");
    }

    public async Task<FlashExecutionResult> ExecuteAsync(CancellationToken ct)
    {
        var res=await leases.AcquireAsync(BusRole.DutCan,"FlashLab-F2-CanTool",
            TimeSpan.FromMinutes(40),ct).ConfigureAwait(false);
        if(res.Outcome!=Outcome.Accepted)
            throw new InvalidOperationException("CAN1 被占用；无法获取编程独占租约。");
        try { return await ProgramCoreAsync(res.Value,ct).ConfigureAwait(false); }
        finally
        {
            var released=await leases.ReleaseAsync(res.Value,CancellationToken.None).ConfigureAwait(false);
            if(released.Outcome!=Outcome.Accepted)
                log("CAN1 租约释放失败："+released.FailureCode);
        }
    }
}
