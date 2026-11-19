using System.IO;
using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Uds;

namespace AutoTest.UdsLab;

/// <summary>
/// A dedicated real-device diagnostic session. No virtual CAN, mock fallback, OEM
/// flash state machine, second ISO-TP implementation, or implicit CAN permission.
/// One EXACT human-approved physical CAN frame is required for every TX frame.
/// </summary>
public sealed class UdsLabHardwareSession : IAsyncDisposable
{
    private readonly UdsEndpointProfile _endpoint;
    private readonly Func<CanFrame, byte[], CancellationToken, Task<string?>> _consent;
    private readonly Action<string> _trace;
    private WinUsbByteLink? _link;
    private UsbSessionService? _usb;
    private CanFrameHub? _hub;
    private HardwareCanReceiveBridge? _bridge;
    private CanTransmitGate? _leases;
    private UdsSession? _session;
    private readonly SemaphoreSlim _leaseLifecycle = new(1, 1);
    private bool _disposed;

    public UdsLabHardwareSession(UdsEndpointProfile endpoint,
        Func<CanFrame, byte[], CancellationToken, Task<string?>> consent,
        Action<string> trace)
    {
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        _endpoint = endpoint;
        _consent = consent;
        _trace = trace;
    }

    public DateTimeOffset? LeaseExpiresUtc => _session?.LeaseExpiresUtc;
    // USB/ISO-TP availability is not proof of ECU presence. Host CAN lease
    // renewal occurs before an actual request, never by silently transmitting.
    public bool IsReady => !_disposed && _usb?.State == UsbSessionState.Connected &&
        _session is not null;
    public string DeviceIdentity => _link?.DeviceInstanceId ?? "未连接";

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_disposed || _usb is not null) throw new InvalidOperationException("诊断会话已使用，须创建新实例。");
        var devices = new WinUsbDiscovery().ListCandidates();
        if (devices.Count != 1)
            throw new IOException("必须恰好连接一台受支持的主控板，发现：" + devices.Count);
        try
        {
            _link = new WinUsbByteLink(devices[0].DeviceInstanceId);
            _link.OperationRecorded += e =>
            {
                if (e.Outcome is "FAILED" or "BLOCKED" or "SHORT")
                    _trace("WinUSB " + e.Operation + "/" + e.Stage + " " + e.Outcome + " " + e.Detail);
            };
            _usb = new UsbSessionService(_link);
            _usb.TraceRecorded += e =>
            {
                if (e.Kind is "TX_REQUEST" or "TX_WRITTEN" or "REQUEST_TIMEOUT" or
                    "REQUEST_ERROR" or "READ_FAULT" or "CONNECTED" or "DISCONNECTED")
                    _trace("USB " + e.Kind + " G" + e.Generation + " " + e.RawHex + " " + e.Detail);
            };
            await _usb.ConnectAsync(ct);
            await _usb.RequestAsync(MainControllerCodec.Ping, ReadOnlyMemory<byte>.Empty,
                TimeSpan.FromSeconds(3), ct);
            _leases = new CanTransmitGate(_usb.Generation);
            _hub = new CanFrameHub(TransmitAsync);
            _bridge = new HardwareCanReceiveBridge(_usb, _hub);
            _bridge.Trace += fact =>
            {
                if (!fact.Valid)
                {
                    _trace("CAN RX INVALID port=" + fact.PhysicalPort + " G" +
                        fact.SessionGeneration + " " + fact.RawUsbPayloadHex + " " + fact.Detail);
                    return;
                }
                if (fact.Role != _endpoint.Role) return;
                // Only stream this ECU's diagnostic response frames into the
                // WPF log; unrelated busy CAN traffic must not flood the UI.
                var raw = Convert.FromHexString(fact.RawUsbPayloadHex);
                if (CanFrameCodec.FromUsbPayload(raw,
                        new BusRoleMap(default, DutPort: 1, KeypadPort: 0), out var observed) &&
                    observed is not null && observed.Id == _endpoint.ResponseCanId)
                    _trace("CAN RX ECU ID=0x" + observed.Id.ToString("X") +
                        " DLC=" + observed.Dlc + " data=" + Convert.ToHexString(observed.Data.ToArray()) +
                        " G" + fact.SessionGeneration + " " + fact.Detail);
            };
            _session = await new UdsSessionService(_leases, _hub)
                .StartSessionAsync(_endpoint, "AutoTest.UdsLab", ct);

            // CanHubIsoTpLink subscribes asynchronously. Do not transmit before
            // its response subscription is installed.
            for (int n = 0; _hub.SubscriberCount == 0 && n < 100; n++)
                await Task.Delay(10, ct);
            if (_hub.SubscriberCount == 0)
                throw new IOException("UDS ISO-TP RX 订阅未就绪，不允许发送请求。");
            _trace("UDS RX 已就绪，物理授权仍逐帧执行；USB Ping 不代表 ECU 在线。");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
        CanTxRequest request, LeaseToken lease, CancellationToken ct)
    {
        if (_disposed || _usb?.State != UsbSessionState.Connected || _link is null ||
            _leases is null || !_leases.Validate(request.Frame.Role, lease))
            return OperationResult<CanTxReceipt>.Rejected("UDS_SESSION_INVALID", "USB/租约已失效");
        var frame = request.Frame;
        if (frame.Role != BusRole.DutCan || frame.Id != _endpoint.RequestCanId ||
            frame.ExtendedId != (_endpoint.RequestCanId > 0x7FF))
            return OperationResult<CanTxReceipt>.Rejected("UDS_FRAME_OUT_OF_PROFILE", "物理帧不匹配当前 ECU 诊断地址");
        var map = new BusRoleMap(default, DutPort: 1, KeypadPort: 0);
        byte[] payload = CanFrameCodec.ToUsbPayload(frame, map);
        _trace("CAN TX 待人工逐帧授权 ID=0x" + frame.Id.ToString("X") +
            " data=" + Convert.ToHexString(frame.Data.ToArray()));
        string? typed = await _consent(frame, payload, ct);
        if (typed is null)
            throw new OperationCanceledException("人工拒绝实体 CAN 帧发送。");
        // PhysicalCommandFence checks exact user-typed text and consumes this
        // one-shot grant BEFORE native USB WritePipe. Never generate consent here.
        _link.AuthorizeOneCanTransmission(payload, typed);
        _trace("CAN TX 实体逐帧授权已提供；等待主控发送 ACK。");
        await _usb.RequestAsync(MainControllerCodec.CanSend, payload,
            TimeSpan.FromSeconds(3), ct);
        _trace("CAN TX USB ACK；不等于 ECU 已收到或执行。");
        return OperationResult<CanTxReceipt>.Accepted(new CanTxReceipt(
            request.CorrelationId, AckKind.DeviceAcknowledged,
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 0), EvidenceRef.New()));
    }

    /// <summary>
    /// Renews only the HOST-side CAN transmit lease. Does not send a UDS
    /// TesterPresent or claim the ECU entered/remained in any diagnosis session.
    /// Called between serialized requests; cannot grant physical CAN permission.
    /// </summary>
    private async Task EnsureHostLeaseAsync(CancellationToken ct)
    {
        await _leaseLifecycle.WaitAsync(ct);
        try
        {
            if (_disposed || _usb?.State != UsbSessionState.Connected ||
                _leases is null || _hub is null || _session is null)
                throw new IOException("无法续约：真实 USB/UDS 链路已失效。");
            if (_session.LeaseExpiresUtc > DateTimeOffset.UtcNow.AddSeconds(12))
                return;
            _trace("HOST CAN 租约即将到期：正在无实体发送地重建 ISO-TP 收发订阅。");
            var old = _session;
            _session = null;
            await old.DisposeAsync();
            ct.ThrowIfCancellationRequested();
            _session = await new UdsSessionService(_leases, _hub)
                .StartSessionAsync(_endpoint, "AutoTest.UdsLab", ct);
            for (int n = 0; _hub.SubscriberCount == 0 && n < 100; n++)
                await Task.Delay(10, ct);
            if (_hub.SubscriberCount == 0)
                throw new IOException("CAN 租约续约后 ISO-TP RX 未就绪。");
            _trace("HOST CAN 租约已续期；ECU 诊断会话状态仍需单独验证。");
        }
        finally { _leaseLifecycle.Release(); }
    }

    public async Task<(UdsExchangeResult Result, IReadOnlyList<UdsTraceEntry> Trace)>
        RequestAsync(byte[] pdu, Guid correlation, CancellationToken ct)
    {
        // Defense in depth: a caller that bypasses the WPF parameter editor
        // must not be able to use 0x2A or any state-changing UDS service via
        // this read-only standalone lab's real CAN path.
        if (pdu is null || pdu.Length < 1 || pdu[0] is not (0x19 or 0x22 or 0x23 or 0x24))
            throw new UnauthorizedAccessException("本版本实体 UDS 通路仅允许已列出的只读服务。");
        await EnsureHostLeaseAsync(ct);
        var session = _session ?? throw new IOException("UDS 诊断会话未就绪。");
        var result = await session.Client.RequestAsync(
            new UdsRequest(ImmutableArray.CreateRange(pdu), correlation), _endpoint, ct);
        var trace = session.Client.Trace.Snapshot()
            .Where(e => e.CorrelationId == correlation).ToArray();
        return (result, trace);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null)
        {
            try { await _session.DisposeAsync(); }
            catch (Exception e) { _trace("UDS 释放异常：" + e.Message); }
            _session = null;
        }
        if (_bridge is not null)
        {
            try { await _bridge.DisposeAsync(); }
            catch (Exception e) { _trace("CAN 接收停止异常：" + e.Message); }
            _bridge = null;
        }
        if (_usb is not null)
        {
            try { await _usb.DisposeAsync(); }
            catch (Exception e) { _trace("USB 断开异常：" + e.Message); }
            _usb = null;
        }
        _hub?.Dispose();
        _hub = null;
        _link = null;
        _leaseLifecycle.Dispose();
    }
}
