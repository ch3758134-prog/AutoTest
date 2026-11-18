using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EPB.Core.Testing;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace EPB_UsbTest;

/// <summary>一次请求-响应（或失败）的结果。</summary>
public readonly record struct UsbResponse(bool Ok, string Err, byte Cmd, byte Status, byte[] Payload)
{
    public static UsbResponse Fail(string err) => new(false, err, 0, 0, Array.Empty<byte>());
    public static UsbResponse Success(byte cmd, byte status, byte[] payload) => new(true, "", cmd, status, payload);
}

/// <summary>
/// USB 通信类：连接板卡（VID 0x1183 / PID 0x5850）。
/// libusb-1.0（LibUsbDotNet 3.0.224），Windows 后端走 winusb.sys，驱动无需更换。
///
/// 独立后台读线程持续读 IN 端点 → 字节流成帧 → 分派：
///   ① SEQ≠0 → 配对 SendAndWait 的等待者（按 SEQ 找 TCS，完成请求-响应）；
///   ② SEQ==0 → 0x41 主动上报帧 → CanFrameReceived 事件（CAN0 按键板 / CAN1 ECU 收帧）。
/// 设备断开自动重连，连接状态经 ConnectionChanged 事件通知（所有事件在后台线程触发，UI 需自行 Dispatcher 编派）。
/// </summary>
public sealed class UsbLink : IUsbLink, IDisposable
{
    public const ushort VendorId = 0x1183;
    public const ushort ProductId = 0x5850;

    private const ReadEndpointID EpIn = ReadEndpointID.Ep02;     // 设备 IN = 0x82
    private const WriteEndpointID EpOut = WriteEndpointID.Ep03;  // 设备 OUT = 0x03

    private readonly object _lock = new();
    // 主控板命令是有响应的串行事务。即使上位机同时触发了默认上电、测试步骤或手动调试，
    // 也不能让多个请求同时压到设备，否则设备会返回 ERR_BUSY。
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly byte[] _buf = new byte[512];
    private readonly List<byte> _acc = new(512);   // 字节流累积缓冲（读线程专用）
    private ushort _seq = 1;
    private readonly Dictionary<ushort, TaskCompletionSource<UsbResponse>> _pending = new();

    private UsbContext? _ctx;
    private UsbDeviceCollection? _devices;
    private IUsbDevice? _dev;

    private Thread? _reader;
    private volatile bool _running;

    public event Action<string>? Log;
    public event Action<bool>? ConnectionChanged;     // true=已连接 false=断开
    public event Action<CanFrame>? CanFrameReceived;  // 0x41 收到 CAN 帧

    public bool IsOpen { get { lock (_lock) return _dev != null && _dev.IsOpen; } }

    /// <summary>枚举并打开设备 + 启动后台读线程。返回 "" = 成功，否则中文错误串。</summary>
    public string Open()
    {
        if (IsOpen) return "已连接";
        lock (_lock)
        {
            _ctx ??= new UsbContext();
            string err = TryOpenDeviceLocked();
            if (err != "") return err;
        }
        StartReader();
        ConnectionChanged?.Invoke(true);
        return "";
    }

    public void Close()
    {
        lock (_lock) _running = false;
        var t = _reader;
        _reader = null;
        t?.Join(400);

        List<KeyValuePair<ushort, TaskCompletionSource<UsbResponse>>> pend;
        lock (_lock)
        {
            try { _dev?.ReleaseInterface(0); } catch { }
            try { _dev?.Close(); } catch { }
            _dev = null;
            try { _devices?.Dispose(); } catch { }
            _devices = null;
            try { _ctx?.Dispose(); } catch { }
            _ctx = null;
            pend = _pending.ToList();
            _pending.Clear();
        }
        foreach (var kv in pend) kv.Value.TrySetResult(UsbResponse.Fail("已关闭"));
        ConnectionChanged?.Invoke(false);
    }

