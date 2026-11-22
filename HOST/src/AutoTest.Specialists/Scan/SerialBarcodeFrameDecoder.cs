using System.Text;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Exact byte framing for a USB virtual COM barcode scanner. A callback/read is
/// not a barcode. CR, LF and CRLF delimit one barcode; invalid frames are never
/// silently truncated into a valid serial number.
/// </summary>
public sealed record SerialBarcodeFrame(string? Text, string Code)
{
    public bool Accepted => Code == "SERIAL_FRAME_OK" && Text is not null;
}

public sealed class SerialBarcodeFrameDecoder
{
    private readonly StringBuilder _buffer = new();
    private readonly int _maximumLength;
    private readonly TimeSpan _maximumGap;
    private DateTimeOffset? _lastByte;
    private string? _reject;
    private bool _skipLf;

    public SerialBarcodeFrameDecoder(int maximumLength = 512, TimeSpan? maximumInterByteGap = null)
    {
        if (maximumLength is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        _maximumLength = maximumLength;
        _maximumGap = maximumInterByteGap ?? TimeSpan.FromSeconds(2);
        if (_maximumGap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumInterByteGap));
    }

    public void Reset()
    {
        _buffer.Clear();
        _lastByte = null;
        _reject = null;
        _skipLf = false;
    }

    public SerialBarcodeFrame? Push(byte value, DateTimeOffset time)
    {
        if (_skipLf && value == (byte)'\n') { _skipLf = false; return null; }
        _skipLf = false;
        if (value == (byte)'\r' || value == (byte)'\n')
        {
            var rejected = _reject;
            var text = _buffer.ToString();
            _buffer.Clear();
            _lastByte = null;
            _reject = null;
            _skipLf = value == (byte)'\r';
            if (rejected is not null) return new(null, rejected);
            return text.Length == 0 ? null : new(text, "SERIAL_FRAME_OK");
        }
        if (_lastByte is { } previous && time - previous > _maximumGap && _buffer.Length > 0)
        {
            // Discard through the delimiter; accepting the remaining suffix is unsafe.
            _buffer.Clear();
            _reject = "SERIAL_FRAME_GAP";
        }
        _lastByte = time;
        if (_reject is not null) return null;
        if (value is < 0x20 or > 0x7e)
        {
            _buffer.Clear();
            _reject = "SERIAL_FRAME_NON_ASCII";
            return null;
        }
        if (_buffer.Length >= _maximumLength)
        {
            _buffer.Clear();
            _reject = "SERIAL_FRAME_TOO_LONG";
            return null;
        }
        _buffer.Append((char)value);
        return null;
    }
}
