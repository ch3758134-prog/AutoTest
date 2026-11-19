using System.Buffers.Binary;
using AutoTest.Contracts;
namespace AutoTest.Devices.Usb;

/// <summary>Transport-level fail-closed guard. It applies even when a caller bypasses the debug UI
/// and calls UsbSessionService directly. A CAN lease is NOT physical authorization.</summary>
public sealed class PhysicalCommandFence
{
    private readonly object _sync = new();
    private byte[]? _singleCanPayload;
    private DateTimeOffset _canExpiry;
    private bool? _oneIgnState;
    private DateTimeOffset _ignExpiry;
    private long _ignGeneration;
    private byte[]? _singleClampPayload;
    private long _clampGeneration;
    private DateTimeOffset _clampExpiry;
    private byte[]? _singleLedPayload;
    private long _ledGeneration;
    private DateTimeOffset _ledExpiry;
    private long _authorizedConnection;
    private HashSet<(byte Port, bool Extended, uint Id)>? _runCan;
    private bool _runIgn;
    private int _remainingWrites;
    private DateTimeOffset _runExpiry;
    private long _runGeneration;
    private string? _runFingerprint;
    private HashSet<string>? _manualBatchCan;
    private long _manualBatchGeneration;
    private DateTimeOffset _manualBatchExpiry;
    private int _manualBatchWrites;
    private string? _manualBatchFingerprint;
    private byte[][]? _manualKeySteps;
    private int _manualKeyIndex;
    private long _manualKeyGeneration;
    private DateTimeOffset _manualKeyExpiry;