    /// <summary>发一帧请求并异步等待匹配响应（读线程喂结果）。返回 (ok, err, status, payload)。</summary>
    public async Task<(bool ok, string err, byte status, byte[] payload)> SendAndWaitAsync(
        byte cmd, byte[]? payload, int timeoutMs = 1000)
    {
        // QUICK: do not transmit removed old ECU/KEY power or SOL commands to new firmware.
        // Old configs remain readable, but physical writes require an explicit compatible adapter.
        if (Frame.IsObsoleteControlCommand(cmd))
        {
            var warning = $"BLOCKED_LEGACY_USB_CMD 0x{cmd:X2}：新版主控已移除此命令；未发送任何 USB 数据。";
            Log?.Invoke(warning);
            return (false, warning, Frame.STS_ERR_CMD, Array.Empty<byte>());
        }
        await _requestGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // CAN_SEND 可能因为主控板发送邮箱刚释放而短暂返回 ERR_BUSY。
            // 这是可恢复的瞬时状态，不能让自动测试把一次动作直接判为执行错误。
            return await TransientCommandRetry.ExecuteAsync(
                () => SendAndWaitCoreAsync(cmd, payload, timeoutMs),
                result => cmd == Frame.CMD_CAN_SEND && result.status == Frame.STS_ERR_BUSY,
                maxRetries: cmd == Frame.CMD_CAN_SEND ? 8 : 0,
                delayMs: 25).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private Task<(bool ok, string err, byte status, byte[] payload)> SendAndWaitCoreAsync(
        byte cmd, byte[]? payload, int timeoutMs)
    {
        ushort seq;
        byte[] req;
        IUsbDevice? dev;
        lock (_lock)
        {
            seq = _seq++;
            req = Frame.MakeRequest(seq, cmd, payload ?? Array.Empty<byte>());
            dev = _dev;
            if (dev == null) return Task.FromResult((false, "未连接", (byte)0, Array.Empty<byte>()));
        }

        var tcs = new TaskCompletionSource<UsbResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _pending[seq] = tcs;

        try
        {
            var writer = dev.OpenEndpointWriter(EpOut);
            var ec = writer.Write(req, timeoutMs, out int written);
            if (ec != Error.Success) return FailPending(seq, tcs, $"写端点失败: {ec}");
            if (written != req.Length) return FailPending(seq, tcs, $"只写入 {written}/{req.Length} 字节");
        }
        catch (Exception ex)
        {
            return FailPending(seq, tcs, $"写端点异常: {ex.Message}");
        }

        return WaitForResponseAsync(tcs, seq, timeoutMs);
    }

    public void Dispose() => Close();

    /* ==================== 读线程 / 成帧 / 分派 ==================== */

    private void StartReader()
    {
        lock (_lock) _running = true;
        _reader = new Thread(ReaderLoop) { IsBackground = true, Name = "UsbRead" };
        _reader.Start();
    }

    private void ReaderLoop()
    {
        while (_running)
        {
            IUsbDevice? dev;
            lock (_lock) dev = _dev;
            if (dev == null)
            {
                if (!TryReconnect()) break;          // 断开期间自动重连
                ConnectionChanged?.Invoke(true);
                Log?.Invoke("已自动重连");
                continue;
            }

            try
            {
                var reader = dev.OpenEndpointReader(EpIn, 512);
                _acc.Clear();
                while (_running)
                {
                    var ec = reader.Read(_buf, 200, out int n);
                    if (ec == Error.Success && n > 0)
                    {
                        lock (_lock) { for (int i = 0; i < n; i++) _acc.Add(_buf[i]); }
                        DispatchFrames();
                    }
                    else if (ec == Error.Timeout)
                    {
                        continue;
                    }
                    else
                    {
                        Log?.Invoke($"读端点错误: {ec}");
                        OnDeviceLost();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"读端点异常: {ex.Message}");
                OnDeviceLost();
            }
        }
    }

    /// <summary>从累积字节中提取全部完整帧并分派（读线程内执行，加锁只保护缓冲/等待表）。</summary>
    private void DispatchFrames()
    {
        List<(ushort seq, byte cmd, byte status, byte[] payload)> frames = new();
        lock (_lock)
        {
            while (_acc.Count > 0)
            {
                int start = -1;
                for (int i = 0; i < _acc.Count; i++)
                    if (_acc[i] == Frame.SOF) { start = i; break; }
                if (start < 0) { _acc.Clear(); break; }
                if (start > 0) _acc.RemoveRange(0, start);           // 丢弃 SOF 前垃圾

                if (_acc.Count < 9) break;                            // 响应帧最小 9 字节，等更多
                byte len = _acc[5];
                if (len > 200) { _acc.RemoveAt(0); continue; }        // 防御：LEN 非法
                int total = 9 + len;
                if (_acc.Count < total) break;                        // 未收齐，等更多
                if (_acc[total - 1] != Frame.EOF) { _acc.RemoveAt(0); continue; }

                ushort crcCalc = Crc16.Compute(_acc, 1, 5 + len);     // SEQ..PAYLOAD
                ushort crcRecv = (ushort)(_acc[total - 3] | (_acc[total - 2] << 8));
                if (crcCalc != crcRecv) { _acc.RemoveAt(0); continue; }

                ushort seq = (ushort)(_acc[1] | (_acc[2] << 8));
                byte cmd = _acc[3];
                byte status = _acc[4];
                byte[] payload = _acc.GetRange(6, len).ToArray();
                _acc.RemoveRange(0, total);
                frames.Add((seq, cmd, status, payload));
            }
        }

        foreach (var f in frames)
        {
            if (f.seq != 0)
            {
                // 请求-响应配对（按 SEQ），服务等待者
                TaskCompletionSource<UsbResponse>? tcs;
                lock (_lock)
                {
                    if (_pending.Remove(f.seq, out tcs))
                        tcs.TrySetResult(UsbResponse.Success(f.cmd, f.status, f.payload));
                }
            }
            else if (f.cmd == Frame.CMD_CAN_RX_IND && Frame.TryParseCanRxInd(f.payload, out var can))
            {
                // 0x41 主动上报 → 广播给订阅者（CAN 帧视图 / ECU 上报）。
                // 时间戳在读线程打（单调时钟），供帧间隔测量，避免 UI 批处理压缩时间差。
                CanFrameReceived?.Invoke(can with { TimeMs = Environment.TickCount64 });
            }
            else
            {
                Log?.Invoke($"未认领上报帧 CMD=0x{f.cmd:X2} seq=0 payload={BitConverter.ToString(f.payload)}");
            }
        }
    }

    /* ==================== 连接管理 ==================== */

    private string TryOpenDeviceLocked()
    {
        try
        {
            // 注意：设备列表不能随便 Dispose（会连带释放设备句柄），只在换新列表时释放旧的。
            var list = _ctx!.List();
            var dev = list.FirstOrDefault(d => d.VendorId == VendorId && d.ProductId == ProductId);
            if (dev == null)
            {
                list.Dispose();
                return $"未找到设备 VID={VendorId:X4} PID={ProductId:X4}（确认驱动已装、线已接）";
            }
            dev.Open();
            dev.SetConfiguration(1);
            dev.ClaimInterface(0);

            var oldDev = _dev;
            var oldList = _devices;
            _dev = dev;
            _devices = list;
            if (oldDev != null) { try { oldDev.Close(); } catch { } }
            if (oldList != null) { try { oldList.Dispose(); } catch { } }
            Log?.Invoke($"已打开设备 VID={VendorId:X4} PID={ProductId:X4}");
            return "";
        }
        catch (Exception ex)
        {
            return $"打开失败: {ex.Message}";
        }
    }

    /// <summary>设备断开：释放句柄 + 让所有等待者立刻失败 + 通知 UI。返回后读线程进入重连循环。</summary>
    private void OnDeviceLost()
    {
        List<KeyValuePair<ushort, TaskCompletionSource<UsbResponse>>> pend;
        lock (_lock)
        {
            if (_dev == null) return;
            try { _dev.ReleaseInterface(0); } catch { }
            try { _dev.Close(); } catch { }
            _dev = null;
            pend = _pending.ToList();
            _pending.Clear();
        }
        foreach (var kv in pend) kv.Value.TrySetResult(UsbResponse.Fail("设备断开"));
        ConnectionChanged?.Invoke(false);
        Log?.Invoke("设备断开，等待重连...");
    }

    /// <summary>自动重连：每 1s 重扫一次，直到成功或停止。</summary>
    private bool TryReconnect()
    {
        while (_running)
        {
            Thread.Sleep(1000);
            if (!_running) return false;
            string err;
            lock (_lock) err = TryOpenDeviceLocked();
            if (err == "") return true;
        }
        return false;
    }

    private static Task<(bool, string, byte, byte[])> FailPending(
        ushort seq, TaskCompletionSource<UsbResponse> tcs, string err)
    {
        tcs.TrySetResult(UsbResponse.Fail(err));
        return Task.FromResult((false, err, (byte)0, Array.Empty<byte>()));
    }

    private async Task<(bool ok, string err, byte status, byte[] payload)> WaitForResponseAsync(
        TaskCompletionSource<UsbResponse> tcs, ushort seq, int timeoutMs)
    {
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (done != tcs.Task)
        {
            lock (_lock) _pending.Remove(seq);
            return (false, "读响应超时", (byte)0, Array.Empty<byte>());
        }
        var r = await tcs.Task.ConfigureAwait(false);
        return (r.Ok, r.Err, r.Status, r.Payload);
    }
}
