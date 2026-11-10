using System.IO;
using System.IO.Ports;
using AutoTest.Specialists.Scan;

namespace AutoTest.Desktop.Tools.Production;

/// <summary>
/// Read-only input path for one operator-selected USB COM scanner.
/// This does not touch the ECU controller WinUSB connection or CAN bus.
/// </summary>
internal sealed class SerialScannerSession : IAsyncDisposable
{
    private readonly SerialBarcodeFramer _framer = new();
    private SerialPort? _port;
    private CancellationTokenSource? _cancel;
    private Task? _reader;
    public event Action<byte[]>? BytesReceived;
    public event Action<SerialBarcodePacket>? BarcodeReceived;
    public event Action<string>? Fault;
    public event Action<string>? FramingWarning;
    public event Action<bool>? ConnectionChanged;
    public string? PortName => _port?.IsOpen == true ? _port.PortName : null;
    public bool HasSession => _port is not null;
    public int BaudRate => _port?.BaudRate ?? 0;

    public static string[] AvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Connect(string com, int baud)
    {
        if (_port is not null) throw new InvalidOperationException("扫码枪串口已有连接，请先断开。");
        if (string.IsNullOrWhiteSpace(com) || !AvailablePorts().Contains(com, StringComparer.OrdinalIgnoreCase))
            throw new IOException("指定 COM 端口未找到，请查看设备管理器后刷新串口列表。");
        if (baud is < 1200 or > 921600)
            throw new ArgumentOutOfRangeException(nameof(baud), "串口波特率超出允许范围。");
        var port = new SerialPort(com, baud, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 150,
            WriteTimeout = 1000,
            DtrEnable = true
        };
        try
        {
            port.Open();
            _framer.Clear();
            _port = port;
            _cancel = new CancellationTokenSource();
            _reader = Task.Run(() => ReadLoop(port, _cancel.Token));
            ConnectionChanged?.Invoke(true);
        }
        catch
        {
            port.Dispose();
            _port = null;
            _cancel?.Dispose();
            _cancel = null;
            _reader = null;
            throw;
        }
    }
    private void ReadLoop(SerialPort port, CancellationToken ct)
    {
        var chunk = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int n = port.Read(chunk, 0, chunk.Length);
                    if (n == 0) continue;
                    byte[] bytes = chunk.AsSpan(0,n).ToArray();
                    BytesReceived?.Invoke(bytes);
                    int oldDropped = _framer.DroppedFrames;
                    var packets = _framer.Accept(bytes, DateTimeOffset.UtcNow);
                    if (_framer.DroppedFrames > oldDropped)
                        FramingWarning?.Invoke(_framer.LastError ?? "无效串口扫码帧");
                    foreach (var packet in packets) BarcodeReceived?.Invoke(packet);
                }
                catch (TimeoutException)
                {
                    var packet = _framer.FlushAfterIdle(DateTimeOffset.UtcNow,
                        TimeSpan.FromMilliseconds(450));
                    if (packet is not null) BarcodeReceived?.Invoke(packet);
                }
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested &&
            ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // COM port closing is expected during deliberate cancellation.
        }
        catch (Exception ex)
        {
            Fault?.Invoke(ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            ConnectionChanged?.Invoke(false);
        }
    }
    public async Task DisconnectAsync()
    {
        var port = _port;
        if (port is null) return;
        var cancel = _cancel;
        var reader = _reader;
        _port = null;
        _cancel = null;
        _reader = null;
        cancel?.Cancel();
        try { port.Close(); }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        if (reader is not null)
        {
            try { await reader.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        port.Dispose();
        cancel?.Dispose();
        _framer.Clear();
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
