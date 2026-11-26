using System.IO.Ports;
using System.Text.RegularExpressions;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Station-local connection settings, not a TEST/FLASH/production recipe.
/// The selected COM port is not proof of the manufacturer's physical identity.
/// </summary>
public sealed record SerialScannerPortSettings(string PortName, int BaudRate = 9600)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName) ||
            !Regex.IsMatch(PortName, @"^COM[1-9][0-9]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("请选择 Windows 中实际存在的 COM 端口。", nameof(PortName));
        if (BaudRate is not (1200 or 2400 or 4800 or 9600 or 19200 or 38400 or 57600 or 115200))
            throw new ArgumentOutOfRangeException(nameof(BaudRate), "不支持的扫码枪串口波特率。");
    }
}

/// <summary>
/// Real Windows USB-serial scanner input. Uses a dedicated short-timeout blocking
/// reader, not DataReceived event counts or SerialPort.BaseStream async cancellation.
/// The port is opened only after the operator selects it; no automatic reconnect.
/// </summary>
public sealed class SerialPortBarcodeLineSource : IVerifiedBarcodeLineSource, IAsyncDisposable
{
    private readonly SerialPort _port;
    private readonly SerialBarcodeFrameDecoder _decoder = new();
    private bool _disposed;
    public string LastFrameHex { get; private set; } = "";
    // Decoder-rejected frames are still physical observations, not DUT IDs.
    public string LastFrameText { get; private set; } = "";
    private string _lastIncompleteHex = "";
    // Diagnostic only: a device may send bytes without the required CR/LF
    // suffix. The caller may display these bytes but never accept a scan.
    public event Action<string, string>? IncompleteFrameObserved;

    private SerialPortBarcodeLineSource(SerialPort port) => _port = port;

    public static string[] GetAvailablePortNames() =>
        SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static SerialPortBarcodeLineSource Open(SerialScannerPortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var port = new SerialPort(settings.PortName, settings.BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 150,
            WriteTimeout = 500,
            DtrEnable = false,
            RtsEnable = false
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
            return new SerialPortBarcodeLineSource(port);
        }
        catch { port.Dispose(); throw; }
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ValueTask<string?>(Task.Run(() => ReadFrame(ct), ct));
    }

    private string? ReadFrame(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int value;
            SerialBarcodeFrame? frame;
            try
            {
                value = _port.ReadByte();
                if (value < 0) throw new IOException("扫码枪串口已经结束输入。");
                frame = _decoder.Push((byte)value, DateTimeOffset.UtcNow);
            }
            catch (TimeoutException)
            {
                // A CR-only scanner finishes its frame on idle. For CRLF,
                // the LF arrives first and both delimiter bytes are retained.
                frame = _decoder.FlushIdle();
                if (frame is null)
                {
                    var pending = _decoder.PendingText;
                    var pendingHex = _decoder.PendingRawHex;
                    if (pending.Length > 0 && pendingHex != _lastIncompleteHex)
                    {
                        _lastIncompleteHex = pendingHex;
                        IncompleteFrameObserved?.Invoke(pending, pendingHex);
                    }
                }
            }
            if (frame is null) continue;
            _lastIncompleteHex = "";
            LastFrameHex = frame.RawHex;
            LastFrameText = frame.Text ?? "";
            if (!frame.Accepted || frame.Text is not { } barcode)
                throw new InvalidDataException("扫码枪数据帧无效：" + frame.Code);
            return barcode;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
