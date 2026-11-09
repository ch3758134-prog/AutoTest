using AutoTest.Devices.Station;

namespace AutoTest.Devices.Usb;

/// <summary>Transport-level fail-closed guard. It applies even when a caller bypasses the debug UI
/// and calls UsbSessionService directly. A CAN lease is NOT physical authorization.</summary>
public sealed class PhysicalCommandFence
{
    private readonly object _sync = new();
    private byte[]? _singleCanPayload;
    private DateTimeOffset _canExpiry;
    private long _authorizedConnection;
    private byte? _stationCommand, _stationValue;
    private long _stationConnection;
    private DateTimeOffset _stationExpiry;
    private byte[]? _keyFrame;
    private long _keyConnection;
    private DateTimeOffset _keyExpiry;

    public static bool IsReadOnly(byte command, ReadOnlySpan<byte> payload) =>
        payload.IsEmpty && (command is 0xF0 or 0xF1 or 0x11 or 0x13 or 0x15 or 0x17 or 0x1F);

    public static bool IsStationCommand(byte cmd) => cmd is 0x10 or 0x12 or 0x14 or 0x16;

    public static string StationConfirmation(byte command, byte value)
    {
        if (!IsStationCommand(command) || value > 1)
            throw new ArgumentOutOfRangeException(nameof(command));
        string operation = command switch
        {
            0x10 => "ECU POWER", 0x12 => "IGN", 0x14 => "KEY POWER", 0x16 => "SOLENOID",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        string valueName = command == 0x16 ? (value == 0 ? "UP" : "DOWN") : (value == 1 ? "ON" : "OFF");
        return $"AUTHORIZE {operation} {valueName}";
    }

    public void AuthorizeSingleStationCommand(byte command, byte value, string phrase, long generation)
    {
        if (!string.Equals(phrase, StationConfirmation(command, value), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact Station confirmation required.");
        if (generation <= 0) throw new InvalidOperationException("No active session.");
        lock (_sync)
        {
            _stationCommand = command; _stationValue = value;
            _stationConnection = generation; _stationExpiry = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }

    public void RevokeStationCommand()
    {
        lock (_sync)
        {
            _stationCommand = null; _stationValue = null; _stationConnection = 0; _stationExpiry = default;
        }
    }

    public void AuthorizeSingleKeyFrame(ReadOnlySpan<byte> exact15Bytes, string phrase, long generation)
    {
        if (!PhysicalKeypadProtocol.TryDecodeExactKeyFrame(exact15Bytes, out uint mask))
            throw new UnauthorizedAccessException("Only exact KeySim CAN0 0x730 frames are permitted.");
        if (!string.Equals(phrase, PhysicalKeypadProtocol.Confirmation(mask), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact full-mask confirmation required.");
        if (generation <= 0) throw new InvalidOperationException("No active session.");
        lock (_sync)
        {
            _keyFrame = exact15Bytes.ToArray(); _keyConnection = generation;
            _keyExpiry = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }

    public void RevokeKeyFrame()
    {
        lock (_sync) { _keyFrame = null; _keyConnection = 0; _keyExpiry = default; }
    }

    /// <summary>Requires an exact typed confirmation, one payload and a living connection.
    /// Not used by B0/B1/B2 and never called automatically on reconnect.</summary>
    public void AuthorizeSingleCanFrame(ReadOnlySpan<byte> exact15Bytes, string typedConfirmation,
        long connectionGeneration)
    {
        if (exact15Bytes.Length != 15 || exact15Bytes[0] > 1 || exact15Bytes[1] > 1 || exact15Bytes[6] > 8)
            throw new ArgumentException("Invalid physical CAN payload.", nameof(exact15Bytes));
        string expectation = "AUTHORIZE CAN TX " + Convert.ToHexString(exact15Bytes);
        if (!string.Equals(typedConfirmation, expectation, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Explicit exact-frame CAN consent was not provided.");
        if (connectionGeneration <= 0) throw new InvalidOperationException("No live connection.");
        lock (_sync)
        {
            _singleCanPayload = exact15Bytes.ToArray();
            _authorizedConnection = connectionGeneration;
            _canExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    public void AssertAllowedAndConsume(ReadOnlySpan<byte> rawRequest, long connectionGeneration)
    {
        if (!UsbWireCodec.TryDecodeRequest(rawRequest, out var request))
            throw new UnauthorizedAccessException("Rejected malformed USB request before physical write.");
        if (request.Sequence == 0)
            throw new UnauthorizedAccessException("Host requests must never use reserved SEQ=0.");
        if (IsReadOnly(request.Command, request.Payload)) return;
        lock (_sync)
        {
            if (_stationCommand == request.Command && _stationValue is byte value &&
                request.Payload.Length == 1 && request.Payload[0] == value &&
                _stationConnection == connectionGeneration && DateTimeOffset.UtcNow < _stationExpiry)
            {
                RevokeStationCommand();
                return;
            }
            if (request.Command == MainControllerCodec.CanSend && _keyFrame is not null &&
                _keyConnection == connectionGeneration && DateTimeOffset.UtcNow < _keyExpiry &&
                request.Payload.AsSpan().SequenceEqual(_keyFrame))
            {
                RevokeKeyFrame();
                return;
            }
            if (request.Command == MainControllerCodec.CanSend && _singleCanPayload is not null &&
                _authorizedConnection == connectionGeneration && DateTimeOffset.UtcNow < _canExpiry &&
                request.Payload.AsSpan().SequenceEqual(_singleCanPayload))
            {
                _singleCanPayload = null; // one shot, consumed BEFORE I/O, including failed writes.
                return;
            }
        }
        throw new UnauthorizedAccessException($"USB command 0x{request.Command:X2} is physically blocked by default.");
    }

    public void Reset()
    {
        lock (_sync)
        {
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _stationCommand = null; _stationValue = null; _stationConnection = 0; _stationExpiry = default;
            _keyFrame = null; _keyConnection = 0; _keyExpiry = default;
        }
    }
}
