using System.IO;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Uds;

namespace AutoTest.Flash;

public sealed record CanToolFlashOptions(
    string SecurityDllPath, string TypedAuthorization, int AlignedBlockLength,
    int P2TimeoutMs, int P2StarTimeoutMs, int EraseTimeoutMs, int FlowControlTimeoutMs,
    bool InsertCanToolEndSignature, bool OperatorConfirmedEcuAndPower,
    bool OperatorConfirmedCanBitrate, bool AuthorizePhysicalProgramming)
{
    public void Validate()
    {
        if (AlignedBlockLength is < 16 or > 4096 ||
            (AlignedBlockLength & (AlignedBlockLength - 1)) != 0)
            throw new ArgumentException("CanTool BlockLength must be a power of two, 16..4096 bytes.");
        if (P2TimeoutMs is < 100 or > 30000 ||
            P2StarTimeoutMs < P2TimeoutMs || P2StarTimeoutMs > 120000 ||
            EraseTimeoutMs is < 1000 or > 180000 ||
            FlowControlTimeoutMs is < 100 or > 30000)
            throw new ArgumentException("Invalid P2, P2*, Erase or ISO-TP FC timeout.");
        if (!OperatorConfirmedEcuAndPower || !OperatorConfirmedCanBitrate)
            throw new InvalidOperationException(
                "Confirm the actual connected ECU, external power and effective CAN1 bitrate.");
        if (!AuthorizePhysicalProgramming)
            throw new UnauthorizedAccessException("Actual erase/programming requires explicit operator opt-in.");
        if (string.IsNullOrWhiteSpace(SecurityDllPath) || !Path.IsPathFullyQualified(SecurityDllPath))
            throw new InvalidOperationException("Select an absolute native ZLGKey provider DLL path.");
    }
}

public sealed record CanToolFlashStage(DateTimeOffset Utc, string Stage,
    string Outcome, string Detail, long ConfirmedBytes, long TotalBytes);
public sealed record CanToolFlashResult(
    string Status, long ConfirmedBytes, long TotalBytes, string LastStage, string Detail);
public sealed record CanToolProbeResult(string Mode, string ResponseHex, string? Error);

