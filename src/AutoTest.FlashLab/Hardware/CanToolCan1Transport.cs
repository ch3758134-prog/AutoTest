using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.FlashLab.Hardware;

/// <summary>
/// The one actual CAN send adapter used by both CanTool APP/BOOT traffic
/// and explicitly consented F1 read-only diagnostics.
/// Reference CanTool's CAN adapter is replaced only here:
/// CAN frame -> 15-byte main-controller USB CAN1 command (0x40) -> native WinUSB.
/// RX is the existing physical USB 0x41 -> HardwareCanReceiveBridge -> CanFrameHub.
/// ISO-TP/UDS remain AutoTest.Specialists and are not reimplemented.
/// </summary>
public sealed class CanToolCan1Transport(
    UsbSessionService session,WinUsbByteLink physical,CanTransmitGate leases,
    Func<Guid> activeProgrammingPermit,
    Action<string,CanFrame,string> trace)
{
    private static readonly BusRoleMap Ports=new(default,1,0);

    /// <summary>
    /// Real ECU programming frames are sent as classic CAN DLC=8 with 00
    /// padding after the ISO-TP data. This is applied exclusively to the
    /// physically-authorized FLASH path, not the generic UDS/ISO-TP stack or
    /// the separately fenced F1 one-DID diagnostic path.
    /// The original CanTool padding byte still needs a real trace comparison.
    /// </summary>
    public static CanFrame PadProgrammingFrame(CanFrame original)
    {
        if(original.Dlc!=original.Data.Length || original.Dlc is < 1 or > 8)
            throw new ArgumentException("Invalid classic CAN frame DLC.",nameof(original));
        if(original.Dlc==8)return original;
        Span<byte> bytes=stackalloc byte[8];
        original.Data.AsSpan().CopyTo(bytes);
        // Span stackalloc contents are not guaranteed to be initialized.
        bytes.Slice(original.Dlc).Clear();
        return CanFrame.Create(original.Role,original.ExtendedId,original.Id,bytes);
    }

    public async ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
        CanTxRequest request,LeaseToken lease,CancellationToken ct)
    {
        if(request.Frame.Role!=BusRole.DutCan ||
           session.State!=UsbSessionState.Connected ||
           lease.SessionGeneration!=session.Generation ||
           !leases.Validate(BusRole.DutCan,lease))
            return OperationResult<CanTxReceipt>.Rejected(
                "CANTOOL_CAN1_LEASE","仅允许当前 WinUSB 会话持有者向物理 CAN1 发送。");

        // The immutable job grants a live programming token before S1.
        // Apply padding BEFORE the physical USB frame, and log exactly that
        // encoded CAN frame, never the unpadded logical ISO-TP PDU.
        Guid token=activeProgrammingPermit();
        CanFrame onWire;
        byte[] data;
        try
        {
            onWire=token!=Guid.Empty
                ? PadProgrammingFrame(request.Frame)
                : request.Frame;
            data=CanFrameCodec.ToUsbPayload(onWire,Ports);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        {
            trace("TX 拒绝",request.Frame,ex.Message);
            return OperationResult<CanTxReceipt>.Rejected("CANTOOL_CAN_FRAME",ex.Message);
        }

        try
        {
            // F2 requires an exact, single-use physical frame permit.
            // Non-flash F1 DID traffic retains its existing independent fence.
            if(token!=Guid.Empty)
                physical.PrepareExactFlashFrame(token,data);
            await session.RequestAsync(MainControllerCodec.CanSend,data,
                TimeSpan.FromSeconds(3),ct).ConfigureAwait(false);
            // ONE visible CAN TX row per physical 0x40 request. USB ACK is a
            // result field, not another CAN frame or an ECU acknowledgement.
            trace("TX",onWire,"主控 USB ACK（非 ECU 回复）");
            return OperationResult<CanTxReceipt>.Accepted(new CanTxReceipt(
                request.CorrelationId,AckKind.DeviceAcknowledged,
                TimePoint.FromHostArrival(DateTimeOffset.UtcNow,0),EvidenceRef.New()));
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            trace("TX 未确认",onWire,"操作取消；CAN 实际发送状态未知");
            throw;
        }
        catch(UnauthorizedAccessException ex)
        {
            trace("TX 拒绝",onWire,ex.Message);
            return OperationResult<CanTxReceipt>.Rejected("CANTOOL_PHYSICAL_FENCE",ex.Message);
        }
        catch(Exception ex) when(ex is IOException or TimeoutException or InvalidOperationException
            or OperationCanceledException)
        {
            // USB ACK missing never proves CAN1 did not transmit.
            trace("TX 未确认",onWire,ex.GetType().Name+": "+ex.Message);
            return OperationResult<CanTxReceipt>.Uncertain("CANTOOL_TX_EFFECT_UNKNOWN",
                ex.Message);
        }
    }
}
