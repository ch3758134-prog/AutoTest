using System.Text;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Streaming byte framing for a physical USB COM barcode scanner.
/// CR, LF and CRLF end one scan; an incomplete read is never a barcode.
/// A CR is held until the following byte or read-idle boundary so the recorded
/// raw HEX contains the ACTUAL CRLF pair instead of silently dropping LF.
/// </summary>
public sealed record SerialBarcodeFrame(string? Text, string Code, string RawHex = "")
{
    public bool Accepted => Code == "SERIAL_FRAME_OK" && Text is not null;
}

public sealed class SerialBarcodeFrameDecoder
{
    private readonly StringBuilder _buffer = new();
    private readonly List<byte> _rawBytes = new();
    private readonly int _maximumLength;
    private readonly TimeSpan _maximumGap;
    private DateTimeOffset? _lastByte;
    private string? _reject;
    private bool _pendingCr;

    public SerialBarcodeFrameDecoder(int maximumLength = 512, TimeSpan? maximumInterByteGap = null)
    {
        if (maximumLength is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        _maximumLength = maximumLength;
        _maximumGap = maximumInterByteGap ?? TimeSpan.FromSeconds(2);
        if (_maximumGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumInterByteGap));
    }

    // Read-only incomplete data for a visible scanner diagnostic. It is
    // never a complete barcode and must not authorize identity or actions.
    public string PendingText => _buffer.ToString();
    public string PendingRawHex => Convert.ToHexString(_rawBytes.ToArray());

    public void Reset()
    {
        _buffer.Clear();
        _rawBytes.Clear();
        _lastByte = null;
        _reject = null;
        _pendingCr = false;
    }

    public SerialBarcodeFrame? Push(byte value, DateTimeOffset time)
    {
        if (_pendingCr)
        {
            // A paired LF belongs to the same raw physical frame.
            if (value == (byte)'\n')
            {
                AddRaw(value);
                return Complete();
            }
            // CR alone ended the previous frame. Preserve the next byte as the
            // beginning of the next frame rather than dropping or merging it.
            var prior = Complete();
            _ = Consume(value, time);
            return prior;
        }
        return Consume(value, time);
    }

    /// <summary>Call after a bounded idle SerialPort.ReadByte timeout.</summary>
    public SerialBarcodeFrame? FlushIdle() => _pendingCr ? Complete() : null;

    private SerialBarcodeFrame? Consume(byte value, DateTimeOffset time)
    {
        AddRaw(value);
        if (value == (byte)'\r')
        {
            _pendingCr = true;
            return null;
        }
        if (value == (byte)'\n') return Complete();

        if (_lastByte is { } previous &&
            time - previous > _maximumGap && _buffer.Length > 0)
        {
            // Keep rejecting through the next delimiter, otherwise a suffix
            // from a truncated scan could be mistaken for another serial.
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

    private void AddRaw(byte value)
    {
        // Bounded diagnostic evidence. Invalid/overlong frames never cause
        // unbounded buffering and do not become truncated accepted identities.
        if (_rawBytes.Count < 8192) _rawBytes.Add(value);
    }

    private SerialBarcodeFrame? Complete()
    {
        var code = _reject;
        var text = _buffer.ToString();
        var hex = Convert.ToHexString(_rawBytes.ToArray());
        Reset();
        if (code is not null) return new(null, code, hex);
        return text.Length == 0 ? null : new(text, "SERIAL_FRAME_OK", hex);
    }
}
