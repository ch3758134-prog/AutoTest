using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Uds;

namespace AutoTest.Desktop.Hardware;

/// <summary>
/// A real, single 0x22 DID operation. Physical permission is an exact-frame
/// WinUSB fence, not a general UDS transmit lease or a new USB session.
/// </summary>
public sealed record StationDidRead(
    UdsExchangeResult Exchange,
    IReadOnlyList<string> CanFrames,
    string? UsbDeviceIdentity,
    long StationEpoch,
    string? Error);

public sealed partial class HardwareConnectionService
{
    public async Task<StationDidRead> ReadOnlyDidAsync(
        UdsEndpointProfile endpoint, ushort did, string typedConfirmation,
        CancellationToken ct = default)
    {
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_usb?.State != UsbSessionState.Connected || _link is null ||
                _bridge is null || _hub is null || CurrentStationEpoch <= 0)
                throw new IOException("必须先在主窗口连接同一台真实主控板。");
            if (_testRunApproved || _manualCanBatchApproved || _manualKeyActionApproved ||
                !_manualCanRows.IsEmpty || IsFlashSecuritySensitive)
                throw new InvalidOperationException(
                    "TEST、FLASH、手动 CAN 或 KEY 正在占用实体工位；不能并发读取 DID。");
            RequireConfiguredEcuCanBitrate();
            var epoch = CurrentStationEpoch;
            var usbGeneration = _usb.Generation;
            using var transport = new StationDidIsoTpLink(this, endpoint, usbGeneration);
            var request = ImmutableArray.Create((byte)0x22, (byte)(did >> 8), (byte)did);
            var client = new UdsClient(new IsoTpUdsMessageChannel(
                new IsoTpConversation(transport)));
            try
            {
                // The device-side fence independently checks the exact request,
                // exact physical CAN1 ID, expected DID and authenticated RX before FC.
                _link.AuthorizeReadOnlyDid(endpoint.RequestCanId,
                    endpoint.ResponseCanId, did, typedConfirmation);
                var exchange = await client.RequestAsync(
                    new UdsRequest(request, Guid.NewGuid()), endpoint, ct);
                if (CurrentStationEpoch != epoch || _usb.Generation != usbGeneration)
                    throw new IOException("读取中主控 USB 会话已经变化，响应作废。");
                if (transport.HasLostFrames)
                    throw new IOException("DID 交换期间 CAN 接收丢帧，响应不能作为可信结果。");
                if (exchange.Outcome == Outcome.Accepted &&
                    (exchange.ResponsePdu.Length < 3 ||
                     exchange.ResponsePdu[0] != 0x62 ||
                     exchange.ResponsePdu[1] != (byte)(did >> 8) ||
                     exchange.ResponsePdu[2] != (byte)did))
                    throw new InvalidDataException(
                        "ECU 正响应的 DID 与所请求的标识不一致，拒绝确认成功。");
                var traceError = client.Trace.Snapshot()
                    .LastOrDefault(x => x.Direction == UdsTraceDirection.Error)?.Note;
                Record("UDS 0x22 / DID 0x" + did.ToString("X4") +
                    " 真实 CAN1 交换：" + exchange.Outcome +
                    (exchange.NegativeResponseCode is { } nrc
                        ? " NRC=0x" + nrc.ToString("X2") : "") +
                    "（仅工程诊断，不构成正式检测 PASS）");
                return new StationDidRead(exchange, transport.TraceSnapshot(),
                    ConnectedDeviceIdentity, epoch, traceError);
            }
            finally { _link.RevokeReadOnlyDid(); }
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// One-shot CAN source/sink. RX comes only from the existing authenticated
    /// USB 0x41 bridge, never synthesized or injected by the UI. TX still goes
    /// through the shared UsbSessionService and its PhysicalCommandFence.
    /// </summary>
    private sealed class StationDidIsoTpLink : IIsoTpFrameLink, IDisposable
    {
        private readonly HardwareConnectionService _owner;
        private readonly UdsEndpointProfile _endpoint;
        private readonly long _generation;
        private readonly long _originalBridgeDrops;
        private readonly long _originalHubDrops;
        private readonly Channel<byte[]> _frames =
            Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128)
            {
                SingleReader = true, SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        private readonly ConcurrentQueue<string> _trace = new();
        private int _disposed;
        private int _failed;

        public StationDidIsoTpLink(HardwareConnectionService owner,
            UdsEndpointProfile endpoint, long usbGeneration)
        {
            _owner = owner;
            _endpoint = endpoint;
            _generation = usbGeneration;
            _originalBridgeDrops = owner._bridge!.DroppedBeforeDecode;
            _originalHubDrops = owner._hub!.TotalDropped;
            // Subscribe before physical authorization/send. The long-lived
            // station monitor already consumes the real shared CAN hub.
            owner.FrameObserved += OnObserved;
        }

        public bool HasLostFrames => Volatile.Read(ref _failed) != 0 ||
            _owner._bridge?.DroppedBeforeDecode != _originalBridgeDrops ||
            _owner._hub?.TotalDropped != _originalHubDrops;

        public IReadOnlyList<string> TraceSnapshot() => _trace.ToArray();

        private void Fail(string reason)
        {
            Interlocked.Exchange(ref _failed, 1);
            _frames.Writer.TryComplete(new IOException(reason));
        }

        private void OnObserved(CanObservation observation)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                observation.Frame.Role != BusRole.DutCan ||
                observation.Frame.Id != _endpoint.ResponseCanId)
                return;
            if (observation.SessionGeneration != _generation ||
                observation.Frame.ExtendedId != (_endpoint.ResponseCanId > 0x7FF) ||
                observation.Frame.Data.Length != observation.Frame.Dlc ||
                HasLostFrames)
            {
                Fail("DID 接收期间发生会话切换、帧格式错误或 CAN 丢帧。");
                return;
            }
            var bytes = observation.Frame.Data.ToArray();
            _trace.Enqueue("RX ECU CAN 0x" + observation.Frame.Id.ToString("X") +
                " [" + bytes.Length + "] " + Convert.ToHexString(bytes) +
                " · USB 0x41 / HostArrival");
            if (!_frames.Writer.TryWrite(bytes))
                Fail("DID 接收数据队列溢出，已拒绝该次响应。");
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            if (Volatile.Read(ref _disposed) != 0 || HasLostFrames)
                throw new IOException("诊断发送通路已关闭或接收存在丢帧。");
            // An operator-authorized DID read permits exactly one 0x22 SF,
            // followed only by the authenticated response's ISO-TP FC frames.
            // The hardware fence is a SECOND independent byte-level check.
            var frame = CanFrame.Create(BusRole.DutCan,
                _endpoint.RequestCanId > 0x7FF, _endpoint.RequestCanId, bytes.Span);
            var payload = CanFrameCodec.ToUsbPayload(frame, StationCanWiring.Current);
            var usb = _owner._usb ?? throw new IOException("USB 已断开。");
            if (usb.Generation != _generation || usb.State != UsbSessionState.Connected)
                throw new IOException("诊断过程中 USB 会话已改变。");
            await usb.RequestAsync(MainControllerCodec.CanSend, payload,
                TimeSpan.FromSeconds(3), ct);
            _trace.Enqueue("TX ECU CAN 0x" + frame.Id.ToString("X") +
                " [" + frame.Dlc + "] " + Convert.ToHexString(bytes.ToArray()) +
                " · USB ACK（不是 ECU ACK）");
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _disposed) != 0 || HasLostFrames)
                throw new IOException("诊断接收通路已关闭或存在 CAN 丢帧。");
            try
            {
                return await _frames.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException failure)
            {
                throw new IOException("诊断 CAN 接收已结束或不可用。", failure);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.FrameObserved -= OnObserved;
            _frames.Writer.TryComplete();
        }
    }
}
