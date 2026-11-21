using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>Real USB frame codecs and production fence; no fake DUT output.</summary>
public sealed class PhysicalEmergencyInterlockTests
{
    [Fact]
    public void UnexpectedUiFailureRevokesOldAndFuturePhysicalWrites()
    {
        var fence = new PhysicalCommandFence();
        var ign = UsbWireCodec.EncodeRequest(1, MainControllerCodec.SetIgn, new byte[] { 1 });
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 7);
        fence.EmergencyInterlock();
        Assert.True(fence.IsEmergencyInterlocked);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(ign, 7));

        // Reset is not an escape hatch for a faulted live USB link.
        fence.Reset();
        fence.AuthorizeSingleIgn(true, "AUTHORIZE IGN ON", 7);
        Assert.Throws<UnauthorizedAccessException>(() =>
            fence.AssertAllowedAndConsume(ign, 7));
        // Diagnostic reads remain available for evidence gathering.
        var read = UsbWireCodec.EncodeRequest(2, MainControllerCodec.GetClamps,
            ReadOnlySpan<byte>.Empty);
        fence.AssertAllowedAndConsume(read, 7);
    }
}
