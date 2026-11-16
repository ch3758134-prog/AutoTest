using AutoTest.Devices.Usb;

namespace AutoTest.UiPreview.Usb;

/// <summary>
/// The only real device wiring in this UI stage. Reuses the approved Devices
/// WinUSB discovery, native link and session protocol. Sends only F0 PING.
/// No CAN hub, controller outputs, firmware writes or fallback transport.
/// </summary>
public sealed class UsbConnectionController : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private UsbSessionService? _session;
    private string? _deviceId;
    private bool _disposed;
    private UsbConnectionSnapshot _status = new("未连接", false, false, null, null);

    public event Action<UsbConnectionSnapshot>? StatusChanged;
    public event Action<string>? LogRecorded;

    public UsbConnectionSnapshot Status => _status;
    public bool HasSession => _session is not null;

    private void Log(string message)
    {
        try { LogRecorded?.Invoke(DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "  " + message); }
        catch { /* Display observers never interrupt USB cleanup. */ }
    }

    private void Update(string label, bool connected, bool busy, string? identity = null,
        string? error = null)
    {
        _status = new(label, connected, busy, identity, error);
        try { StatusChanged?.Invoke(_status); }
        catch { }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        UsbSessionService? pending = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null) throw new InvalidOperationException("请先断开 USB。");

            Update("连接中", false, true);
            Log("开始查找主控板（仅 VID_1183 / PID_5850）。");
            var candidates = new WinUsbDiscovery().ListCandidates();
            foreach (var found in candidates)
                Log("发现设备：" + found.DeviceInstanceId +
                    "；驱动：" + (found.DriverService ?? "未识别"));

            if (candidates.Count == 0)
                throw new IOException("未发现主控板。请检查 USB 线缆、设备供电和 WinUSB 驱动。");
            if (candidates.Count != 1)
                throw new IOException("检测到 " + candidates.Count +
                    " 台主控板；当前不自动选择设备，请只连接目标主控板。");

            var candidate = candidates[0];
            var link = new WinUsbByteLink(candidate.DeviceInstanceId);
            link.OperationRecorded += operation =>
            {
                var detail = operation.Detail is null ? "" : "；" + operation.Detail;
                Log("WinUSB " + operation.Operation + " / " + operation.Stage + " / " +
                    operation.Outcome + " " + (operation.NativeErrorHex ?? "") + detail);
            };

            pending = new UsbSessionService(link);
            var localSession = pending;
            pending.TraceRecorded += trace =>
            {
                var detail = trace.Detail is null ? "" : "；" + trace.Detail;
                Log("协议 " + trace.Kind + " G" + trace.Generation +
                    (trace.Command is byte command ? " CMD=0x" + command.ToString("X2") : "") +
                    (trace.Sequence is ushort seq ? " SEQ=" + seq : "") +
                    (trace.Status is byte status ? " STATUS=0x" + status.ToString("X2") : "") +
                    (trace.RawHex.Length == 0 ? "" : " RAW=" + trace.RawHex) + detail);
            };
            pending.StateChanged += state =>
            {
                if (state == UsbSessionState.Faulted && ReferenceEquals(_session, localSession))
                {
                    Log("USB 接收链路中断；需先断开清理后重连。");
                    Update("连接异常", false, false, _deviceId,
                        "USB 接收链路发生错误，查看 USB 日志并断开重连。");
                }
            };

            await pending.ConnectAsync(cancellationToken).ConfigureAwait(false);
            // A real firmware response is required; native Open alone is not success.
            var pong = await pending.RequestAsync(FirmwareReadOnlyProtocol.Ping,
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);
            if (!FirmwareReadOnlyProtocol.ValidReadOnlyPayload(
                    FirmwareReadOnlyProtocol.Ping, pong.Payload))
                throw new IOException("主控板 PING 应答不是 PONG，协议校验失败。");
            if (pending.State != UsbSessionState.Connected)
                throw new IOException("PING 返回后 USB 接收会话已异常。");

            _session = pending;
            _deviceId = candidate.DeviceInstanceId;
            pending = null;
            Log("USB 连接成功：WinUSB 已打开，F0 PING 得到 PONG。");
            Update("已连接", true, false, _deviceId);
        }
        catch (Exception ex)
        {
            Log("USB 连接失败：" + ex);
            if (pending is not null)
            {
                try { await pending.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { Log("连接清理异常：" + cleanup); }
            }
            Update(ex is OperationCanceledException ? "已取消" : "连接失败",
                false, false, null, ex.Message);
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is null)
            {
                _deviceId = null;
                Update("未连接", false, false);
                return;
            }
            Update("断开中", false, true, _deviceId);
            var old = _session;
            _session = null;
            try
            {
                await old.DisposeAsync().ConfigureAwait(false);
                Log("USB 已断开，原生句柄和接收循环已清理。");
                _deviceId = null;
                Update("未连接", false, false);
            }
            catch (Exception ex)
            {
                Log("USB 断开清理失败：" + ex);
                _deviceId = null;
                Update("断开异常", false, false, null, ex.Message);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycle.Dispose();
    }
}

public sealed record UsbConnectionSnapshot(
    string Label, bool Connected, bool Busy, string? DeviceInstanceId, string? Error);
