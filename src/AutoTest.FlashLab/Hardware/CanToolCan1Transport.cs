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
    Func<Guid> activeProgrammingPermit)
{
    private static readonly BusRoleMap Ports=new(default,1,0);

    public async ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
        CanTxRequest request,LeaseToken lease,CancellationToken ct)
    {
        if(request.Frame.Role!=BusRole.DutCan ||
           session.State!=UsbSessionState.Connected ||
           lease.SessionGeneration!=session.Generation ||
           !leases.Validate(BusRole.DutCan,lease))
            return OperationResult<CanTxReceipt>.Rejected(
                "CANTOOL_CAN1_LEASE","仅允许当前 WinUSB 会话持有者向物理 CAN1 发送。");

        byte[] data;
        try {data=CanFrameCodec.ToUsbPayload(request.Frame,Ports);}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        {return OperationResult<CanTxReceipt>.Rejected("CANTOOL_CAN_FRAME",ex.Message);}

        try
        {
            // During a flash, each physical USB frame must have one exact,
            // single-use grant. Outside flash, the F1 one-DID physical fence
            // remains the only path, and it independently rejects other SIDs.
            var token=activeProgrammingPermit();
            if(token!=Guid.Empty)
                physical.PrepareExactFlashFrame(token,data);
            await session.RequestAsync(MainControllerCodec.CanSend,data,
                TimeSpan.FromSeconds(3),ct).ConfigureAwait(false);
            return OperationResult<CanTxReceipt>.Accepted(new CanTxReceipt(
                request.CorrelationId,AckKind.DeviceAcknowledged,
                TimePoint.FromHostArrival(DateTimeOffset.UtcNow,0),EvidenceRef.New()));
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {throw;}
        catch(UnauthorizedAccessException ex)
        {return OperationResult<CanTxReceipt>.Rejected("CANTOOL_PHYSICAL_FENCE",ex.Message);}
        catch(Exception ex) when(ex is IOException or TimeoutException or InvalidOperationException
            or OperationCanceledException)
        {
            // USB ACK unknown does not prove that CAN1 never transmitted.
            return OperationResult<CanTxReceipt>.Uncertain("CANTOOL_TX_EFFECT_UNKNOWN",
                ex.Message);
        }
    }
}