    /// <summary>
    /// One deliberate physical-run authorization. Limited to the exact CAN arbitration
    /// IDs chosen at preparation, a single live USB generation and a bounded duration.
    /// No implicit approval by a software configuration or a CAN lease.
    /// </summary>
    public void AuthorizePhysicalRun(string fingerprint, string typedConfirmation,
        IEnumerable<(byte Port, bool Extended, uint Id)> allowedCan,
        bool allowIgn, long generation, int maxWrites = 1000)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64 ||
            !fingerprint.All(Uri.IsHexDigit) ||
            typedConfirmation != "AUTHORIZE TEST RUN " + fingerprint)
            throw new UnauthorizedAccessException("Exact plan SHA256 consent is required.");
        if (generation <= 0 || maxWrites is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxWrites));
        var entries = allowedCan.ToHashSet();
        if (entries.Any(x => x.Port > 1 || x.Id > (x.Extended ? 0x1FFFFFFFu : 0x7FFu)))
            throw new ArgumentException("Invalid allowed CAN frame.");
        lock (_sync)
        {
            if (_runFingerprint is not null || _manualBatchCan is not null || _manualKeySteps is not null)
                throw new InvalidOperationException("Another physical run, CAN task or KEY action is active.");
            // Test authorization supersedes all pending manual one-shot grants.
            _singleCanPayload = null;
            _authorizedConnection = 0;
            _canExpiry = default;
            _oneIgnState = null;
            _ignGeneration = 0;
            _ignExpiry = default;
            _singleClampPayload = null;
            _clampGeneration = 0;
            _clampExpiry = default;
            _singleLedPayload = null;
            _ledGeneration = 0;
            _ledExpiry = default;
            _runCan = entries;
            _runIgn = allowIgn;
            _remainingWrites = maxWrites;
            _runExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
            _runGeneration = generation;
            _runFingerprint = fingerprint;
        }
    }

    public void RevokePhysicalRun()
    {
        lock (_sync)
        {
            _runCan = null;
            _runIgn = false;
            _runGeneration = 0;
            _runExpiry = default;
            _runFingerprint = null;
            _remainingWrites = 0;
        }
    }

    /// <summary>Time- and write-limited allowlist of exact ECU CAN frames for the manual bench.
    /// The operator approves a content-fingerprinted snapshot; subsequent UI edits never widen it.</summary>
    public void AuthorizeManualCanBatch(string fingerprint, string confirmation,
        IEnumerable<byte[]> exactCanPayloads, long generation, int maxWrites = 3000)
    {
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit) ||
            confirmation != "AUTHORIZE BENCH CAN " + fingerprint)
            throw new UnauthorizedAccessException("Exact manual CAN batch fingerprint confirmation required.");
        if (generation <= 0 || maxWrites is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxWrites));
        var payloads = exactCanPayloads.Select(payload => payload.ToArray()).ToArray();
        if (payloads.Length is < 1 or > 32 || payloads.Any(payload =>
            payload.Length != 15 || payload[0] != StationCanWiring.DutPort || payload[1] > 1 || payload[6] > 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4)) >
                (payload[1] == 1 ? 0x1FFFFFFFu : 0x7FFu)))
            throw new ArgumentException("ECU CAN 手动发送必须配置 1～32 条有效报文。");
        lock (_sync)
        {
            if (_runFingerprint is not null || _manualBatchCan is not null)
                throw new InvalidOperationException("Physical test or manual batch is already authorized.");
            _singleCanPayload = null;
            _oneIgnState = null;
            _singleClampPayload = null;
            _manualBatchCan = payloads.Select(payload => Convert.ToHexString(payload)).ToHashSet(StringComparer.Ordinal);
            _manualBatchGeneration = generation;
            _manualBatchExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
            _manualBatchWrites = maxWrites;
            _manualBatchFingerprint = fingerprint;
        }
    }

    public void RevokeManualCanBatch()
    {
        lock (_sync)
        {
            _manualBatchCan = null;
            _manualBatchGeneration = 0;
            _manualBatchExpiry = default;
            _manualBatchWrites = 0;
            _manualBatchFingerprint = null;
        }
    }

    /// <summary>Ordered exact KEY CAN frames, at most 512 sends in one current USB generation.
    /// No implicit KEY release at end or on cancel.</summary>
    public void AuthorizeManualKeySequence(string sha, string confirmation,
        IReadOnlyList<byte[]> frames, long generation)
    {
        if (sha is null || sha.Length != 64 || !sha.All(Uri.IsHexDigit) ||
            confirmation != "AUTHORIZE KEY ACTION " + sha)
            throw new UnauthorizedAccessException("Exact KEY action SHA consent required.");
        if (generation <= 0 || frames.Count is < 1 or > 512 ||
            frames.Any(frame => !IsDocumentedKeySimFrame(frame)))
            throw new ArgumentException("KEY CAN 动作序列必须包含 1～512 条有效的按键控制报文。");
        lock (_sync)
        {
            if (_runFingerprint is not null || _manualKeySteps is not null)
                throw new InvalidOperationException("Physical TEST or KEY action is already authorized.");
            _singleCanPayload = null;
            _manualKeySteps = frames.Select(x => x.ToArray()).ToArray();
            _manualKeyIndex = 0;
            _manualKeyGeneration = generation;
            _manualKeyExpiry = DateTimeOffset.UtcNow.AddMinutes(31);
        }
    }

    public void RevokeManualKeySequence()
    {
        lock (_sync)
        {
            _manualKeySteps = null; _manualKeyIndex = 0;
            _manualKeyGeneration = 0; _manualKeyExpiry = default;
        }
    }

    public static bool IsReadOnly(byte command, ReadOnlySpan<byte> payload) =>
        payload.IsEmpty && (command is MainControllerCodec.Ping or MainControllerCodec.GetVersion
            or MainControllerCodec.GetIgn or MainControllerCodec.GetClamps or
               MainControllerCodec.GetLeds or 0x53);

    /// <summary>Requires an exact typed confirmation, one payload and a living connection.
    /// Not used by B0/B1/B2 and never called automatically on reconnect.</summary>
    private static bool IsDocumentedKeySimFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length != 15 || data[0] != StationCanWiring.KeypadPort || data[1] != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4)) != 0x730 || data[6] != 8 ||
            (data[10] & 0xF0) != 0 || data.Slice(11, 4).IndexOfAnyExcept((byte)0) >= 0)
            return false;
        if (data[7] == 0x01) return true;
        return data[7] == 0x80 && data[8] == 0 && data[9] == 0 && data[10] == 0;
    }

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
            if (_runFingerprint is not null ||
                (_manualKeySteps is not null && exact15Bytes[0] == StationCanWiring.KeypadPort) ||
                (_manualBatchCan is not null && !IsDocumentedKeySimFrame(exact15Bytes)))
                throw new InvalidOperationException("ECU CAN 发送任务运行中，仅允许已授权的单次 KEY CAN 按键命令。");
            _singleCanPayload = exact15Bytes.ToArray();
            _authorizedConnection = connectionGeneration;
            _canExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    /// <summary>One exact state, one USB generation, 15-second consent window.
    /// Consumed before USB I/O. Never authorizes fixture or a second IGN write.</summary>
    public void AuthorizeSingleIgn(bool on, string typedConfirmation, long connectionGeneration)
    {
        if (typedConfirmation != "AUTHORIZE IGN " + (on ? "ON" : "OFF"))
            throw new UnauthorizedAccessException("Exact one-shot IGN consent required.");
        if (connectionGeneration <= 0)
            throw new InvalidOperationException("Cannot authorize IGN without a live USB generation.");
        lock (_sync)
        {
            if (_runFingerprint is not null)
                throw new InvalidOperationException("Manual IGN is blocked while an automatic test run is authorized.");
            _oneIgnState = on;
            _ignGeneration = connectionGeneration;
            _ignExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    /// <summary>Exact two-byte independent clamp change; manual one-shot only.
    /// A CAN/IGN or TEST lease cannot authorize 0x18.</summary>
    public void AuthorizeSingleClamp(byte clamp, bool press, string typedConfirmation,
        long connectionGeneration)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        if (typedConfirmation != "AUTHORIZE CLAMP " + clamp + (press ? " PRESS" : " RELEASE"))
            throw new UnauthorizedAccessException("Exact clamp/action consent required.");
        if (connectionGeneration <= 0)
            throw new InvalidOperationException("Cannot authorize a clamp without a live USB generation.");
        lock (_sync)
        {
            if (_runFingerprint is not null)
                throw new InvalidOperationException("Manual clamp is blocked while an automatic test run is authorized.");
            _singleClampPayload = new byte[] { clamp, press ? (byte)1 : (byte)0 };
            _clampGeneration = connectionGeneration;
            _clampExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    /// <summary>Authorize one exact LED GPIO change on a single live USB generation.
    /// Neither physical-run approval nor a CAN transmit lease grants this command.</summary>
    public void AuthorizeSingleLed(byte index, bool on, string typedConfirmation,
        long connectionGeneration)
    {
        if (index is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(index));
        if (typedConfirmation != "AUTHORIZE LED " + index + (on ? " ON" : " OFF"))
            throw new UnauthorizedAccessException("Exact LED/index/level consent required.");
        if (connectionGeneration <= 0)
            throw new InvalidOperationException("Cannot authorize LED without live USB generation.");
        lock (_sync)
        {
            if (_runFingerprint is not null)
                throw new InvalidOperationException("Manual LED GPIO is blocked during automatic TEST.");
            _singleLedPayload = new byte[] { index, on ? (byte)1 : (byte)0 };
            _ledGeneration = connectionGeneration;
            _ledExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
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
            if (request.Command == MainControllerCodec.CanSend &&
                _manualKeySteps is not null && _manualKeyGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _manualKeyExpiry &&
                _manualKeyIndex < _manualKeySteps.Length &&
                request.Payload.AsSpan().SequenceEqual(_manualKeySteps[_manualKeyIndex]))
            {
                _manualKeyIndex++;
                return;
            }
            if (request.Command == MainControllerCodec.SetIgn &&
                request.Payload.Length == 1 && request.Payload[0] <= 1 &&
                _oneIgnState.HasValue && _oneIgnState.Value == (request.Payload[0] == 1) &&
                _ignGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _ignExpiry)
            {
                _oneIgnState = null; // consumes consent even when USB write fails
                _ignGeneration = 0;
                return;
            }
            if (request.Command == MainControllerCodec.SetLed &&
                request.Payload.Length == 2 && _singleLedPayload is not null &&
                _ledGeneration == connectionGeneration && DateTimeOffset.UtcNow < _ledExpiry &&
                request.Payload.AsSpan().SequenceEqual(_singleLedPayload))
            {
                _singleLedPayload = null; // consume before USB write; not a blanket LED grant
                _ledGeneration = 0;
                _ledExpiry = default;
                return;
            }
            if (request.Command == MainControllerCodec.SetClamp &&
                request.Payload.Length == 2 && _singleClampPayload is not null &&
                _clampGeneration == connectionGeneration && DateTimeOffset.UtcNow < _clampExpiry &&
                request.Payload.AsSpan().SequenceEqual(_singleClampPayload))
            {
                _singleClampPayload = null; // consumed before the USB write
                _clampGeneration = 0;
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
        lock (_sync)
        {
            if (request.Command == MainControllerCodec.CanSend &&
                _manualBatchCan is not null &&
                _manualBatchGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _manualBatchExpiry && _manualBatchWrites > 0 &&
                _manualBatchCan.Contains(Convert.ToHexString(request.Payload)))
            {
                _manualBatchWrites--;
                return;
            }
            if (_runFingerprint is not null && _runCan is not null &&
                _runGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _runExpiry && _remainingWrites > 0)
            {
                bool allowed = false;
                if (request.Command == MainControllerCodec.CanSend && request.Payload.Length == 15 &&
                    request.Payload[0] <= 1 && request.Payload[1] <= 1 &&
                    request.Payload[6] <= 8)
                {
                    uint id = BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(2, 4));
                    allowed = _runCan.Contains((request.Payload[0], request.Payload[1] == 1, id));
                }
                else if (request.Command == MainControllerCodec.SetIgn &&
                    request.Payload.Length == 1 && request.Payload[0] <= 1)
                    allowed = _runIgn;
                if (allowed) { _remainingWrites--; return; }
            }
        }
        throw new UnauthorizedAccessException($"USB command 0x{request.Command:X2} is physically blocked by default.");
    }

    public void Reset()
    {
        lock (_sync)
        {
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _oneIgnState = null; _ignExpiry = default; _ignGeneration = 0;
            _singleClampPayload = null; _clampExpiry = default; _clampGeneration = 0;
            _singleLedPayload = null; _ledExpiry = default; _ledGeneration = 0;
            _runCan = null; _runIgn = false; _remainingWrites = 0;
            _runExpiry = default; _runGeneration = 0; _runFingerprint = null;
            _manualBatchCan = null; _manualBatchGeneration = 0;
            _manualBatchExpiry = default; _manualBatchWrites = 0;
            _manualBatchFingerprint = null;
            _manualKeySteps = null; _manualKeyIndex = 0;
            _manualKeyGeneration = 0; _manualKeyExpiry = default;
        }
    }
}
