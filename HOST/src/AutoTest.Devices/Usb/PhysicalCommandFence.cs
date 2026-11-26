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
    // Station execution owns this scoped fixture permit across FLASH/TEST,
    // but a fixture write is never permitted WHILE either stage is active.
    // It cannot be created by a barcode or software-only production view.
    private string? _stationFixtureFingerprint;
    private long _stationFixtureGeneration;
    private DateTimeOffset _stationFixtureExpiry;
    private byte _stationFixtureMask;
    private bool _stationFixtureIgnAllowed;
    private bool? _stationFixtureIgnExact;
    private byte[]? _stationFixtureExact;
    private byte[]? _singleLedPayload;
    private long _ledGeneration;
    private DateTimeOffset _ledExpiry;
    private long _authorizedConnection;
    private byte? _baudPermit;
    private long _baudPermitEpoch;
    private DateTimeOffset _baudPermitExpires;
    private HashSet<(byte Port, bool Extended, uint Id)>? _runCan;
    private bool _runIgn;
    private int _remainingWrites;
    private DateTimeOffset _runExpiry;
    private long _runGeneration;
    private string? _runFingerprint;
    private readonly Dictionary<Guid, string> _manualCanRows = new();
    private long _manualCanRowsGeneration;
    private HashSet<string>? _manualBatchCan;
    private long _manualBatchGeneration;
    private DateTimeOffset _manualBatchExpiry;
    private int? _manualBatchWrites;
    private string? _manualBatchFingerprint;
    private byte[][]? _manualKeySteps;
    private int _manualKeyIndex;
    private long _manualKeyGeneration;
    private DateTimeOffset _manualKeyExpiry;

    // Programming is separate from F1 DID and physical TEST RUN authorization.
    // Only a single, exact CAN1 frame is queued at a time. A matching authorized
    // job fingerprint alone NEVER permits arbitrary CAN frames or output commands.
    private sealed class ProgrammingGrant
    {
        public required Guid Token;
        public required long Generation;
        public required uint AppId, BootId;
        public required DateTimeOffset Expires;
        public required int FrameLimit;
        public int Consumed;
        public byte[]? NextExact;
    }
    private ProgrammingGrant? _programming;

    public static string FlashConfirmation(string planSha256) =>
        "AUTHORIZE ECU FLASH " + planSha256;

    public Guid AuthorizeProgrammingJob(string planSha256,string typed,
        uint appId,uint bootId,long generation,int frameLimit)
    {
        if(planSha256.Length!=64 || !planSha256.All(Uri.IsHexDigit) ||
            !string.Equals(typed,FlashConfirmation(planSha256),StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact frozen ECU flash SHA256 authorization required.");
        if(generation<=0 || frameLimit is < 1 or > 500000 ||
            appId>0x1FFFFFFF || bootId>0x1FFFFFFF || appId==0 || bootId==0)
            throw new ArgumentException("Invalid physical flash grant scope.");
        lock(_sync)
        {
            if(_programming is not null || _runFingerprint is not null || _didGrant is not null ||
                _manualBatchCan is not null || _manualCanRows.Count != 0 || _manualKeySteps is not null)
                throw new InvalidOperationException("Another physical CAN scope is already active.");
            // Cancel residual one-shot permissions from the main station before
            // transferring physical authority to a frozen programming job.
            _singleCanPayload = null; _authorizedConnection = 0; _canExpiry = default;
            _oneIgnState = null; _ignGeneration = 0; _ignExpiry = default;
            _singleClampPayload = null; _clampGeneration = 0; _clampExpiry = default;
            _stationFixtureExact = null; // No pending 0x18 during FLASH.
            _stationFixtureIgnExact = null;
            _singleLedPayload = null; _ledGeneration = 0; _ledExpiry = default;
            _baudPermit = null; _baudPermitEpoch = 0; _baudPermitExpires = default;
            var token=Guid.NewGuid();
            _programming=new ProgrammingGrant
            {
                Token=token,Generation=generation,AppId=appId,BootId=bootId,
                FrameLimit=frameLimit,Expires=DateTimeOffset.UtcNow.AddMinutes(40)
            };
            return token;
        }
    }

    /// <summary>Queues one exact 15-byte CAN1 USB payload, consumed before native WritePipe.
    /// This is NOT public arbitrary CAN authority: a live approved programming token
    /// and one of the two frozen APP/BOOT TX IDs are required for every frame.</summary>
    public void PrepareProgrammingCanFrame(Guid token,ReadOnlySpan<byte> payload,long generation)
    {
        lock(_sync)
        {
            var g=_programming;
            if(g is null || token==Guid.Empty || g.Token!=token ||
                g.Generation!=generation || DateTimeOffset.UtcNow>=g.Expires ||
                g.Consumed>=g.FrameLimit || g.NextExact is not null)
                throw new UnauthorizedAccessException("Missing or exhausted physical programming frame permit.");
            if(payload.Length!=15 || payload[0]!=1 || payload[1]>1 || payload[6]>8 ||
                payload[6]<1)
                throw new ArgumentException("Programming transport must use classic CAN1.");
            uint id=BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2,4));
            if((id!=g.AppId && id!=g.BootId) ||
                payload[1]!=(id>0x7FFu?1:0))
                throw new UnauthorizedAccessException("Programming CAN frame ID outside approved APP/BOOT TX scope.");
            g.NextExact=payload.ToArray();
        }
    }
    public void RevokeProgrammingJob(Guid token)
    {
        lock(_sync)
            if(_programming?.Token==token) _programming=null;
    }
    private bool TryConsumeProgramming(UsbRequest request,long generation)
    {
        var g=_programming;
        if(g is null || g.Generation!=generation || DateTimeOffset.UtcNow>=g.Expires ||
            g.Consumed>=g.FrameLimit || g.NextExact is null ||
            request.Command!=MainControllerCodec.CanSend ||
            !request.Payload.AsSpan().SequenceEqual(g.NextExact)) return false;
        g.NextExact=null;
        g.Consumed++;
        return true;
    }

    // Separate one-DID grant. Never piggyback on the broad physical test-run whitelist.
    private sealed class DidGrant
    {
        public required uint TxId, RxId;
        public required ushort Did;
        public required long Generation;
        public required DateTimeOffset Expires;
        public bool RequestSent;
        public bool ResponseStarted;
        public bool FlowPending;
        public int RemainingConsecutive;
        public int SinceFlow;
        public int ExpectedSequence = 1;
        public int FlowCount;
    }
    private DidGrant? _didGrant;

    public static string ReadDidConfirmation(uint txId, uint rxId, ushort did) =>
        $"AUTHORIZE DID 0x{did:X4} TX 0x{txId:X} RX 0x{rxId:X} CAN1";

    /// <summary>Single explicit 0x22 DID read on physical CAN1, one USB generation,
    /// bounded to 30 seconds. Cannot authorize 0x10, 0x27 or any programming SID.</summary>
    public void AuthorizeReadOnlyDid(uint txId, uint rxId, ushort did, string typedConfirmation, long generation)
    {
        if (generation <= 0 || txId > 0x1FFFFFFFu || rxId > 0x1FFFFFFFu ||
            txId == rxId || (txId > 0x7FFu) != (rxId > 0x7FFu))
            throw new ArgumentException("Invalid or mixed-size CAN1 UDS endpoint.");
        if (!string.Equals(typedConfirmation, ReadDidConfirmation(txId,rxId,did), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact DID/ID/CAN1 physical consent is required.");
        lock (_sync)
        {
            if (_didGrant is not null || _programming is not null ||
                _runFingerprint is not null || _manualBatchCan is not null || _manualCanRows.Count != 0 ||
                _manualKeySteps is not null)
                throw new InvalidOperationException("A physical TEST, manual task, DID or flash job is already authorized.");
            _didGrant = new DidGrant { TxId=txId, RxId=rxId, Did=did,
                Generation=generation, Expires=DateTimeOffset.UtcNow.AddSeconds(30) };
        }
    }
    public void RevokeReadOnlyDid() { lock (_sync) _didGrant=null; }

    /// <summary>Only UsbSessionService may feed decoded, CRC-checked 0x41 indications
    /// from the connected WinUSB reader. UI/callers cannot manufacture an FF grant.</summary>
    internal void ObserveAuthenticatedCanRx(ReadOnlySpan<byte> payload,long generation)
    {
        lock (_sync)
        {
            var d = _didGrant;
            if (d is null || !d.RequestSent || d.Generation != generation ||
                DateTimeOffset.UtcNow >= d.Expires || payload.Length != 15 ||
                payload[0] != 1 || payload[1] != (d.RxId > 0x7FFu ? 1 : 0) ||
                BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2,4)) != d.RxId)
                return;
            int dlc=payload[6];
            if (dlc is < 1 or > 8) { _didGrant=null; return; }
            byte pci=payload[7];
            if (!d.ResponseStarted)
            {
                // An ECU 0x7F 0x22 0x78 pending SF is permitted but grants no FC.
                if ((pci & 0xF0) != 0x10) return;
                if (dlc != 8) { _didGrant=null; return; }
                int size=((pci&0x0F)<<8)|payload[8];
                // FirstFrame must actually begin the positive response 0x62 + exact DID.
                if (size is < 8 or > 4095 || payload[9] != 0x62 ||
                    payload[10] != (byte)(d.Did>>8) || payload[11] != (byte)d.Did)
                { _didGrant=null; return; }
                d.ResponseStarted=true;
                d.RemainingConsecutive=(size-6+6)/7;
                d.FlowPending=true;
                return;
            }
            if ((pci & 0xF0) != 0x20 || d.FlowPending || d.RemainingConsecutive <= 0 ||
                (pci & 0x0F) != d.ExpectedSequence || dlc < 2)
            { _didGrant=null; return; }
            d.RemainingConsecutive--;
            d.ExpectedSequence=(d.ExpectedSequence+1)&0xF;
            d.SinceFlow++;
            if (d.RemainingConsecutive>0 && d.SinceFlow==8)
            { d.FlowPending=true; d.SinceFlow=0; }
        }
    }

    private bool TryConsumeReadDid(UsbRequest request,long generation)
    {
        var d=_didGrant;
        if (d is null || d.Generation!=generation || DateTimeOffset.UtcNow>=d.Expires ||
            request.Command!=MainControllerCodec.CanSend || request.Payload.Length!=15)
            return false;
        var p=request.Payload;
        if (p[0]!=1 || p[1]!=(d.TxId>0x7FFu?1:0) ||
            BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2,4))!=d.TxId)
            return false;
        // No CAN padding tolerance: the exact ISO-TP bytes and DLC must match.
        if (!d.RequestSent && p[6]==4 && p[7]==3 && p[8]==0x22 &&
            p[9]==(byte)(d.Did>>8) && p[10]==(byte)d.Did &&
            p.AsSpan(11,4).SequenceEqual(new byte[4]))
        { d.RequestSent=true; return true; }
        if (d.RequestSent && d.ResponseStarted && d.FlowPending &&
            d.RemainingConsecutive>0 && d.FlowCount<75 && p[6]==3 &&
            p[7]==0x30 && p[8]==8 && p[9]==0 &&
            p.AsSpan(10,5).SequenceEqual(new byte[5]))
        { d.FlowCount++; d.FlowPending=false; return true; }
        return false;
    }

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
            if (_runFingerprint is not null || _manualBatchCan is not null || _manualCanRows.Count != 0 ||
                _manualKeySteps is not null || _programming is not null || _didGrant is not null)
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
            _stationFixtureExact = null; // No pending 0x18 during TEST.
            _stationFixtureIgnExact = null;
            _baudPermit = null;
            _baudPermitEpoch = 0;
            _baudPermitExpires = default;
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

    /// <summary>Extend only the existing operator-approved test scope.
    /// No CAN IDs or output permissions can be added by a renewal.</summary>
    public void RefreshPhysicalRun(string fingerprint, long generation, int maxWrites)
    {
        if (maxWrites is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxWrites));
        lock (_sync)
        {
            if (_runCan is null || _runFingerprint != fingerprint ||
                _runGeneration != generation || DateTimeOffset.UtcNow >= _runExpiry)
                throw new UnauthorizedAccessException("当前真实测试运行许可已失效，禁止续期。");
            _remainingWrites = maxWrites;
            _runExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
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

    /// <summary>Independent exact-frame grants for manually selected ECU CAN rows.</summary>
    public void AuthorizeManualCanRow(Guid token, ReadOnlySpan<byte> payload, long generation)
    {
        if (token == Guid.Empty || generation <= 0 || payload.Length != 15 ||
            payload[0] != StationCanWiring.DutPort || payload[1] > 1 || payload[6] > 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4)) >
                (payload[1] == 1 ? 0x1FFFFFFFu : 0x7FFu))
            throw new ArgumentException("ECU CAN 发送必须是合法的完整经典 CAN 帧。");
        lock (_sync)
        {
            if (_runFingerprint is not null || _programming is not null ||
                _didGrant is not null || _manualBatchCan is not null ||
                _stationFixtureFingerprint is not null ||
                (_manualCanRows.Count > 0 && _manualCanRowsGeneration != generation) ||
                _manualCanRows.Count >= 32)
                throw new InvalidOperationException("当前工位不能增加 CAN 发送任务。");
            _manualCanRows.Add(token, Convert.ToHexString(payload));
            _manualCanRowsGeneration = generation;
        }
    }

    public void RevokeManualCanRow(Guid token)
    {
        lock (_sync)
        {
            _manualCanRows.Remove(token);
            if (_manualCanRows.Count == 0) _manualCanRowsGeneration = 0;
        }
    }

    public void RevokeAllManualCanRows()
    {
        lock (_sync) { _manualCanRows.Clear(); _manualCanRowsGeneration = 0; }
    }

    /// <summary>Exact ECU CAN allowlist for the manual bench. Null budget means current-session continuous sending.
    /// The operator approves a content-fingerprinted snapshot; subsequent UI edits never widen it.</summary>
    public void AuthorizeManualCanBatch(string fingerprint, string confirmation,
        IEnumerable<byte[]> exactCanPayloads, long generation, int? maxWrites = 3000)
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
            if (_runFingerprint is not null || _manualBatchCan is not null || _manualCanRows.Count != 0 ||
                _programming is not null || _didGrant is not null ||
                _stationFixtureFingerprint is not null)
                throw new InvalidOperationException("Station execution or manual batch is already authorized.");
            _singleCanPayload = null;
            _oneIgnState = null;
            _singleClampPayload = null;
            _manualBatchCan = payloads.Select(payload => Convert.ToHexString(payload)).ToHashSet(StringComparer.Ordinal);
            _manualBatchGeneration = generation;
            _manualBatchExpiry = maxWrites.HasValue ? DateTimeOffset.UtcNow.AddMinutes(5) : DateTimeOffset.MaxValue;
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
            if (_runFingerprint is not null || _manualKeySteps is not null ||
                _programming is not null || _didGrant is not null ||
                _stationFixtureFingerprint is not null)
                throw new InvalidOperationException("Station execution or KEY action is already authorized.");
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
               MainControllerCodec.GetLeds or MainControllerCodec.GetEcuCanBaud or 0x53);

    /// <summary>Explicit operator Apply / consented FLASH only. Exactly one
    /// 0x42 payload, one USB generation, five seconds, consumed before I/O.</summary>
    public void AuthorizeSingleEcuCanBaud(byte code, long epoch)
    {
        if (code > 7 || epoch <= 0) throw new ArgumentOutOfRangeException(nameof(code));
        lock (_sync)
        {
            if (_runFingerprint is not null || _manualBatchCan is not null || _manualCanRows.Count != 0 ||
                _manualKeySteps is not null || _programming is not null || _didGrant is not null)
                throw new InvalidOperationException(
                    "ECU CAN 码率切换要求空闲的 TEST、CAN、KEY、UDS、FLASH 会话。");
            _singleCanPayload = null; _authorizedConnection = 0;
            _oneIgnState = null; _ignGeneration = 0;
            _singleClampPayload = null; _clampGeneration = 0;
            _singleLedPayload = null; _ledGeneration = 0;
            _baudPermit = code;
            _baudPermitEpoch = epoch;
            _baudPermitExpires = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }

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
            if (_runFingerprint is not null || _programming is not null || _didGrant is not null ||
                _stationFixtureFingerprint is not null ||
                (_manualKeySteps is not null && exact15Bytes[0] == StationCanWiring.KeypadPort) ||
                ((_manualBatchCan is not null || _manualCanRows.Count != 0) && !IsDocumentedKeySimFrame(exact15Bytes)))
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
            if (_runFingerprint is not null || _programming is not null ||
                _didGrant is not null || _stationFixtureFingerprint is not null)
                throw new InvalidOperationException("Manual IGN is blocked while station execution is authorized.");
            _oneIgnState = on;
            _ignGeneration = connectionGeneration;
            _ignExpiry = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    /// <summary>Explicit, plan-fingerprinted station fixture ownership, not
    /// a manual bench permit and not a blanket USB output authorization.
    /// The same scope survives the FLASH/TEST phases without allowing clamp
    /// commands inside them; each actual write requires a new exact grant.
    /// </summary>
    public void AuthorizeStationFixtureScope(string fingerprint,
        string confirmation, byte allowedMask, bool allowIgn, long generation)
    {
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit) ||
            confirmation != "AUTHORIZE STATION FIXTURE " + fingerprint ||
            allowedMask > 3 || (allowedMask == 0 && !allowIgn) || generation <= 0)
            throw new UnauthorizedAccessException("工位夹具运行许可必须绑定本次冻结工艺、输出和设备会话。");
        lock (_sync)
        {
            if (_stationFixtureFingerprint is not null || _programming is not null ||
                _runFingerprint is not null || _didGrant is not null ||
                _manualBatchCan is not null || _manualCanRows.Count != 0 ||
                _manualKeySteps is not null)
                throw new InvalidOperationException("工位正在由另一真实任务占用，不能取得夹具运行权限。");
            _stationFixtureFingerprint = fingerprint;
            _stationFixtureGeneration = generation;
            _stationFixtureMask = allowedMask;
            _stationFixtureIgnAllowed = allowIgn;
            _stationFixtureExpiry = DateTimeOffset.UtcNow.AddMinutes(60);
            _stationFixtureExact = null;
            _stationFixtureIgnExact = null;
            _singleCanPayload = null;
            _authorizedConnection = 0;
            _canExpiry = default;
            _oneIgnState = null;
            _ignGeneration = 0;
            _ignExpiry = default;
            _singleLedPayload = null;
            _ledGeneration = 0;
            _ledExpiry = default;
            _baudPermit = null;
            _baudPermitEpoch = 0;
            _baudPermitExpires = default;
            _singleClampPayload = null;
            _clampGeneration = 0;
            _clampExpiry = default;
        }
    }

    public void AuthorizeStationFixtureChange(string fingerprint,
        byte clamp, bool press, long generation)
    {
        if (clamp is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(clamp));
        lock (_sync)
        {
            if (_stationFixtureFingerprint != fingerprint ||
                _stationFixtureGeneration != generation ||
                DateTimeOffset.UtcNow >= _stationFixtureExpiry ||
                (_stationFixtureMask & (1 << (clamp - 1))) == 0 ||
                _runFingerprint is not null || _programming is not null ||
                _didGrant is not null || _manualKeySteps is not null ||
                _manualBatchCan is not null || _manualCanRows.Count != 0)
                throw new UnauthorizedAccessException(
                    "夹具动作超出本次工位授权、运行阶段或 USB 连接世代。");
            _stationFixtureExact = new byte[] { clamp, press ? (byte)1 : (byte)0 };
        }
    }

    public void AuthorizeStationIgnChange(string fingerprint, bool on, long generation)
    {
        lock (_sync)
        {
            if (_stationFixtureFingerprint != fingerprint ||
                _stationFixtureGeneration != generation ||
                !_stationFixtureIgnAllowed ||
                DateTimeOffset.UtcNow >= _stationFixtureExpiry ||
                _runFingerprint is not null || _programming is not null ||
                _didGrant is not null)
                throw new UnauthorizedAccessException("IGN 不属于当前工位已批准阶段或冻结动作。");
            _stationFixtureIgnExact = on;
        }
    }

    public void RevokeStationFixtureScope(string fingerprint, long generation)
    {
        lock (_sync)
        {
            if (_stationFixtureFingerprint != fingerprint ||
                _stationFixtureGeneration != generation) return;
            _stationFixtureFingerprint = null;
            _stationFixtureGeneration = 0;
            _stationFixtureExpiry = default;
            _stationFixtureMask = 0;
            _stationFixtureIgnAllowed = false;
            _stationFixtureIgnExact = null;
            _stationFixtureExact = null;
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
            if (_runFingerprint is not null || _programming is not null ||
                _didGrant is not null || _stationFixtureFingerprint is not null)
                throw new InvalidOperationException("Manual clamp is blocked while a station fixture or automatic run is authorized.");
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
            if (_runFingerprint is not null || _programming is not null ||
                _didGrant is not null || _stationFixtureFingerprint is not null)
                throw new InvalidOperationException("Manual LED GPIO is blocked during station execution.");
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
            // During a programming/DID grant no other write can borrow a
            // previously issued CAN/IGN/LED/clamp/manual-test authorization.
            if (TryConsumeProgramming(request, connectionGeneration)) return;
            if (TryConsumeReadDid(request, connectionGeneration)) return;
            if (_programming is not null || _didGrant is not null)
                throw new UnauthorizedAccessException(
                    "USB write denied: another dedicated ECU flash/diagnostic scope is active.");
            if (request.Command == MainControllerCodec.SetEcuCanBaud &&
                request.Payload.Length == 1 && request.Payload[0] <= 7 &&
                _baudPermit == request.Payload[0] &&
                _baudPermitEpoch == connectionGeneration &&
                DateTimeOffset.UtcNow < _baudPermitExpires &&
                _runFingerprint is null && _manualBatchCan is null && _manualCanRows.Count == 0 &&
                _manualKeySteps is null)
            {
                _baudPermit = null; _baudPermitEpoch = 0; _baudPermitExpires = default;
                return;
            }
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
            if (request.Command == MainControllerCodec.SetIgn &&
                request.Payload.Length == 1 && request.Payload[0] <= 1 &&
                _stationFixtureIgnExact is { } ign &&
                _stationFixtureGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _stationFixtureExpiry &&
                _runFingerprint is null && _programming is null && _didGrant is null &&
                ign == (request.Payload[0] == 1))
            {
                _stationFixtureIgnExact = null;
                return;
            }
            if (request.Command == MainControllerCodec.SetClamp &&
                request.Payload.Length == 2 && _stationFixtureExact is not null &&
                _stationFixtureGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _stationFixtureExpiry &&
                _runFingerprint is null && _programming is null && _didGrant is null &&
                request.Payload.AsSpan().SequenceEqual(_stationFixtureExact))
            {
                _stationFixtureExact = null; // exact, consumed before USB I/O
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
                _manualCanRows.Count > 0 && _manualCanRowsGeneration == connectionGeneration &&
                _manualCanRows.Values.Contains(Convert.ToHexString(request.Payload)))
                return;
            if (request.Command == MainControllerCodec.CanSend &&
                _manualBatchCan is not null &&
                _manualBatchGeneration == connectionGeneration &&
                DateTimeOffset.UtcNow < _manualBatchExpiry && (_manualBatchWrites is null or > 0) &&
                _manualBatchCan.Contains(Convert.ToHexString(request.Payload)))
            {
                if (_manualBatchWrites.HasValue) _manualBatchWrites--;
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
            _baudPermit = null; _baudPermitEpoch = 0; _baudPermitExpires = default;
            _oneIgnState = null; _ignExpiry = default; _ignGeneration = 0;
            _singleClampPayload = null; _clampExpiry = default; _clampGeneration = 0;
            _stationFixtureFingerprint = null; _stationFixtureGeneration = 0;
            _stationFixtureExpiry = default; _stationFixtureMask = 0;
            _stationFixtureIgnAllowed = false; _stationFixtureIgnExact = null;
            _stationFixtureExact = null;
            _singleLedPayload = null; _ledExpiry = default; _ledGeneration = 0;
            _programming = null; _didGrant = null;
            _runCan = null; _runIgn = false; _remainingWrites = 0;
            _runExpiry = default; _runGeneration = 0; _runFingerprint = null;
            _manualCanRows.Clear(); _manualCanRowsGeneration = 0;
            _manualBatchCan = null; _manualBatchGeneration = 0;
            _manualBatchExpiry = default; _manualBatchWrites = 0;
            _manualBatchFingerprint = null;
            _manualKeySteps = null; _manualKeyIndex = 0;
            _manualKeyGeneration = 0; _manualKeyExpiry = default;
        }
    }
}
