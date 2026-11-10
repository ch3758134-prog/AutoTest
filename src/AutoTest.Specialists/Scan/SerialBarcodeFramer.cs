using System.Collections.Immutable;

namespace AutoTest.Specialists.Scan;

/// <summary>
/// Translates USB-serial scanner byte streams into framed barcodes.
/// CR, LF, CRLF, ETX terminate; scanners without terminators use an
/// explicit idle flush. No keyboard focus is involved.
/// </summary>
public sealed record SerialBarcodePacket(ImmutableArray<byte> Data, string Boundary);
public sealed class SerialBarcodeFramer
{
    public const int MaximumBytes = 512;
    private readonly List<byte> _current = [];
    private bool _discardUntilBoundary;
    private DateTimeOffset? _lastByteUtc;
    public int PendingBytes => _current.Count;
    public int DroppedFrames { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<SerialBarcodePacket> Accept(ReadOnlySpan<byte> input, DateTimeOffset now)
    {
        var completed = new List<SerialBarcodePacket>();
        foreach (byte b in input)
        {
            _lastByteUtc = now;
            if (b is 0x0D or 0x0A or 0x03)
            {
                if (_discardUntilBoundary) { _discardUntilBoundary = false; _current.Clear(); }
                else if (_current.Count > 0)
                {
                    completed.Add(new SerialBarcodePacket(_current.ToImmutableArray(),
                        b == 0x0D ? "CR" : b == 0x0A ? "LF" : "ETX"));
                    _current.Clear();
                }
                continue;
            }
            if (b == 0x02 && _current.Count == 0) continue; // common optional STX prefix
            if (_discardUntilBoundary) continue;
            if (b < 0x20 || b == 0x7F)
            {
                _current.Clear();
                _discardUntilBoundary = true;
                DroppedFrames++;
                LastError = $"Unsupported control byte 0x{b:X2}; discarded until next terminator.";
                continue;
            }
            if (_current.Count >= MaximumBytes)
            {
                _current.Clear();
                _discardUntilBoundary = true;
                DroppedFrames++;
                LastError = "SCAN_SERIAL_TOO_LONG: discarded until next terminator.";
                continue;
            }
            _current.Add(b);
        }
        return completed;
    }
    public SerialBarcodePacket? FlushAfterIdle(DateTimeOffset now, TimeSpan quiet)
    {
        if (_discardUntilBoundary || _current.Count == 0 || _lastByteUtc is null ||
            quiet <= TimeSpan.Zero || now - _lastByteUtc.Value < quiet)
            return null;
        var packet = new SerialBarcodePacket(_current.ToImmutableArray(), "IDLE");
        _current.Clear();
        return packet;
    }
    public void Clear()
    {
        _current.Clear();
        _discardUntilBoundary = false;
        _lastByteUtc = null;
        LastError = null;
    }
}