/// <summary>
/// Native-only CanTool ZLGKey ABI. No fallback LFSR/sa_demo algorithm is provided.
/// SafetyFile.dll from the user sample is managed and has no native export, so
/// it is diagnosed as incompatible instead of silently inventing a key.
/// </summary>
public sealed class NativeCanToolKeyProvider : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int ZlgKeyDelegate(
        [In] byte[] seed, ushort seedLength, uint level,
        [MarshalAs(UnmanagedType.LPStr)] string? variant,
        [Out] byte[] keyBuffer, out ushort keyLength);

    private readonly nint _library;
    private readonly ZlgKeyDelegate _key;
    public string Sha256 { get; }
    public NativeCanToolKeyProvider(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 512 or > 16_777_216)
            throw new InvalidDataException("Native Seed/Key DLL is missing or implausible.");
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        _library = NativeLibrary.Load(file.FullName);
        try
        {
            if (!NativeLibrary.TryGetExport(_library, "ZLGKey", out var pointer))
                throw new EntryPointNotFoundException(
                    "Selected DLL has no native ZLGKey export. Managed SafetyFile.dll cannot use this ABI.");
            _key = Marshal.GetDelegateForFunctionPointer<ZlgKeyDelegate>(pointer);
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public byte[] ComputeKey(ReadOnlySpan<byte> seed)
    {
        if (seed.Length is < 1 or > 128)
            throw new InvalidDataException("Invalid ECU security seed length.");
        byte[] input = seed.ToArray();
        byte[] buffer = new byte[64];
        try
        {
            int code = _key(input, checked((ushort)input.Length),
                0, null, buffer, out ushort size);
            if (code != 0 || size is < 1 or > 64)
                throw new InvalidOperationException(
                    "Native ZLGKey failed or returned an invalid key length; abort before programming.");
            return buffer.AsSpan(0, size).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
    public void Dispose() => NativeLibrary.Free(_library);
}

/// <summary>
/// CanTool's documented ECU sequence, with ONLY the CAN TX/RX device transport
/// replaced by AutoTest's single physical WinUSB CAN1 hub. Existing specialists
/// supply the ISO-TP framing (FF/FC/CF), not a second protocol implementation.
///
/// Reference CanTool stages:
/// 10 03 probe Boot then APP; APP 11 01 -> Boot 10 02; 27 01/02;
/// 31 01 FF00 erase; each address block 34 -> 36 -> 37 -> 31 01 FF01;
/// 11 01 reset. No fabricated responses, retries or mock execution.
/// </summary>
public sealed class CanToolFlashEngine
{
    private readonly HardwareBenchController _device;
    private readonly IProgress<CanToolFlashStage> _progress;
    private string _lastStage = "NONE";
    private long _confirmed;
    private long _total;
    private bool _eraseStarted;

    public CanToolFlashEngine(HardwareBenchController device, IProgress<CanToolFlashStage> progress)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    private void Emit(string stage, string outcome, string detail)
    {
        _lastStage = stage;
        _progress.Report(new(DateTimeOffset.UtcNow, stage, outcome, detail, _confirmed, _total));
    }

    public static ImmutableArray<FirmwareSegment> BuildCanToolAlignedBlocks(
        CanToolConfigurationSnapshot config, FirmwareImage image,
        int blockLength, bool signature)
    {
        if (blockLength is < 16 or > 4096 ||
            (blockLength & (blockLength - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(blockLength));
        FirmwareImageParser.ValidateSegments(image.Segments);
        if (!config.ImageFits(image)) throw new InvalidDataException(
            "Firmware segment lies outside the CanTool FlashStartAddr/FlashEndAddr inclusive region.");
        var blocks = new SortedDictionary<uint, byte[]>();
        byte[] GetBlock(uint addr)
        {
            if (blocks.TryGetValue(addr, out var existing)) return existing;
            ulong end = (ulong)addr + (ulong)blockLength;
            if (addr < config.FlashStart || end > config.EndExclusive)
                throw new InvalidDataException($"Aligned CanTool block 0x{addr:X8}..0x{end:X} exceeds XML Flash region.");
            if ((long)(blocks.Count + 1) * blockLength > FirmwareImageParser.MaxImageBytes)
                throw new InvalidDataException("CanTool aligned block map exceeds configured memory bound.");
            var fresh = Enumerable.Repeat((byte)0xFF, blockLength).ToArray();
            blocks.Add(addr, fresh);
            return fresh;
        }
        foreach (var segment in image.Segments)
        {
            var data = segment.Data.AsSpan();
            for (var i = 0; i < data.Length;)
            {
                ulong position = (ulong)segment.Address + (ulong)i;
                uint baseAddress = checked((uint)(position / (ulong)blockLength * (ulong)blockLength));
                int within = checked((int)(position - baseAddress));
                int length = Math.Min(blockLength - within, data.Length - i);
                data.Slice(i, length).CopyTo(GetBlock(baseAddress).AsSpan(within));
                i += length;
            }
        }
        // The reference CanTool writes 12 34 56 78 at FlashEnd-3. It is an
        // OEM-specific mutation, NOT universally safe. Only do so by explicit
        // operator selection (default false), never as an invisible side effect.
        if (signature)
        {
            if (config.EndExclusive < 4)
                throw new InvalidDataException("Invalid terminal signature position.");
            ReadOnlySpan<byte> marker = [0x12, 0x34, 0x56, 0x78];
            for (var i = 0; i < 4; i++)
            {
                ulong addr = config.EndExclusive - 4UL + (ulong)i;
                uint baseAddress = checked((uint)(addr / (ulong)blockLength * (ulong)blockLength));
                GetBlock(baseAddress)[checked((int)(addr - baseAddress))] = marker[i];
            }
        }
        return blocks.Select(p => new FirmwareSegment(p.Key,
            ImmutableArray.CreateRange(p.Value))).ToImmutableArray();
    }

    private sealed class ProgrammingUds(IsoTpConversation conversation,
        CanToolFlashOptions options, Action<string, string, string> log)
    {
        public async Task<byte[]> ExchangeAsync(byte[] pdu, TimeSpan? overrideTimeout,
            CancellationToken ct)
        {
            if (pdu.Length == 0 || pdu.Length > IsoTpFrameCodec.MaxClassicPdu)
                throw new InvalidDataException("UDS request exceeds supported classic ISO-TP size.");
            bool secret = pdu[0] is 0x27 or 0x36;
            log("UDS TX", "REQUEST", "SID=0x" + pdu[0].ToString("X2") +
                "; " + (secret ? "DATA=[REDACTED]" : Convert.ToHexString(pdu)));
            await conversation.SendPduAsync(pdu, ct).ConfigureAwait(false);
            var timeout = overrideTimeout ?? TimeSpan.FromMilliseconds(options.P2TimeoutMs);
            for (int pending = 0; ;)
            {
                byte[] response = await conversation.ReceivePduAsync(timeout, ct).ConfigureAwait(false);
                if (response.Length == 3 && response[0] == 0x7F && response[1] == pdu[0])
                {
                    log("UDS RX", "NRC", $"SID={pdu[0]:X2} NRC={response[2]:X2}");
                    if (response[2] == 0x78 && pending++ < 40)
                    {
                        timeout = TimeSpan.FromMilliseconds(options.P2StarTimeoutMs);
                        continue;
                    }
                    throw new InvalidDataException(
                        $"ECU negative response SID=0x{pdu[0]:X2} NRC=0x{response[2]:X2}.");
                }
                if (response.Length == 0 || response[0] != unchecked((byte)(pdu[0] + 0x40)))
                    throw new InvalidDataException(
                        $"Unexpected ECU response for 0x{pdu[0]:X2}.");
                log("UDS RX", "POSITIVE", "SID=0x" + response[0].ToString("X2") +
                    "; " + (secret ? "DATA=[REDACTED]" : Convert.ToHexString(response)));
                return response;
            }
        }
    }

    private async Task<byte[]> Exchange(ProgrammingUds uds, byte[] pdu,
        string phase, CancellationToken ct, int? timeoutMs = null)
    {
        Emit(phase, "REQUEST", $"UDS 0x{pdu[0]:X2}");
        try
        {
            byte[] response = await uds.ExchangeAsync(pdu,
                timeoutMs is null ? null : TimeSpan.FromMilliseconds(timeoutMs.Value), ct);
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Emit(phase, "ERROR", ex.GetType().Name + ": " + ex.Message);
            throw;
        }
    }

    public async Task<CanToolFlashResult> RunAsync(
        CanToolConfigurationSnapshot configuration, FirmwareImage image,
        CanToolFlashOptions options, CancellationToken ct)
    {
        _confirmed = 0; _total = 0; _eraseStarted = false; _lastStage = "PREFLIGHT";
        LeaseToken lease = default;
        bool hasLease = false;
        IDisposable? authorization = null;
        try
        {
            options.Validate();
            if (_device.State != UsbSessionState.Connected ||
                !_device.ProtocolVersionVerified || _device.Hub is null ||
                _device.Generation <= 0)
                throw new IOException("Connect and verify the real WinUSB main controller first.");
            if (_device.TraceDropped != 0 || _device.TraceError is not null ||
                _device.CanDropped != 0 || _device.CanMalformed != 0 ||
                _device.HubDropped != 0)
                throw new IOException("USB/CAN trace is not healthy; refuse erase.");
            if (configuration.AppRequestId == configuration.BootRequestId ||
                configuration.AppResponseId == configuration.BootResponseId)
                throw new InvalidDataException("CanTool APP and Boot endpoints must be distinct for mode switching.");
            var blocks = BuildCanToolAlignedBlocks(
                configuration, image, options.AlignedBlockLength,
                options.InsertCanToolEndSignature);
            _total = blocks.Sum(s => (long)s.Data.Length);
            Emit("PREFLIGHT", "PASS",
                $"ConfigSHA={configuration.SourceSha256}; imageSHA={image.SourceFingerprint.Sha256}; " +
                $"actual APP TX/RX={configuration.AppRequestId:X}/{configuration.AppResponseId:X}; " +
                $"BOOT TX/RX={configuration.BootRequestId:X}/{configuration.BootResponseId:X}; " +
                $"blocks={blocks.Length}; alignedBytes={_total}. " +
                "CAN bitrate is operator-confirmed externally, not configured by USB.");
            using var keyProvider = new NativeCanToolKeyProvider(options.SecurityDllPath);
            Emit("KEY_PROVIDER", "LOADED", $"Native ZLGKey export found; DLL SHA256={keyProvider.Sha256}. " +
                "Algorithm/ECU compatibility is NOT proven by ABI.");

            var leaseResult = await _device.PhysicalDutCanLeases.AcquireAsync(
                BusRole.DutCan, "CanTool-Standalone-Flash-" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromMinutes(25), ct);
            if (leaseResult.Outcome != Outcome.Accepted || leaseResult.Value.Value == Guid.Empty)
                throw new IOException("Cannot acquire exclusive physical CAN1 lease: " + leaseResult.FailureCode);
            lease = leaseResult.Value;
            hasLease = true;
            authorization = _device.AuthorizePhysicalCanToolFlash(
                lease, configuration.AppRequestId, configuration.BootRequestId,
                configuration.SourceSha256, image.SourceFingerprint.Sha256,
                options.TypedAuthorization, TimeSpan.FromMinutes(25), 250_000);
            var appProfile = new UdsEndpointProfile(BusRole.DutCan,
                configuration.AppRequestId, configuration.AppResponseId,
                TimeSpan.FromMilliseconds(options.P2TimeoutMs),
                TimeSpan.FromMilliseconds(options.P2StarTimeoutMs), "Normal");
            var bootProfile = new UdsEndpointProfile(BusRole.DutCan,
                configuration.BootRequestId, configuration.BootResponseId,
                TimeSpan.FromMilliseconds(options.P2TimeoutMs),
                TimeSpan.FromMilliseconds(options.P2StarTimeoutMs), "Normal");

            await using var appLink = new CanHubIsoTpLink(_device.Hub!, appProfile, lease);
            await using var bootLink = new CanHubIsoTpLink(_device.Hub!, bootProfile, lease);
            var isoOptions = new IsoTpOptions(
                TimeSpan.FromMilliseconds(options.FlowControlTimeoutMs));
            var app = new ProgrammingUds(new IsoTpConversation(appLink, isoOptions), options, Emit);
            var boot = new ProgrammingUds(new IsoTpConversation(bootLink, isoOptions), options, Emit);
            // Ensure real RX subscribers have entered the existing CAN Hub
            // before the first physical probe is transmitted.
            for (int attempt = 0; _device.Hub!.SubscriberCount < 2 && attempt < 120; attempt++)
                await Task.Delay(10, ct);
            if (_device.Hub.SubscriberCount < 2)
                throw new IOException("Physical APP/BOOT CAN response subscriptions did not start.");

            bool bootMode = false;
            Emit("PROBE", "START", "CanTool order: Boot 10 03, then APP 10 03.");
            try
            {
                var response = await Exchange(boot, [0x10, 0x03], "PROBE_BOOT", ct);
                if (response.Length < 2 || response[1] != 0x03)
                    throw new InvalidDataException("Boot 50 03 echo missing.");
                bootMode = true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Emit("PROBE_BOOT", "NO_RESPONSE", "Trying original APP endpoint. " + ex.Message);
            }
            if (!bootMode)
            {
                var response = await Exchange(app, [0x10, 0x03], "PROBE_APP", ct);
                if (response.Length < 2 || response[1] != 0x03)
                    throw new InvalidDataException("APP 50 03 echo missing.");
                var reset = await Exchange(app, [0x11, 0x01], "APP_RESET_TO_BOOT", ct);
                if (reset.Length < 2 || reset[1] != 0x01)
                    throw new InvalidDataException("APP 51 01 reset echo missing.");
                await Task.Delay(100, ct); // CanTool's APP->Boot transition wait
            }
            var session = await Exchange(boot, [0x10, 0x02], "BOOT_PROGRAM_SESSION", ct);
            if (session.Length < 2 || session[1] != 0x02)
                throw new InvalidDataException("Boot programming session 50 02 echo missing.");

            var seedResponse = await Exchange(boot, [0x27, 0x01], "SECURITY_SEED", ct);
            if (seedResponse.Length < 3 || seedResponse[1] != 0x01)
                throw new InvalidDataException("Invalid 67 01 ECU seed response.");
            byte[] seed = seedResponse.AsSpan(2).ToArray();
            byte[] key;
            try { key = keyProvider.ComputeKey(seed); }
            finally { CryptographicOperations.ZeroMemory(seed); }
            try
            {
                var keyRequest = new byte[key.Length + 2];
                keyRequest[0] = 0x27; keyRequest[1] = 0x02; key.CopyTo(keyRequest, 2);
                var unlocked = await Exchange(boot, keyRequest, "SECURITY_UNLOCK", ct);
                if (unlocked.Length < 2 || unlocked[1] != 0x02)
                    throw new InvalidDataException("Invalid 67 02 ECU security response.");
                CryptographicOperations.ZeroMemory(keyRequest);
            }
            finally { CryptographicOperations.ZeroMemory(key); }

            var eraseLength = checked((uint)configuration.FlashLength);
            var eraseRequest = new byte[12];
            eraseRequest[0]=0x31; eraseRequest[1]=0x01;
            eraseRequest[2]=0xFF; eraseRequest[3]=0x00;
            BinaryPrimitives.WriteUInt32BigEndian(eraseRequest.AsSpan(4, 4), configuration.FlashStart);
            BinaryPrimitives.WriteUInt32BigEndian(eraseRequest.AsSpan(8, 4), eraseLength);
            _eraseStarted = true;
            var eraseResponse = await Exchange(boot, eraseRequest, "ERASE_FULL_REGION", ct,
                options.EraseTimeoutMs);
            if (eraseResponse.Length < 4 ||
                !eraseResponse.AsSpan(0, 4).SequenceEqual(new byte[] { 0x71, 0x01, 0xFF, 0x00 }))
                throw new InvalidDataException("Erase routine 71 01 FF00 positive echo missing.");
            Emit("ERASE_FULL_REGION", "CONFIRMED", $"ECU answered erase request; address=0x{configuration.FlashStart:X8}; bytes={eraseLength}.");

            for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var block = blocks[blockIndex];
                int size = block.Data.Length;
                var download = new byte[11];
                download[0]=0x34; download[1]=0; download[2]=0x44;
                BinaryPrimitives.WriteUInt32BigEndian(download.AsSpan(3, 4), block.Address);
                BinaryPrimitives.WriteUInt32BigEndian(download.AsSpan(7, 4), checked((uint)size));
                var acceptDownload = await Exchange(boot, download,
                    $"BLOCK_{blockIndex+1}_DOWNLOAD", ct);
                uint maxRequest = UdsDownloadResponseParser.MaxTransferRequestBytes(acceptDownload);
                int chunk = checked((int)Math.Min((uint)(IsoTpFrameCodec.MaxClassicPdu - 2), maxRequest - 2));
                if (chunk <= 0) throw new InvalidDataException("0x74 maxNumberOfBlockLength invalid.");
                byte sequence = 1;
                int position = 0;
                var blockBytes = block.Data.ToArray();
                while (position < size)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = Math.Min(chunk, size - position);
                    var request = new byte[n+2];
                    request[0] = 0x36; request[1] = sequence;
                    Array.Copy(blockBytes, position, request, 2, n);
                    var acknowledgement = await Exchange(boot, request,
                        $"BLOCK_{blockIndex+1}_TRANSFER_{sequence:X2}", ct);
                    if (acknowledgement.Length != 2 || acknowledgement[1] != sequence)
                        throw new InvalidDataException("ECU 76 block sequence counter mismatch.");
                    position += n;
                    _confirmed += n;
                    Emit("TRANSFER_ACK", "ECU_ACK", $"Block {blockIndex+1}/{blocks.Length}; BSC=0x{sequence:X2}; bytes {_confirmed}/{_total}");
                    sequence = unchecked((byte)(sequence + 1));
                    CryptographicOperations.ZeroMemory(request);
                }
                var exit = await Exchange(boot, [0x37], $"BLOCK_{blockIndex+1}_TRANSFER_EXIT", ct);
                if (exit.Length < 1 || exit[0] != 0x77)
                    throw new InvalidDataException("ECU RequestTransferExit acknowledgement missing.");
                var verify = new byte[12];
                verify[0]=0x31; verify[1]=0x01;
                verify[2]=0xFF; verify[3]=0x01;
                BinaryPrimitives.WriteUInt32BigEndian(verify.AsSpan(4, 4), block.Address);
                BinaryPrimitives.WriteUInt32BigEndian(verify.AsSpan(8, 4), checked((uint)size));
                var checkedBytes = await Exchange(boot, verify,
                    $"BLOCK_{blockIndex+1}_VERIFY", ct, options.P2StarTimeoutMs);
                if (checkedBytes.Length < 6 ||
                    !checkedBytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x71, 0x01, 0xFF, 0x01 }))
                    throw new InvalidDataException("ECU 71 01 FF01 checksum response malformed or missing.");
                ushort ecuChecksum = BinaryPrimitives.ReadUInt16BigEndian(checkedBytes.AsSpan(checkedBytes.Length-2));
                ushort hostChecksum = CanToolChecksum(blockBytes);
                if (ecuChecksum != hostChecksum)
                    throw new InvalidDataException(
                        $"Block checksum mismatch at 0x{block.Address:X8}: ECU={ecuChecksum:X4}; host={hostChecksum:X4}.");
                Emit("BLOCK_VERIFIED", "MATCH", $"Block {blockIndex+1}/{blocks.Length} at 0x{block.Address:X8}; 16-bit complement checksum matched.");
            }

            var finalReset = await Exchange(boot, [0x11, 0x01], "ECU_RESET", ct);
            if (finalReset.Length < 2 || finalReset[1] != 0x01)
                throw new InvalidDataException("ECU reset 51 01 echo missing.");
            Emit("TRANSFER_COMPLETED", "FINAL_APP_NOT_VERIFIED",
                "CanTool protocol stages completed and ECU reset acknowledged. " +
                "The original CanTool implementation does NOT verify post-reset APP identity.");
            return new("TRANSFER_COMPLETED_FINAL_APP_UNVERIFIED", _confirmed, _total,
                _lastStage, "All CanTool transfer/verify stages acknowledged; APP re-identification not yet proven.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            string state = _eraseStarted ? "UNCERTAIN_AFTER_WRITE_CANCEL" : "CANCELLED_BEFORE_ERASE";
            Emit(_lastStage, state, "Operator cancelled; no automatic destructive retry.");
            return new(state, _confirmed, _total, _lastStage, "Inspect ECU state before any retry.");
        }
        catch (Exception ex)
        {
            string state = _eraseStarted ? "UNCERTAIN_AFTER_WRITE_FAILURE" : "FAILED_BEFORE_ERASE";
            Emit(_lastStage, state, ex.GetType().Name + ": " + ex.Message);
            return new(state, _confirmed, _total, _lastStage, ex.Message);
        }
        finally
        {
            authorization?.Dispose();
            if (hasLease)
            {
                try { await _device.PhysicalDutCanLeases.ReleaseAsync(lease, CancellationToken.None); }
                catch (Exception ex) { Emit("CLEANUP", "ERROR", "Failed to release CAN1 lease: " + ex.Message); }
            }
        }
    }

    /// <summary>
    /// CanTool APP/Boot detection without erase, security access, reset or
    /// firmware. It sends 0x10 03 and will change the ECU diagnostic session.
    /// A dedicated physical scope grants only the exact short UDS request.
    /// </summary>
    public async Task<CanToolProbeResult> ProbeAsync(
        CanToolConfigurationSnapshot config, CancellationToken ct)
    {
        LeaseToken lease = default;
        bool acquired = false;
        IDisposable? permit = null;
        try
        {
            if (_device.State != UsbSessionState.Connected ||
                !_device.ProtocolVersionVerified || _device.Hub is null)
                throw new IOException("Connect the physical WinUSB controller first.");
            Emit("PROBE_PREFLIGHT", "START",
                $"Boot={config.BootRequestId:X}/{config.BootResponseId:X}; " +
                $"App={config.AppRequestId:X}/{config.AppResponseId:X}; no erase or download.");
            var result = await _device.PhysicalDutCanLeases.AcquireAsync(
                BusRole.DutCan, "CanTool-Physical-Probe-" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromSeconds(35), ct);
            if (result.Outcome != Outcome.Accepted || result.Value.Value == Guid.Empty)
                throw new IOException("Physical CAN1 is busy: " + result.FailureCode);
            lease = result.Value; acquired = true;
            string approval = HardwareBenchController.PhysicalCanToolProbeConfirmation(
                config.SourceSha256, config.AppRequestId, config.BootRequestId,
                lease.SessionGeneration);
            permit = _device.AuthorizePhysicalCanToolProbe(lease,
                config.AppRequestId, config.BootRequestId,
                config.SourceSha256, approval);
            var app = new UdsEndpointProfile(
                BusRole.DutCan, config.AppRequestId, config.AppResponseId,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), "Normal");
            var boot = new UdsEndpointProfile(
                BusRole.DutCan, config.BootRequestId, config.BootResponseId,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), "Normal");
            await using var appLink = new CanHubIsoTpLink(_device.Hub!, app, lease);
            await using var bootLink = new CanHubIsoTpLink(_device.Hub!, boot, lease);
            for (int count = 0; _device.Hub!.SubscriberCount < 2 && count < 100; count++)
                await Task.Delay(10, ct);
            if (_device.Hub!.SubscriberCount < 2)
                throw new IOException("CAN1 RX APP/Boot filters failed to subscribe.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var isoOpts = new IsoTpOptions(TimeSpan.FromSeconds(2));
            async Task<byte[]> ProbeOne(
                string name, IsoTpConversation conversation, CancellationToken token)
            {
                Emit("PROBE_" + name, "TX", "Real CAN1 ISO-TP 02 10 03 on configured endpoint.");
                await conversation.SendPduAsync(new byte[]{0x10, 0x03}, token);
                var rx = await conversation.ReceivePduAsync(TimeSpan.FromSeconds(2), token);
                Emit("PROBE_" + name, "RX", Convert.ToHexString(rx));
                if (rx.Length < 2 || rx[0] != 0x50 || rx[1] != 0x03)
                    throw new InvalidDataException(name + " 0x10 03 did not receive matching 0x50 03.");
                return rx;
            }
            try
            {
                byte[] resp = await ProbeOne("BOOT",
                    new IsoTpConversation(bootLink, isoOpts), ct);
                Emit("PROBE_FINISHED", "BOOT", "Boot target answered 0x50 03.");
                return new("BOOT", Convert.ToHexString(resp), null);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException)
            {
                Emit("PROBE_BOOT", "NO_VALID_RESPONSE", ex.Message);
            }
            try
            {
                byte[] resp = await ProbeOne("APP",
                    new IsoTpConversation(appLink, isoOpts), ct);
                Emit("PROBE_FINISHED", "APP", "APP target answered 0x50 03.");
                return new("APP", Convert.ToHexString(resp), null);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException)
            {
                Emit("PROBE_FINISHED", "NO_VALID_ECU_RESPONSE", ex.Message);
                return new("NO_RESPONSE", "", ex.Message);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Emit("PROBE_FINISHED", "CANCELLED", "Physical CAN1 probe cancelled.");
            return new("CANCELLED", "", "No erase/download was issued.");
        }
        catch (Exception ex)
        {
            Emit("PROBE_FINISHED", "FAILED", ex.GetType().Name + ": " + ex.Message);
            return new("FAILED", "", ex.Message);
        }
        finally
        {
            permit?.Dispose();
            if (acquired)
                await _device.PhysicalDutCanLeases.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    public static ushort CanToolChecksum(ReadOnlySpan<byte> data)
    {
        uint sum=0;
        foreach (var b in data) sum += b;
        return unchecked((ushort)~sum);
    }
}
