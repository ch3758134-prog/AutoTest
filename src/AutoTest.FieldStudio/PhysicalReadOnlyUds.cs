using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;
using AutoTest.Specialists.Uds;

namespace AutoTest.FieldStudio;

/// <summary>
/// One explicitly authorized, bounded, read-only physical UDS transaction.
/// USB/CAN hardware belongs exclusively to HardwareBenchController.
/// This service never creates a second WinUSB session or enables programming.
/// </summary>
internal static class PhysicalReadOnlyUds
{
    public static async Task<UdsExchangeResult> RunAsync(
        HardwareBenchController controller,
        ReadOnlyUdsPhysicalEndpoint physical,
        UdsEndpointProfile endpoint,
        byte[] exactPdu, Guid requestId, string typedAuthorization, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(controller);
        physical.Validate();
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        if (controller.State != UsbSessionState.Connected || !controller.ProtocolVersionVerified ||
            controller.Hub is null || controller.Generation <= 0)
            throw new InvalidOperationException("Controller must have verified GET_VERSION and a live shared CAN Hub.");
        if (physical.RequestCanId != endpoint.RequestCanId || physical.ResponseCanId != endpoint.ResponseCanId ||
            endpoint.Role != BusRole.DutCan)
            throw new InvalidOperationException("Current UDS profile does not match the physical CAN1 endpoint.");
        if (requestId == Guid.Empty || exactPdu.Length == 0 ||
            exactPdu[0] is not (0x19 or 0x22))
            throw new UnauthorizedAccessException("Only one explicitly authorized 0x19/0x22 request is admitted.");

        // One lease on CAN1, never a second hardware session; 60 seconds outlives
        // the bounded 25-second request permit.
        var leases = controller.PhysicalDutCanLeases;
        var acquired = await leases.AcquireAsync(BusRole.DutCan,
            "AutoTest.FieldStudio.ReadOnlyUds", TimeSpan.FromSeconds(60), ct);
        if (acquired.Outcome != Outcome.Accepted)
            throw new IOException("Cannot acquire the only DUT CAN lease: " + acquired.FailureCode);
        LeaseToken lease = acquired.Value;
        try
        {
            await using var link = new CanHubIsoTpLink(controller.Hub, endpoint, lease);

            // Give the subscriber an opportunity to be registered before the
            // first physical frame is issued. True RX loss is still detected by the
            // Device/ISO-TP path; no synthetic responses are generated here.
            await Task.Delay(80, ct);
            using var permission = controller.AuthorizePhysicalReadOnlyUds(
                physical, exactPdu, lease, requestId, TimeSpan.FromSeconds(25),
                maxFrames: 256, typedAuthorization);

            var client = new UdsClient(new IsoTpUdsMessageChannel(
                new IsoTpConversation(link, new IsoTpOptions(TimeSpan.FromMilliseconds(1500)))));
            return await client.RequestAsync(
                new UdsRequest(exactPdu.ToImmutableArray(), requestId), endpoint, ct);
        }
        finally
        {
            // Release even on failures/cancellation. A failed USB TX is
            // UNCERTAIN, not a successful ECU exchange.
            var released = await leases.ReleaseAsync(lease, CancellationToken.None);
            if (released.Outcome != Outcome.Accepted)
                throw new IOException("CAN lease release failed: " + released.FailureCode);
        }
    }
}
