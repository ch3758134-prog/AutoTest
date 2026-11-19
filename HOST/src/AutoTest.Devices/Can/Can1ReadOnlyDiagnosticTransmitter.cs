using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Can;

/// <summary>CAN1 only; the USB physical fence independently rejects any frame
/// outside the one exact consented 0x22 request and FF-bound ISO-TP FC.</summary>
public sealed class Can1ReadOnlyDiagnosticTransmitter(
    UsbSessionService usb, CanTransmitGate leases) 
{
    private static readonly BusRoleMap PortMap = new(default, 1, 0);

    public async ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
        CanTxRequest request, LeaseToken lease, CancellationToken ct)
    {
        if (request.Frame.Role != BusRole.DutCan || !leases.Validate(BusRole.DutCan, lease) ||
            lease.SessionGeneration != usb.Generation || usb.State != UsbSessionState.Connected)
            return OperationResult<CanTxReceipt>.Rejected("DID_SCOPE_OR_LEASE",
                "Connected CAN1 and a valid current-generation UDS lease are mandatory.");
        try
        {
            var payload = CanFrameCodec.ToUsbPayload(request.Frame, PortMap);
            await usb.RequestAsync(MainControllerCodec.CanSend,payload,TimeSpan.FromSeconds(3),ct)
                .ConfigureAwait(false);
            return OperationResult<CanTxReceipt>.Accepted(
                new CanTxReceipt(request.CorrelationId, AckKind.DeviceAcknowledged,
                    TimePoint.FromHostArrival(DateTimeOffset.UtcNow,0), EvidenceRef.New()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException ex)
        { return OperationResult<CanTxReceipt>.Rejected("PHYSICAL_FENCE",ex.Message); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        { return OperationResult<CanTxReceipt>.Uncertain("DID_CAN_TX_UNKNOWN",ex.Message); }
    }
}
