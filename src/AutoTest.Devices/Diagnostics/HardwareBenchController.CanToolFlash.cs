using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Dedicated, bounded CAN1 writer for the standalone CanTool Flash laboratory.
/// The ONLY physical CAN path remains the existing WinUSB session / CAN_SEND
/// and the SAME CanFrameHub used by the device receiver.
/// A lease alone does NOT authorize transmission; the operator must authorize
/// an exact profile + image + session. No path is available to the R1 TEST UI.
/// </summary>
public sealed partial class HardwareBenchController
{
    private readonly object _flashPermitLock = new();
    private CanToolFlashPermit? _activeFlashPermit;
    private sealed class CanToolFlashPermit
    {
        public required LeaseToken Lease;
        public required long Generation;
        public required uint AppTx;
        public required uint BootTx;
        public required DateTimeOffset Expires;
        public required string ConfigurationHash;
        public required string ImageHash;
        public int RemainingFrames;
        public bool AwaitingConsecutive;
        public bool ProbeOnly;
        public bool Disposed;
    }
    private sealed class FlashPermitHandle(HardwareBenchController owner, CanToolFlashPermit permit)
        : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._flashPermitLock)
            {
                permit.Disposed = true;
                if (ReferenceEquals(owner._activeFlashPermit, permit))
                    owner._activeFlashPermit = null;
            }
        }
    }

    public bool PhysicalCanToolFlashActive
    {
        get { lock (_flashPermitLock) return _activeFlashPermit is { Disposed: false }; }
    }

    public static string PhysicalCanToolConfirmation(
        string configurationSha256, string firmwareSha256,
        uint appCanId, uint bootCanId, long generation)
    {
        static bool IsHash(string value) =>
            value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (!IsHash(configurationSha256) || !IsHash(firmwareSha256) ||
            appCanId is 0 or > 0x1FFFFFFF || bootCanId is 0 or > 0x1FFFFFFF ||
            generation <= 0)
            throw new ArgumentException("Exact configuration, image, CAN identifiers and USB generation are mandatory.");
        return $"AUTHORIZE CANTOOL FLASH CAN1 APP=0x{appCanId:X} BOOT=0x{bootCanId:X} " +
            $"CONFIG={configurationSha256[..16].ToUpperInvariant()} IMAGE={firmwareSha256[..16].ToUpperInvariant()} GEN={generation}";
    }

    /// <summary>
    /// The caller acquires an exclusive DUT CAN1 lease, freezes the
    /// Configuration.xml and firmware SHA256, displays the complete target and
    /// erase range, and requires a correctly typed confirmation.
    /// No frame is emitted by this method. Limit is ≤30 min / ≤250K CAN frames.
    /// </summary>
    public IDisposable AuthorizePhysicalCanToolFlash(
        LeaseToken lease, uint appRequestId, uint bootRequestId,
        string configurationSha256, string firmwareSha256,
        string typedConfirmation, TimeSpan lifetime, int maxFrames)
    {
        string expected = PhysicalCanToolConfirmation(
            configurationSha256, firmwareSha256, appRequestId, bootRequestId, lease.SessionGeneration);
        if (!string.Equals(typedConfirmation, expected, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Exact typed CanTool programming authorization was not provided.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(30) ||
            maxFrames is < 1 or > 250_000)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Programming grant must be bounded.");
        var session = _session;
        if (session is null || session.State != UsbSessionState.Connected ||
            !_versionVerified || _link is null || _hub is null)
            throw new IOException("Verified physical main controller WinUSB session is required.");
        if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0 ||
            CanDropped != 0 || CanMalformed != 0 || HubDropped != 0)
            throw new IOException("USB/CAN evidence is incomplete; refuse physical programming.");
        if (_physicalUdsLeases is null ||
            !_physicalUdsLeases.Validate(BusRole.DutCan, lease) ||
            session.Generation != lease.SessionGeneration)
            throw new UnauthorizedAccessException("Exclusive current-generation CAN1 lease required.");
        lock (_flashPermitLock)
        {
            if (_activeFlashPermit is { Disposed: false })
                throw new InvalidOperationException("Physical CAN1 programming already active.");
            _physicalUdsBoundary.Revoke();
            var permit = new CanToolFlashPermit
            {
                Lease = lease, Generation = session.Generation,
                AppTx = appRequestId, BootTx = bootRequestId,
                ConfigurationHash = configurationSha256,
                ImageHash = firmwareSha256,
                Expires = DateTimeOffset.UtcNow.Add(lifetime),
                RemainingFrames = maxFrames
            };
            _activeFlashPermit = permit;
            _log.RecordMessage("FLASH_LAB_AUTHORIZE",
                $"CAN1 gen={session.Generation}; APP={appRequestId:X}; BOOT={bootRequestId:X}; " +
                $"configSha={configurationSha256}; imageSha={firmwareSha256}; maxFrames={maxFrames}");
            return new FlashPermitHandle(this, permit);
        }
    }

    public static string PhysicalCanToolProbeConfirmation(
        string configurationSha256, uint appCanId, uint bootCanId, long generation)
    {
        if (configurationSha256 is not { Length: 64 } ||
            !configurationSha256.All(Uri.IsHexDigit) ||
            appCanId is 0 or > 0x1FFFFFFF ||
            bootCanId is 0 or > 0x1FFFFFFF || generation <= 0)
            throw new ArgumentException("Invalid CanTool probe configuration or USB generation.");
        return $"AUTHORIZE CANTOOL PROBE CAN1 APP=0x{appCanId:X} BOOT=0x{bootCanId:X} " +
            $"CONFIG={configurationSha256[..16].ToUpperInvariant()} GEN={generation}";
    }

    /// <summary>
    /// Single-generation, four-frame, short-lived actual UDS 10 03 probe.
    /// It cannot erase, unlock, download, reset or send arbitrary UDS data.
    /// Diagnostic-session probing itself can change ECU session state.
    /// </summary>
    public IDisposable AuthorizePhysicalCanToolProbe(
        LeaseToken lease, uint appRequestId, uint bootRequestId,
        string configurationSha256, string typedConfirmation)
    {
        var expected = PhysicalCanToolProbeConfirmation(
            configurationSha256, appRequestId, bootRequestId, lease.SessionGeneration);
        if (!string.Equals(typedConfirmation, expected, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Explicit physical 0x10 03 probe confirmation required.");
        var session = _session;
        if (session is null || session.State != UsbSessionState.Connected ||
            !_versionVerified || _link is null || _hub is null ||
            session.Generation != lease.SessionGeneration)
            throw new IOException("Current verified main controller is required.");
        if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0 ||
            CanDropped != 0 || CanMalformed != 0 || HubDropped != 0)
            throw new IOException("Physical CAN1 trace lost; refuse probe.");
        if (_physicalUdsLeases is null || !_physicalUdsLeases.Validate(BusRole.DutCan, lease))
            throw new UnauthorizedAccessException("Exclusive CAN1 lease is required for physical probe.");
        lock (_flashPermitLock)
        {
            if (_activeFlashPermit is { Disposed: false })
                throw new InvalidOperationException("Another CanTool Flash or probe session is active.");
            _physicalUdsBoundary.Revoke();
            var permit = new CanToolFlashPermit
            {
                Lease = lease, Generation = session.Generation,
                AppTx = appRequestId, BootTx = bootRequestId,
                ConfigurationHash = configurationSha256,
                ImageHash = "PROBE_ONLY",
                Expires = DateTimeOffset.UtcNow.AddSeconds(40),
                RemainingFrames = 4,
                ProbeOnly = true
            };
            _activeFlashPermit = permit;
            _log.RecordMessage("FLASH_LAB_UDS_PROBE_AUTHORIZE",
                $"CAN1 probe only 10 03; gen={session.Generation}; APP={appRequestId:X}; " +
                $"BOOT={bootRequestId:X}; XML_SHA={configurationSha256}");
            return new FlashPermitHandle(this, permit);
        }
    }

    private void RevokePhysicalCanToolFlash()
    {
        lock (_flashPermitLock)
        {
            if (_activeFlashPermit is not null) _activeFlashPermit.Disposed = true;
            _activeFlashPermit = null;
        }
    }

    private bool TryAuthorizeCanToolFrame(CanTxRequest tx, LeaseToken lease, long generation,
        out string error)
    {
        lock (_flashPermitLock)
        {
            error = "FLASH_PERMIT_MISSING";
            var permit = _activeFlashPermit;
            if (permit is null || permit.Disposed) return false;
            if (permit.Generation != generation || permit.Lease != lease ||
                permit.Expires <= DateTimeOffset.UtcNow)
            {
                error = "FLASH_SCOPE_STALE_OR_EXPIRED";
                return false;
            }
            if (permit.RemainingFrames <= 0)
            {
                error = "FLASH_CAN_FRAME_BUDGET_EXHAUSTED";
                permit.Disposed = true;
                return false;
            }
            var frame = tx.Frame;
            if (frame.Role != BusRole.DutCan ||
                (frame.Id != permit.AppTx && frame.Id != permit.BootTx) ||
                frame.ExtendedId != (frame.Id > 0x7FF) ||
                frame.Dlc != frame.Data.Length || frame.Data.Length < 1)
            {
                error = "FLASH_CAN1_ENDPOINT_MISMATCH";
                return false;
            }
            // Enforce a restricted ISO-TP envelope. The lab workflow issues
            // only 10/11/27/31/34/36/37; no arbitrary CAN injection.
            var bytes = frame.Data.AsSpan();
            if (permit.ProbeOnly)
            {
                if (bytes.Length < 3 || bytes[0] != 0x02 ||
                    bytes[1] != 0x10 || bytes[2] != 0x03)
                {
                    error = "PROBE_SCOPE_DENIES_NON_1003";
                    return false;
                }
                permit.RemainingFrames--;
                error = "";
                return true;
            }
            byte pci = (byte)(bytes[0] >> 4);
            if (pci is 0 or 1)
            {
                var sidIndex = pci == 0 ? 1 : 2;
                if (bytes.Length <= sidIndex ||
                    bytes[sidIndex] is not (0x10 or 0x11 or 0x27 or 0x31 or 0x34 or 0x36 or 0x37))
                {
                    error = "FLASH_UNAUTHORIZED_UDS_SERVICE";
                    return false;
                }
                permit.AwaitingConsecutive = pci == 1;
            }
            else if (pci == 2)
            {
                if (!permit.AwaitingConsecutive)
                {
                    error = "FLASH_ORPHAN_ISOTP_CONSECUTIVE";
                    return false;
                }
            }
            else if (pci != 3)
            {
                error = "FLASH_INVALID_ISOTP_PCI";
                return false;
            }
            permit.RemainingFrames--;
            error = "";
            return true;
        }
    }

    private async ValueTask<OperationResult<CanTxReceipt>> TransmitCanToolFlashFrameAsync(
        CanTxRequest tx, LeaseToken lease, UsbSessionService session,
        WinUsbByteLink link, CancellationToken ct)
    {
        if (_physicalUdsLeases is null ||
            !_physicalUdsLeases.Validate(BusRole.DutCan, lease) ||
            !ReferenceEquals(_session, session) || !_versionVerified ||
            _log is null || _log.Dropped != 0 || _log.FailureMessage is not null ||
            CanDropped != 0 || CanMalformed != 0 || HubDropped != 0)
        {
            RevokePhysicalCanToolFlash();
            return OperationResult<CanTxReceipt>.Rejected(
                "FLASH_SESSION_OR_TRACE_UNHEALTHY", "Physical USB/CAN1 session, lease or trace unavailable.");
        }
        if (!TryAuthorizeCanToolFrame(tx, lease, session.Generation, out var failure))
            return OperationResult<CanTxReceipt>.Rejected(failure, "CAN1 programming frame denied by active job scope.");
        byte[] raw;
        try { raw = CanFrameCodec.ToUsbPayload(tx.Frame,
            new BusRoleMap(default, DutPort: 1, KeypadPort: 0)); }
        catch (Exception ex)
        { return OperationResult<CanTxReceipt>.Rejected("FLASH_CAN_FRAME_INVALID", ex.Message); }
        string hex = Convert.ToHexString(raw);
        _log.RecordMessage("FLASH_CAN1_TX_INTENT",
            $"gen={session.Generation}; cid={tx.CorrelationId:N}; canId={tx.Frame.Id:X}; " +
            $"dlc={tx.Frame.Dlc}; rawUsb={hex}; local trace contains firmware/security data");
        if (_log.Dropped != 0 || _log.FailureMessage is not null)
        {
            RevokePhysicalCanToolFlash();
            return OperationResult<CanTxReceipt>.Rejected("FLASH_TRACE_NOT_DURABLE",
                "Unable to record CAN TX intent.");
        }
        try
        {
            link.AuthorizeOneCanTransmission(raw, "AUTHORIZE CAN TX " + hex);
            var ack = await session.RequestAsync(
                MainControllerCodec.CanSend, raw, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || !ReferenceEquals(_session, session) ||
                session.State != UsbSessionState.Connected || ack.Command != MainControllerCodec.CanSend ||
                ack.Payload.Length != 0 || !PhysicalCanToolFlashActive ||
                session.Generation != lease.SessionGeneration)
                throw new IOException("Flash CAN frame USB ACK/session changed; actual transmission uncertain.");
            _log?.RecordMessage("FLASH_CAN1_MAIN_ACK",
                $"gen={session.Generation}; cid={tx.CorrelationId:N}; USB_SEQ={ack.Sequence}; " +
                "main-controller ACK, not ECU UDS response");
            return OperationResult<CanTxReceipt>.Accepted(
                new CanTxReceipt(tx.CorrelationId, AckKind.DeviceAcknowledged,
                    TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()),
                    EvidenceRef.New()), tx.CorrelationId);
        }
        catch (Exception ex)
        {
            _log?.RecordMessage("FLASH_CAN1_UNCERTAIN", ex.GetType().Name + ": " + ex.Message);
            RevokePhysicalCanToolFlash();
            return OperationResult<CanTxReceipt>.Uncertain("FLASH_CAN_TX_UNCERTAIN",
                "CAN1 transmission / USB ACK uncertain; inspect ECU before retrying.", tx.CorrelationId);
        }
        finally { link.RevokeOneCanTransmission(); }
    }
}
